// Copy committed Published files with bounded Git binary batches, not one process
// per screenshot. Only this channel-copy path uses the helper; history identity,
// archive policy and site budget remain owned by their existing components.
import {execFileSync} from 'node:child_process';
import {mkdirSync, writeFileSync, lstatSync} from 'node:fs';
import {dirname, resolve, relative, isAbsolute, sep} from 'node:path';

const mib = 1024 * 1024;
const objectId = /^(?:[a-f0-9]{40}|[a-f0-9]{64})$/;
const headerPattern = /^([a-f0-9]{40}|[a-f0-9]{64}) blob ([0-9]+)$/;

export function publishedRelativePath(path) {
  if (typeof path !== 'string' || !path.startsWith('Published/') ||
      /[\x00-\x1f\x7f\\:]/.test(path) ||
      path.split('/').some(part => !part || part === '.' || part === '..' ||
        /[. ]$/.test(part) || /^(?:con|prn|aux|nul|com[1-9]|lpt[1-9])(?:\.|$)/i.test(part))) {
    throw new Error(`Unsafe artifact path: ${JSON.stringify(path)}`);
  }
  return path.slice('Published/'.length);
}

function existingStat(path) {
  try { return lstatSync(path); } catch (error) { if (error.code === 'ENOENT') return null; throw error; }
}

function safeTarget(directory, name) {
  const root = resolve(directory), target = resolve(root, name), rel = relative(root, target);
  if (!rel || rel === '..' || rel.startsWith('..' + sep) || isAbsolute(rel)) throw new Error('Artifact escapes destination');
  // Never follow pre-existing symlink/junction output paths into another folder.
  for (let cursor = target; ; cursor = dirname(cursor)) {
    const stat = existingStat(cursor);
    if (stat?.isSymbolicLink()) throw new Error(`Symlink artifact destination: ${cursor}`);
    if (cursor === target ? stat && !stat.isFile() : stat && !stat.isDirectory()) throw new Error(`Invalid artifact destination: ${cursor}`);
    if (cursor === dirname(cursor)) break;
  }
  return target;
}

function gitBatch(root, mode, input, maxBuffer) {
  return execFileSync('git', ['cat-file', mode], {
    cwd:root, input, encoding:null, maxBuffer, windowsHide:true,
    stdio:['pipe','pipe','pipe']
  });
}

// Exported parser is tested against truncated/missing/corrupted protocol records.
// Payloads are binary views; never decode, trim or normalize the blob bytes.
export function parseBlobBatch(buffer, expected) {
  let offset = 0;
  const blobs = [];
  for (const row of expected) {
    const end = buffer.indexOf(10, offset);
    if (end < offset) throw new Error(`Missing Git blob header: ${row.path}`);
    const header = buffer.toString('ascii', offset, end);
    const match = headerPattern.exec(header);
    if (!match || match[1] !== row.oid || Number(match[2]) !== row.size) throw new Error(`Unexpected Git blob record: ${row.path}`);
    offset = end + 1;
    const next = offset + row.size;
    if (next >= buffer.length || buffer[next] !== 10) throw new Error(`Truncated Git blob payload: ${row.path}`);
    blobs.push(buffer.subarray(offset, next));
    offset = next + 1;
  }
  if (offset !== buffer.length) throw new Error('Unexpected trailing Git batch bytes');
  return blobs;
}

export function materializePublished(root, commit, paths, directory, options = {}) {
  if (!objectId.test(commit)) throw new Error('Use a resolved full commit object ID for Published materialization');
  const {batchBytes = 48 * mib, maxBlobBytes = 150 * mib} = options;
  if (!Number.isSafeInteger(batchBytes) || batchBytes <= 0 || !Number.isSafeInteger(maxBlobBytes) || maxBlobBytes < batchBytes) throw new Error('Invalid Git batch byte limits');
  const seen = new Set(), spellings = new Map();
  const files = paths.map(path => {
    const name = publishedRelativePath(path);
    if (seen.has(name)) throw new Error(`Duplicate artifact path: ${path}`);
    seen.add(name);
    // Reject casing aliases on every host, including shared directory prefixes,
    // so a Linux-assembled file list cannot overwrite or merge differently on Windows.
    const parts = name.split('/');
    for (let count = 1; count <= parts.length; count++) {
      const prefix = parts.slice(0,count).join('/'), folded = prefix.toLowerCase();
      if (spellings.has(folded) && spellings.get(folded) !== prefix) throw new Error(`Case-colliding artifact paths: ${path}`);
      spellings.set(folded,prefix);
    }
    return {path, name};
  }).filter(row => !/\.(zip|apk)$/i.test(row.path));
  if (!files.length) return {files:0, bytes:0, batches:0, gitProcesses:0};
  // Validate every destination before launching Git or writing any files.
  for (const row of files) row.target = safeTarget(directory, row.name);
  const requests = files.map(row => `${commit}:${row.path}`).join('\n') + '\n';
  const metadata = gitBatch(root, '--batch-check', requests, Buffer.byteLength(requests) + files.length * 160 + 1024);
  const lines = metadata.toString('ascii').split('\n');
  if (lines.pop() !== '' || lines.length !== files.length) throw new Error('Incomplete Git blob size response');
  for (let index = 0; index < files.length; index++) {
    const row = files[index], match = headerPattern.exec(lines[index]);
    if (!match) throw new Error(`Missing or non-blob artifact: ${row.path}`);
    row.oid = match[1]; row.size = Number(match[2]);
    if (!Number.isSafeInteger(row.size) || row.size > maxBlobBytes) throw new Error(`Artifact exceeds Git blob byte limit: ${row.path}`);
  }
  let batches = 0, bytes = 0;
  for (let first = 0; first < files.length;) {
    let last = first, payloadBytes = 0;
    do {
      payloadBytes += files[last].size;
      last++;
    } while (last < files.length && payloadBytes + files[last].size <= batchBytes);
    const rows = files.slice(first, last);
    const buffer = gitBatch(root, '--batch', rows.map(row => row.oid).join('\n') + '\n', payloadBytes + rows.length * 128 + 1024);
    const blobs = parseBlobBatch(buffer, rows);
    for (let index = 0; index < rows.length; index++) {
      const row = rows[index];
      // Recheck before writes too, since earlier outputs may introduce a path conflict.
      safeTarget(directory, row.name);
      mkdirSync(dirname(row.target), {recursive:true});
      writeFileSync(row.target, blobs[index]);
      bytes += row.size;
    }
    batches++; first = last;
  }
  return {files:files.length, bytes, batches, gitProcesses:1 + batches};
}
