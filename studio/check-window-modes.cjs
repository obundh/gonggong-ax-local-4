const {app,BrowserWindow,ipcMain,screen}=require('electron');
const path=require('node:path');
const assert=require('node:assert/strict');
const {applyMode}=require('./window-layout.cjs');
app.whenReady().then(async()=>{
 try{
  const win=new BrowserWindow({show:false,width:430,height:760,titleBarStyle:'hidden',titleBarOverlay:true,webPreferences:{preload:path.join(__dirname,'preload.cjs'),sandbox:true,contextIsolation:true,nodeIntegration:false}});
  ipcMain.handle('ax:set-mode',(event,mode)=>{assert.equal(event.sender,win.webContents);return applyMode(win,mode);});
  ipcMain.handle('ax:state',()=>({busy:false,phase:'idle',message:'창 크기 테스트',events:[]}));
  await win.loadFile(path.join(__dirname,'dist/index.html'));
  for(const mode of ['studio','compact','studio','compact']){
   const result=await win.webContents.executeJavaScript(`window.axShell.setMode('${mode}')`);
   const area=screen.getDisplayMatching(result).workArea;
   assert.equal(result.width,Math.min(mode==='studio'?1440:430,area.width));
   assert.equal(result.height,Math.min(mode==='studio'?960:760,area.height));
   assert.ok(result.x>=area.x&&result.x+result.width<=area.x+area.width);
   console.log(mode,JSON.stringify(result));
  }
  console.log('PASS: isolated preload / IPC / native window sizing');app.exit(0);
 }catch(error){console.error(error);app.exit(1);}
});
