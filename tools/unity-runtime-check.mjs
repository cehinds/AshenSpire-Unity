#!/usr/bin/env node
// Compile every Unity Runtime assembly against Unity's reference assemblies
// (UnityTests/RuntimeCheck) and fail on any compiler error except the known
// Unity 6 API gaps of the 2021.3 reference package listed below.
import {execFileSync} from 'node:child_process';
import {readdirSync, statSync, readFileSync, existsSync} from 'node:fs';
import {join, dirname} from 'node:path';
import {fileURLToPath} from 'node:url';

const root = join(dirname(fileURLToPath(import.meta.url)), '..');
// dotnet sees files that Unity silently skips when their metadata is malformed.
// Check import identity before treating a reference compile as useful evidence.
const assetFiles = (function walk(dir) {
  return readdirSync(dir).flatMap(name => {
    const path = join(dir,name);
    return statSync(path).isDirectory() ? walk(path) : [path];
  });
})(join(root,'Unity/Assets'));
const assetGuids = new Map();
for (const file of assetFiles) {
  if (file.endsWith('.cs') && !existsSync(file+'.meta')) throw Error('Missing Unity script metadata: '+file);
  if (!file.endsWith('.meta')) continue;
  const guid = readFileSync(file,'utf8').match(/^guid: ([0-9a-f]{32})\r?$/m)?.[1];
  if (!guid) throw Error('Invalid Unity asset GUID: '+file);
  if (assetGuids.has(guid)) throw Error('Duplicate Unity asset GUID: '+file+' and '+assetGuids.get(guid));
  assetGuids.set(guid,file);
}
console.log(`Unity import metadata: ${assetGuids.size} unique valid GUIDs.`);
// Unity 6 API used by the project but absent from UnityEngine.Modules 2021.3.
// A member on an existing sealed type cannot be shimmed, so it is accepted here.
const unity6Gaps = [/error CS1061: 'MeshGenerationContext' does not contain a definition for 'painter2D'/];

const sources = (function walk(dir) {
  return readdirSync(dir).flatMap(name => {
    const path = join(dir, name);
    return statSync(path).isDirectory() ? walk(path) : name.endsWith('.cs') ? [path] : [];
  });
})(join(root, 'Unity/Assets/AshenSpire/Runtime'));

let output = '';
try {
  output = execFileSync('dotnet', ['build', join(root, 'UnityTests/RuntimeCheck'), '-nologo', '-clp:NoSummary'], {encoding: 'utf8', stdio: ['ignore', 'pipe', 'pipe']});
} catch (error) {
  output = `${error.stdout ?? ''}${error.stderr ?? ''}`;
}
const errors = [...new Set(output.split('\n').filter(line => / error [A-Z]+\d+:/.test(line)).map(line => line.replace(/\s+\[[^\]]+\]\s*$/, '').replace(/^.*\/Unity\/Assets\/AshenSpire\//, '')))];
const unexpected = errors.filter(line => !unity6Gaps.some(gap => gap.test(line)));
const accepted = errors.length - unexpected.length;
if (!errors.length && !/Build succeeded/.test(output)) {
  console.error(output);
  console.error('unity-runtime-check: the build did not run');
  process.exit(2);
}
for (const line of unexpected) console.error(line);
if (unexpected.length) {
  console.log(`unity-runtime-check: ${sources.length} passed, ${unexpected.length} failed`);
  process.exit(1);
}
if (accepted) console.log(`Accepted ${accepted} known Unity 6 API gap(s) of the 2021.3 reference package.`);
console.log(`unity-runtime-check: OK — ${sources.length} checks passed.`);
