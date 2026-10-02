// Later review captures can describe an immutable player without rewriting it.
import {execFileSync} from 'node:child_process';
import {createHash} from 'node:crypto';
import {safeArtifactPath} from './unity-build-history.mjs';

export function attachBuildReviews(root,history,refs){
 const git=args=>execFileSync('git',args,{cwd:root,encoding:'utf8',stdio:['ignore','pipe','pipe'],maxBuffer:20*1024*1024}).trim();
 const read=(commit,file)=>JSON.parse(git(['show',`${commit}:${file}`]));
 const dates=new Map(),seen=new Set();
 for(const ref of refs){
  let commit,manifest,proof,presentation;
  try{
   commit=git(['rev-parse','--verify',`${ref}^{commit}`]);if(seen.has(commit))continue;seen.add(commit);
   manifest=read(commit,'Published/build.json');proof=read(commit,'Published/validation.json');presentation=read(commit,'Published/presentation.json');
  }catch{continue;} // Older checkpoints need not have review evidence.
  if(!/^[a-f0-9]{64}$/.test(proof.sourceDigest)||proof.sourceDigest!==manifest.sourceDigest||proof.version!==manifest.version||proof.buildNumber!==manifest.buildNumber)continue;
  if(!Array.isArray(proof.currentPlayerSuites)||!proof.currentPlayerSuites.length||proof.currentPlayerSuites.some(row=>row.success!==true))continue;
  const tree=git(['ls-tree','-r',commit,'--','Published/Web']).split('\n').filter(Boolean).map(line=>{
   const match=/^100644 blob ([a-f0-9]+)\t(.+)$/.exec(line);if(!match)throw Error('Unsupported review Web artifact');
   return {blob:match[1],path:match[2]};
  });
  const id='build-'+createHash('sha256').update(JSON.stringify(tree)).digest('hex').slice(0,20);
  const build=history.builds.find(row=>row.id===id);
  if(!build||build.manifest.sourceDigest!==proof.sourceDigest)continue;
  const paths=new Set(git(['ls-tree','-r','--name-only',commit,'--','Published']).split('\n'));
  if(typeof presentation.guide!=='string'||!presentation.guide.endsWith('.md')||!Array.isArray(presentation.screenshots)||!presentation.screenshots.length||presentation.screenshots.length>32)throw Error('Invalid matching review presentation');
  for(const file of [presentation.guide,...presentation.screenshots]){safeArtifactPath(file);if(!paths.has(file))throw Error('Review artifact is not committed: '+file);}
  if(presentation.screenshots.some(file=>!/^Published\/[A-Za-z0-9_./-]+\.png$/.test(file))||new Set(presentation.screenshots).size!==presentation.screenshots.length)throw Error('Invalid review screenshot');
  const date=Number(git(['show','-s','--format=%ct',commit]));
  if(date<(dates.get(id)??-1))continue;
  dates.set(id,date);
  build.review={commit,guide:presentation.guide,screenshots:presentation.screenshots,validation:'Published/validation.json'};
 }
 return history;
}
