// Real browser/canvas regression using Node's built-in HTTP, WebSocket and CDP.
// No desktop automation or third-party browser driver is required.
import {createServer} from 'node:http';
import {spawn} from 'node:child_process';
import {readFile, writeFile, mkdir, mkdtemp} from 'node:fs/promises';
import {createHash} from 'node:crypto';
import path from 'node:path';
import {fileURLToPath, pathToFileURL} from 'node:url';
import assert from 'node:assert/strict';

const repo = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const directory = path.resolve(process.argv[2] || path.join(repo, 'TestResults/security-issue2-v2/pdfjs'));
const browserPath = process.argv[3] || 'C:/Program Files/Google/Chrome/Application/chrome.exe';
await mkdir(directory, {recursive:true});
const profile = await mkdtemp(path.join(directory, 'browser-'));
const {build}=await import(pathToFileURL(path.join(repo,'tauri/node_modules/esbuild/lib/main.js')));
const component=await build({stdin:{contents:`
import React from 'react';
import {createRoot} from 'react-dom/client';
import Preview from './src/Preview';
import {TooltipProvider} from './src/components/ui/tooltip';
import {translator} from './src/i18n';
globalThis.mountPreview=async(bytes)=>{
  const container=document.getElementById('root');
  const root=createRoot(container);let failed=false;
  root.render(<TooltipProvider><Preview data={Array.from(bytes)} cuts={[]} toggle={()=>{}} disabled={false} t={translator('en')} onError={()=>{failed=true}} view={{page:1,scale:0.8}} onView={()=>{}}/></TooltipProvider>);
  const started=Date.now();
  while(!container.textContent.includes('Rendered pages 40/40')){
    if(failed || Date.now()-started>60000)throw Error('Shipped Preview failed or did not finish 40 pages');
    await new Promise(resolve=>setTimeout(resolve,50));
  }
  const pages=[...container.querySelectorAll('.pdf-page canvas')];
  const thumbnails=[...container.querySelectorAll('.thumbnail-page canvas')];
  if(pages.length!==40 || thumbnails.length!==40 || [...pages,...thumbnails].some(canvas=>canvas.width===0 || canvas.dataset.error==='true'))throw Error('Shipped Preview missing rendered canvases');
  const canvas=pages[0], scale=canvas.width/595;
  const glyphs=canvas.getContext('2d').getImageData(Math.floor(48*scale),Math.floor(69*scale),Math.floor(400*scale),Math.ceil(15*scale)).data;
  const ink=Array.from(glyphs).filter((value,index)=>index%4!==3 && value<180).length;
  if(ink<40)throw Error('Shipped Preview CJK glyph area is blank');
  return {pages:pages.length,thumbnails:thumbnails.length,image:pages[0].toDataURL(),destroy:()=>root.unmount()};
};`,resolveDir:path.join(repo,'tauri'),loader:'tsx'},bundle:true,write:false,format:'esm',target:'es2022',jsx:'automatic',define:{'process.env.NODE_ENV':'"production"'},plugins:[{name:'local-pdf-worker',setup(build){build.onResolve({filter:/pdf\.worker\.min\.mjs\?url$/},()=>({path:'worker',namespace:'fixture'}));build.onLoad({filter:/.*/,namespace:'fixture'},()=>({contents:"export default '/lib/legacy/build/pdf.worker.min.mjs'",loader:'js'}));}}]});
const componentScript=component.outputFiles[0].contents;
const hashes = {};
for (const name of ['rich-40.pdf', 'script-marker-40.pdf'])
  hashes[name] = createHash('sha256').update(await readFile(path.join(directory,name))).digest('hex');
const html = '<!doctype html><meta charset="utf-8"><canvas id="page"></canvas><div id="root"></div><script type="module" src="/regression.mjs"></script>';
const csp=JSON.parse(await readFile(path.join(repo,'tauri/src-tauri/tauri.conf.json'),'utf8')).app.security.csp;
const harness = `
import {getDocument, GlobalWorkerOptions} from '/lib/legacy/build/pdf.mjs';
import '/component.mjs';
GlobalWorkerOptions.workerSrc='/lib/legacy/build/pdf.worker.min.mjs';
globalThis.__pdfScriptRan=false;
globalThis.regression=(async()=>{
  const results=[];
  for(const file of ['rich-40.pdf','script-marker-40.pdf']) {
    const task=getDocument({url:'/fixture/'+file,cMapUrl:'/lib/cmaps/',cMapPacked:true,standardFontDataUrl:'/lib/standard_fonts/',wasmUrl:'/lib/wasm/'});
    const doc=await task.promise;
    if(doc.numPages!==40)throw Error('Incorrect page count');
    const pages=[];
    for(let number=1;number<=doc.numPages;number++) {
      const page=await doc.getPage(number), text=await page.getTextContent();
      if(!text.items.some(item=>item.str.includes('PAGE-'+String(number).padStart(3,'0'))))throw Error('Page ID missing');
      if(!text.items.some(item=>item.str.includes('日本語')))throw Error('CJK text missing');
      for(const scale of [0.14,0.8]) {
        const viewport=page.getViewport({scale}), canvas=document.getElementById('page');
        canvas.width=Math.ceil(viewport.width);canvas.height=Math.ceil(viewport.height);
        const context=canvas.getContext('2d');
        await page.render({canvas,canvasContext:context,viewport}).promise;
        const data=context.getImageData(0,0,canvas.width,canvas.height).data;
        if(!data.some((value,index)=>index%4!==3 && value<220))throw Error('Blank canvas');
      }
      pages.push(number);
    }
    await task.destroy();
    const preview=await globalThis.mountPreview(new Uint8Array(await(await fetch('/fixture/'+file)).arrayBuffer()));
    globalThis.previewImage=preview.image;
    results.push({file,pages:pages.length,componentPages:preview.pages,thumbnails:preview.thumbnails,cjkText:true,scriptRan:globalThis.__pdfScriptRan});
    preview.destroy();
  }
  if(globalThis.__pdfScriptRan)throw Error('PDF script executed');
  return results;
})();
`;
const requests=[];
const server=createServer(async(req,res)=>{
  try {
    const url=new URL(req.url,'http://localhost');
    requests.push(url.pathname);
    res.setHeader('Content-Security-Policy',csp);
    if(url.pathname==='/'){res.setHeader('Content-Type','text/html');res.end(html);return;}
    if(url.pathname==='/regression.mjs'){res.setHeader('Content-Type','text/javascript');res.end(harness);return;}
    if(url.pathname==='/component.mjs'){res.setHeader('Content-Type','text/javascript');res.end(componentScript);return;}
    let filename;
    if(url.pathname.startsWith('/lib/'))filename=path.resolve(repo,'tauri/node_modules/pdfjs-dist',url.pathname.slice(5));
    else if(url.pathname.startsWith('/pdfjs/'))filename=path.resolve(repo,'tauri/public/pdfjs',url.pathname.slice(7));
    else if(['/fixture/rich-40.pdf','/fixture/script-marker-40.pdf'].includes(url.pathname))filename=path.join(directory,path.basename(url.pathname));
    else{res.writeHead(404);res.end();return;}
    const allowed=path.resolve(repo,'tauri/node_modules/pdfjs-dist')+path.sep;
    const assets=path.resolve(repo,'tauri/public/pdfjs')+path.sep;
    if(!filename.startsWith(allowed) && !filename.startsWith(assets) && path.dirname(filename)!==directory){res.writeHead(403);res.end();return;}
    const mime=filename.endsWith('.mjs')?'text/javascript':filename.endsWith('.wasm')?'application/wasm':filename.endsWith('.pdf')?'application/pdf':'application/octet-stream';
    res.setHeader('Content-Type',mime);res.end(await readFile(filename));
  } catch {res.writeHead(404);res.end();}
});
await new Promise(resolve=>server.listen(0,'127.0.0.1',resolve));
const url=`http://127.0.0.1:${server.address().port}/`;
const child=spawn(browserPath,['--headless=new','--disable-background-networking','--disable-component-update','--no-first-run','--no-default-browser-check','--remote-debugging-port=0','--remote-debugging-address=127.0.0.1',`--user-data-dir=${profile}`,'about:blank'],{windowsHide:true,stdio:'ignore'});
let socket, report;
const delay=ms=>new Promise(resolve=>setTimeout(resolve,ms));
try {
  let port;
  for(let i=0;i<200;i++){try{port=Number((await readFile(path.join(profile,'DevToolsActivePort'),'utf8')).split('\n')[0]);break;}catch{await delay(50);}}
  assert(port,'Browser did not expose local DevTools');
  const tab=await (await fetch(`http://127.0.0.1:${port}/json/new?about:blank`,{method:'PUT'})).json();
  socket=new WebSocket(tab.webSocketDebuggerUrl);
  await new Promise((resolve,reject)=>{socket.onopen=resolve;socket.onerror=reject;});
  let sequence=0;const pending=new Map();
  socket.onmessage=event=>{const message=JSON.parse(event.data);if(message.id){const call=pending.get(message.id);if(call){pending.delete(message.id);message.error?call.reject(Error(JSON.stringify(message.error))):call.resolve(message.result);}}};
  const call=(method,params={})=>new Promise((resolve,reject)=>{const id=++sequence;pending.set(id,{resolve,reject});socket.send(JSON.stringify({id,method,params}));});
  await call('Page.enable');await call('Runtime.enable');
  await call('Page.navigate',{url});
  let evaluation;
  for(let i=0;i<200;i++){
    evaluation=await call('Runtime.evaluate',{expression:'globalThis.regression',awaitPromise:true,returnByValue:true,timeout:120000});
    if(evaluation.exceptionDetails)throw Error(JSON.stringify(evaluation.exceptionDetails));
    if(evaluation.result.value)break;
    await delay(50);
  }
  const results=evaluation.result.value;
  assert.equal(results?.length,2);assert(results.every(item=>item.pages===40 && item.thumbnails===40 && !item.scriptRan));
  const screenshot=await call('Page.captureScreenshot',{format:'png'});
  await writeFile(path.join(directory,'render.png'),Buffer.from(screenshot.data,'base64'));
  const raster=await call('Runtime.evaluate',{expression:'globalThis.previewImage',returnByValue:true});
  await writeFile(path.join(directory,'preview-page-1.png'),Buffer.from(raster.result.value.split(',')[1],'base64'));
  for(const [name,before]of Object.entries(hashes))assert.equal(createHash('sha256').update(await readFile(path.join(directory,name))).digest('hex'),before);
  report={status:'PASS',pdfjs:JSON.parse(await readFile(path.join(repo,'tauri/node_modules/pdfjs-dist/package.json'),'utf8')).version,results,sourceHashes:hashes,requests,desktopUi:'NOT_EXECUTED',csp};
  // Close the private browser gracefully; it owns only this isolated profile.
  const browser=await (await fetch(`http://127.0.0.1:${port}/json/version`)).json();
  const control=new WebSocket(browser.webSocketDebuggerUrl);
  await new Promise(resolve=>{control.onopen=()=>{control.send(JSON.stringify({id:1,method:'Browser.close'}));resolve();};});
} catch(error){report={status:'FAIL',error:String(error),sourceHashes:hashes};process.exitCode=1;}
finally {
  socket?.close();
  if(child.exitCode===null){await Promise.race([new Promise(resolve=>child.once('exit',resolve)),delay(5000)]);if(child.exitCode===null)child.kill();}
  server.close();
  await writeFile(path.join(directory,'results.json'),JSON.stringify(report,null,2));
  console.log(JSON.stringify({status:report.status,results:report.results,error:report.error}));
}
