// Build immutable browser archives from committed Published artifacts only.
// Channel membership comes from first-parent Git history. Native downloads stay
// linked to their exact commit so Pages does not duplicate every APK/ZIP.
import {execFileSync} from 'node:child_process';
import {createHash} from 'node:crypto';
import {mkdirSync, writeFileSync, existsSync, readFileSync} from 'node:fs';
import {dirname, join} from 'node:path';
import {planArchiveHosting} from './unity-archive-hosting.mjs';

export const historyChannels = ['dev', 'test', 'release', 'main'];
const hash = data => createHash('sha256').update(data).digest('hex');
const git = (root, args, encoding = 'utf8') => execFileSync('git', args, {cwd:root, encoding, maxBuffer:150*1024*1024, stdio:['ignore','pipe','pipe']});
export function safeArtifactPath(path) {
  if (!path.startsWith('Published/') || path.includes('\\') || path.split('/').some(p => !p || p === '.' || p === '..') || /[\x00-\x1f]/.test(path)) throw new Error(`Unsafe artifact path: ${path}`);
  return path.slice(10);
}
function jsonAt(root, commit, file) {
  return JSON.parse(git(root, ['show', `${commit}:${file}`]));
}
function changesAt(root, commit) {
  let changes;
  try { changes = jsonAt(root, commit, 'Published/changelog.json'); } catch { return {}; }
  return Object.fromEntries(['Added','Changed','Fixed'].map(key => [key, Array.isArray(changes[key]) ? changes[key].filter(v => typeof v === 'string') : []]));
}
export function collectHistory(root, refs = Object.fromEntries(historyChannels.map(c => [c, `origin/${c}`]))) {
  const builds = new Map(), channels = Object.create(null);
  // Default channels stay present; named preview refs add membership without
  // changing promotion or deduplicating away their own history list.
  for (const channel of new Set([...historyChannels,...Object.keys(refs)])) {
    const ref = refs[channel];
    if (!ref) { channels[channel] = []; continue; }
    let tip;
    try { tip = git(root, ['rev-parse', '--verify', `${ref}^{commit}`]).trim(); }
    catch { channels[channel] = []; continue; }
    const commits = git(root, ['log','--first-parent','--reverse','--format=%H',tip,'--','Published/build.json']).trim().split('\n').filter(Boolean);
    const members = [];
    for (const commit of commits) {
      let manifestText;
      try { manifestText = git(root, ['show',`${commit}:Published/build.json`]); } catch { continue; }
      const manifest = JSON.parse(manifestText);
      const tree = git(root, ['ls-tree','-r',commit,'--','Published/Web']).trim().split('\n').filter(Boolean).map(line => {
        const match = /^(\d+) blob ([a-f0-9]+)\t(.+)$/.exec(line);
        if (!match || match[1] !== '100644') throw new Error(`Unsupported Web artifact at ${commit}: ${line}`);
        safeArtifactPath(match[3]);
        return {blob:match[2], path:match[3]};
      });
      if (!tree.some(f => f.path === 'Published/Web/index.html')) continue;
      // Actual committed Web blobs identify the player, independent of evidence
      // edits, version-label corrections, channel promotion and checkout date.
      const id = `build-${hash(JSON.stringify(tree)).slice(0,20)}`;
      if (!members.includes(id)) members.push(id);
      if (builds.has(id)) continue;
      const paths = git(root, ['ls-tree','-r','--name-only',commit,'--','Published']).trim().split('\n').filter(Boolean);
      builds.set(id, {id, commit, manifest, tree, paths, changes:changesAt(root,commit), pullRequests:[],
        buildNumber:manifest.buildNumber ?? `legacy-${String(manifest.version)}-${id.slice(-8)}`,
        builtAt:manifest.builtAt ?? null});
    }
    channels[channel] = members;
  }
  return {builds:[...builds.values()], channels};
}

export async function resolvePullRequests(builds, repository, token = process.env.GITHUB_TOKEN) {
  if (!/^[\w.-]+\/[\w.-]+$/.test(repository)) throw new Error('Invalid GitHub repository name');
  for (const build of builds) {
    const declared = build.manifest.pullRequest;
    const number = typeof declared === 'number' ? declared : declared?.number;
    if (Number.isSafeInteger(number) && number > 0) {
      build.pullRequests = [{number, url:`https://github.com/${repository}/pull/${number}`, state:'unknown',draft:null,merged:null,provenance:'build manifest'}];
      build.pullRequestStatus = 'declared: PR state not verified';
      continue;
    }
    if (!token) { build.pullRequestStatus = 'unknown: no GitHub API credential supplied'; continue; }
    try {
      const response = await fetch(`https://api.github.com/repos/${repository}/commits/${build.commit}/pulls`, {
        headers:{Authorization:`Bearer ${token}`, Accept:'application/vnd.github+json', 'X-GitHub-Api-Version':'2022-11-28'}, signal:AbortSignal.timeout(15000)
      });
      if (!response.ok) throw new Error(`HTTP ${response.status}`);
      const result = await response.json();
      if (!Array.isArray(result)) throw new Error('Malformed associated PR response');
      build.pullRequests = result.filter(pr => Number.isSafeInteger(pr.number) && pr.number > 0 && pr.base?.repo?.full_name?.toLowerCase() === repository.toLowerCase()).map(pr => ({
        number:pr.number,url:`https://github.com/${repository}/pull/${pr.number}`,
        state:pr.merged_at?'merged':['open','closed'].includes(pr.state)?pr.state:'unknown',
        draft:typeof pr.draft==='boolean'?pr.draft:null,merged:Boolean(pr.merged_at),provenance:'GitHub commit associated pull requests'}));
      build.pullRequestStatus = build.pullRequests.length ? 'verified' : 'unknown: no associated PR found';
    } catch (error) { build.pullRequestStatus = `unknown: GitHub lookup failed (${error.message})`; }
  }
}

function writeImmutable(path, bytes) {
  if (existsSync(path)) {
    if (!readFileSync(path).equals(Buffer.from(bytes))) throw new Error(`Archive collision: refusing to overwrite ${path}`);
    return;
  }
  mkdirSync(dirname(path), {recursive:true}); writeFileSync(path, bytes);
}
export function materializeHistory(root, out, history, {remoteRuntime=false} = {}) {
  let bytes = 0;
  for (const build of history.builds) {
    let hosting;
    if (remoteRuntime) {
      if (!/^build-[a-f0-9]{20}$/.test(build.id)) throw new Error('Unsafe archive ID');
      const originalIndex = git(root,['show',`${build.commit}:Published/Web/index.html`],null);
      hosting = planArchiveHosting(originalIndex.toString('utf8'),build);
    }
    const omitted = new Set(hosting?.omittedPaths ?? []);
    const tree = new Map(build.tree.map(file => [file.path,file.blob]));
    const files = [...build.tree.map(f => f.path), ...['build.json','changelog.json','validation.json'].map(f => `Published/${f}`).filter(p => build.paths.includes(p))];
    for (const path of files) {
      const relative = safeArtifactPath(path);
      const content = git(root, ['show',`${build.commit}:${path}`], null);
      if (remoteRuntime && tree.has(path)) {
        const blob = tree.get(path);
        const actual = createHash(blob.length === 64 ? 'sha256' : 'sha1').update(`blob ${content.length}\0`).update(content).digest('hex');
        if (actual !== blob) throw new Error(`Archived Git blob mismatch: ${build.commit}/${relative}`);
      }
      const expected = build.manifest.files?.[relative];
      if (expected && hash(content) !== expected) throw new Error(`Archived artifact hash mismatch: ${build.commit}/${relative}`);
      if (omitted.has(path)) continue; // Omission happens only AFTER exact-byte validation.
      const hosted = hosting && path === 'Published/Web/index.html' ? Buffer.from(hosting.html,'utf8') : content;
      writeImmutable(join(out,'builds',build.id,relative), hosted); bytes += hosted.length;
    }
    if (hosting) {
      const receipt = Buffer.from(JSON.stringify(hosting.receipt,null,2)+'\n','utf8');
      writeImmutable(join(out,'builds',build.id,'hosting.json'),receipt); bytes += receipt.length;
    }
  }
  return bytes;
}

export function publicHistory(history) {
  return {schemaVersion:1, channels:history.channels, builds:history.builds.map(({tree,paths,...build}) => ({...build,
    player:`builds/${build.id}/Web/`, archivedManifest:`builds/${build.id}/build.json`}))};
}
