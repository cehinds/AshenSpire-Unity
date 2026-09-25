// Playwright with Unity WebGL rendered by Mesa's llvmpipe Vulkan driver (lavapipe).
//
// The browser playtests pass `--use-angle=swiftshader`. Every playtest step waits
// for rendered frames, and Chromium's bundled SwiftShader draws a phone-density
// Unity canvas at about 7 frames per second on a four-core CI runner; lavapipe
// draws the same canvas at about 60. CI selects this module through the
// PLAYWRIGHT_MODULE variable every playtest already honours, so the playtests
// themselves (their inputs, waits and assertions) are unchanged.
//
// Needs the `mesa-vulkan-drivers` package. VK_ICD_FILENAMES should name
// lvp_icd.json so no other Vulkan driver is chosen. `node tools/playwright-lavapipe.cjs`
// launches a page and fails unless WebGL really reports llvmpipe, so a silent
// fallback to SwiftShader cannot go unnoticed.
const real=require(process.env.PLAYWRIGHT_REAL_MODULE||'playwright');
const lavapipe=['--use-angle=vulkan','--use-vulkan=native','--enable-features=Vulkan,DefaultANGLEVulkan,VulkanFromANGLE','--ignore-gpu-blocklist','--disable-vulkan-surface'];
function withLavapipe(options={}){
 const args=(options.args||[]).filter(arg=>arg!=='--use-angle=swiftshader'&&arg!=='--enable-unsafe-swiftshader');
 return {...options,args:[...args,...lavapipe]};
}
const launch=real.chromium.launch.bind(real.chromium);
real.chromium.launch=options=>launch(withLavapipe(options));
module.exports=real;

if(require.main===module)(async()=>{
 const browser=await real.chromium.launch({headless:true,args:['--enable-unsafe-swiftshader','--use-angle=swiftshader']});
 try{
  const page=await browser.newPage();
  const renderer=await page.evaluate(()=>{const gl=document.createElement('canvas').getContext('webgl2');const info=gl&&gl.getExtension('WEBGL_debug_renderer_info');return info?gl.getParameter(info.UNMASKED_RENDERER_WEBGL):'no WebGL 2';});
  console.log('WebGL renderer: '+renderer);
  if(!/llvmpipe/i.test(renderer)){console.error('Expected Mesa llvmpipe; is mesa-vulkan-drivers installed and VK_ICD_FILENAMES set?');process.exitCode=1;}
 }finally{await browser.close();}
})().catch(error=>{console.error(error);process.exitCode=1;});
