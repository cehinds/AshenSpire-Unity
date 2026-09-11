// Match BuildTools.SourceDigest: normalize relative paths BEFORE ordinal sorting.
// Text hashes use LF; image/font bytes remain unchanged. No platform receipts or
// game assets are modified here. readBytes receives the canonical relative path.
import {createHash} from 'node:crypto';

export function unitySourceDigest(relativePaths,readBytes){
 const paths=relativePaths.map(path=>path.replaceAll('\\','/')).sort();
 const hash=createHash('sha256');
 for(const path of paths){
  hash.update(path);
  const bytes=readBytes(path);
  hash.update(/\.(png|jpg|webp|ttf|otf)$/i.test(path)?bytes:bytes.toString('utf8').replaceAll('\r\n','\n'));
 }
 return hash.digest('hex');
}
