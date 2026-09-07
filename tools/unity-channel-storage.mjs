// Plan channel-local launch pages and byte-exact shared evidence folders.
// Immutable archives own runtime payloads. Channel document URLs never redirect,
// so Unity still sees the original channel URL for its label and save context.
// Unsupported player templates retain their complete original Web folder.
import {createHash} from 'node:crypto';
import {publishedRelativePath} from './unity-git-blobs.mjs';

const channelNames = ['dev','test','release','main'];
const objectId = /^(?:[a-f0-9]{40}|[a-f0-9]{64})$/;
const payloads = ['Web.loader.js','Web.data','Web.framework.js','Web.wasm'];
const hash = value => createHash('sha256').update(value).digest('hex');
const urlPath = value => value.split('/').map(encodeURIComponent).join('/');
const treeIdentity = files => JSON.stringify(files.map(({path,blob}) => ({path,blob})).sort((a,b) => a.path < b.path ? -1 : a.path > b.path ? 1 : 0));

export function rewriteChannelPlayer(originalHtml, archiveId) {
  if (typeof originalHtml !== 'string') throw new Error('Channel player HTML must be text');
  if (!/^build-[a-f0-9]{20}$/.test(archiveId)) throw new Error('Unsafe immutable archive ID');
  const unchanged = reason => ({html:originalHtml, rewritten:false, reason});
  // A base element or an unfamiliar template needs separate compatibility work.
  // Never rewrite document.location, product identity, streaming-assets paths,
  // loader/runtime bytes, or save configuration to reduce storage.
  if (/<base\b/i.test(originalHtml)) return unchanged('Player contains a base element');
  const patterns = [
    /(<script\b[^>]*\bsrc\s*=\s*)(["'])(Build\/Web\.loader\.js(?:\?[^"'<>\r\n]*)?)\2/gi,
    /(\bdataUrl\s*:\s*)(["'])(Build\/Web\.data(?:\?[^"'<>\r\n]*)?)\2/g,
    /(\bframeworkUrl\s*:\s*)(["'])(Build\/Web\.framework\.js(?:\?[^"'<>\r\n]*)?)\2/g,
    /(\bcodeUrl\s*:\s*)(["'])(Build\/Web\.wasm(?:\?[^"'<>\r\n]*)?)\2/g
  ];
  if (patterns.some(pattern => [...originalHtml.matchAll(pattern)].length !== 1)) return unchanged('Player does not have exactly four recognized runtime URLs');
  let html = originalHtml;
  for (const pattern of patterns) html = html.replace(pattern, (_,prefix,quote,url) => `${prefix}${quote}../../builds/${archiveId}/Web/${url}${quote}`);
  return {html, rewritten:true, archiveId, originalIndexSha256:hash(originalHtml), launchIndexSha256:hash(html)};
}

function validateFiles(files) {
  if (!Array.isArray(files)) throw new Error('Missing channel artifact tree');
  const seen = new Set(), spellings = new Map();
  for (const file of files) {
    const relative = publishedRelativePath(file.path);
    if (!objectId.test(file.blob) || file.mode != null && !['100644','100755'].includes(file.mode)) throw new Error(`Unsupported channel artifact: ${file.path}`);
    if (seen.has(relative)) throw new Error(`Duplicate channel artifact: ${file.path}`);
    seen.add(relative);
    const parts = relative.split('/');
    for (let length = 1; length <= parts.length; length++) {
      const prefix = parts.slice(0,length).join('/'), folded = prefix.toLowerCase();
      if (spellings.has(folded) && spellings.get(folded) !== prefix) throw new Error(`Case-colliding channel artifact: ${file.path}`);
      spellings.set(folded,prefix);
    }
  }
}

export function planChannelStorage(selectedChannels, historyBuilds) {
  const channels = Object.create(null), folders = new Map();
  for (const selected of selectedChannels) {
    if (!channelNames.includes(selected.channel) || channels[selected.channel]) throw new Error('Invalid or duplicate channel');
    if (!objectId.test(selected.commit)) throw new Error('Channel commit must be a complete object ID');
    validateFiles(selected.files);
    const webTree = selected.files.filter(file => file.path.startsWith('Published/Web/'));
    const archive = historyBuilds.find(build => treeIdentity(build.tree) === treeIdentity(webTree));
    if (!archive) throw new Error(`Selected ${selected.channel} Web tree has no exact immutable archive`);
    const player = rewriteChannelPlayer(selected.html, archive.id);
    if (player.rewritten && payloads.some(name => !webTree.some(file => file.path === `Published/Web/Build/${name}`))) throw new Error('Recognized player is missing a runtime payload');
    const plan = {copyPaths:[], assetUrls:Object.create(null), archiveId:archive.id, player, sharedFolders:Object.create(null)};
    channels[selected.channel] = plan;
    const groups = new Map();
    for (const file of selected.files) {
      const relative = publishedRelativePath(file.path);
      if (/\.(?:zip|apk)$/i.test(relative)) continue; // Exact downloads remain on GitHub.
      plan.assetUrls[file.path] = urlPath(relative);
      if (relative.startsWith('Web/')) {
        if (player.rewritten && payloads.some(name => relative === `Web/Build/${name}`)) {
          plan.assetUrls[file.path] = `../builds/${archive.id}/${urlPath(relative)}`;
        } else plan.copyPaths.push(file.path);
      } else if (!relative.includes('/')) {
        // Channel manifests and receipts remain independently addressable.
        plan.copyPaths.push(file.path);
      } else {
        const folder = relative.split('/')[0];
        if (!groups.has(folder)) groups.set(folder,[]);
        groups.get(folder).push(file);
      }
    }
    for (const [folder,files] of groups) {
      // Match the ENTIRE committed folder tree, including excluded downloads.
      // This preserves relative links and never combines divergent evidence.
      const wholeTree = selected.files.filter(file => file.path.startsWith(`Published/${folder}/`));
      const key = folder + ':' + treeIdentity(wholeTree);
      const owner = folders.get(key) ?? selected.channel;
      folders.set(key,owner); plan.sharedFolders[folder] = owner;
      if (owner === selected.channel) plan.copyPaths.push(...files.map(file => file.path));
      else for (const file of files) plan.assetUrls[file.path] = `../${owner}/${urlPath(publishedRelativePath(file.path))}`;
    }
  }
  return {channels};
}

export function channelAssetUrl(plan, channel, publishedPath) {
  publishedRelativePath(publishedPath);
  const url = plan.channels[channel]?.assetUrls[publishedPath];
  if (!url) throw new Error(`Artifact has no channel URL: ${channel}/${publishedPath}`);
  return url;
}

// Optional current-build guide/gallery selection. Missing metadata preserves the
// historical selectors; an explicit malformed selection must never quietly show
// old screenshots as current evidence. Paths are selected committed artifacts,
// and their public URLs still follow the exact-folder sharing plan above.
export function parseChannelPresentation(plan, channel, text) {
  if (text === undefined) return null;
  if (typeof text !== 'string' || text.length > 32768) throw new Error('Invalid presentation manifest text');
  let value;
  try { value = JSON.parse(text); } catch { throw new Error('Invalid presentation manifest JSON'); }
  if (!value || typeof value !== 'object' || Array.isArray(value) ||
      Object.keys(value).some(key => !['guide','screenshots'].includes(key)) ||
      !Array.isArray(value.screenshots) || value.screenshots.length < 1 || value.screenshots.length > 32) {
    throw new Error('Presentation requires a guide and 1 to 32 screenshots');
  }
  const requirePath = (path, extension) => {
    if (typeof path !== 'string' || path.length > 512 || !path.toLowerCase().endsWith(extension))
      throw new Error(`Presentation requires ${extension} artifact paths`);
    channelAssetUrl(plan, channel, path); // Validates spelling, safety and selected-tree membership.
    return path;
  };
  const guide = requirePath(value.guide, '.md');
  const screenshots = value.screenshots.map(path => requirePath(path, '.png'));
  if (new Set(screenshots).size !== screenshots.length) throw new Error('Duplicate presentation screenshot');
  return {guide, screenshots};
}
