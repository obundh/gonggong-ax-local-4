// Explicit --smoke-test diagnostic: no desktop recording or input replay.
const assert=require('node:assert/strict');
module.exports=async function({app,win,bridge,getState}){
 const delay=ms=>new Promise(resolve=>setTimeout(resolve,ms));
 try{
  const deadline=Date.now()+20000;
  while(getState().phase==='connecting'&&Date.now()<deadline)await delay(100);
  assert.equal(getState().phase,'idle','bundled native engine handshake');
  await win.webContents.executeJavaScript("document.querySelector('.intro-bottom button')?.click()");
  await win.webContents.executeJavaScript('document.fonts.ready');
  const ui=await win.webContents.executeJavaScript(`({
   font:getComputedStyle(document.body).fontFamily,
   controls:[...document.querySelectorAll('input[aria-label="반복 횟수"]')].map(n=>({min:n.min,max:n.max})),
   addButtons:document.querySelectorAll('button[aria-label="행동 추가"]').length,
   desktop:window.axShell.desktop,
   errors:[...document.querySelectorAll('[role="alert"]')].map(n=>n.textContent)
  })`);
  assert(ui.font.includes('IBM Plex Sans KR'));
  assert(ui.desktop&&ui.addButtons===2&&ui.controls.length===2);
  assert(ui.controls.every(n=>n.min==='1'&&n.max==='999'));
  assert.equal(ui.errors.length,0);
  const bounds=[];
  for(const mode of ['studio','compact']){
   bounds.push(await win.webContents.executeJavaScript(`window.axShell.setMode('${mode}')`));
  }
  assert(bounds[0].width>=bounds[1].width);
  console.log('AX_SMOKE '+JSON.stringify({ok:true,packaged:app.isPackaged,version:app.getVersion(),engine:getState().phase,ui,bounds}));
  const exit=new Promise(resolve=>bridge.child.once('exit',resolve));
  bridge.close();
  await Promise.race([exit,delay(10000).then(()=>{throw Error('Engine exit timeout');})]);
  app.exit(0);
 }catch(error){
  console.error('AX_SMOKE_FAILED '+error.message);
  bridge.close();
  const timeout=setTimeout(()=>{if(bridge.child.exitCode===null)bridge.child.kill();app.exit(1);},3000);
  bridge.child.once('exit',()=>{clearTimeout(timeout);app.exit(1);});
 }
};
