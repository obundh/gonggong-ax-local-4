const {app,BrowserWindow,session,ipcMain,dialog,protocol,net}=require('electron');
const {applyMode}=require('./window-layout.cjs');
const path=require('node:path');
const {pathToFileURL}=require('node:url');
protocol.registerSchemesAsPrivileged([{scheme:'axmedia',privileges:{standard:true,secure:true,stream:true,supportFetchAPI:true}}]);
if(!app.requestSingleInstanceLock())app.quit();
else app.whenReady().then(()=>{
 session.defaultSession.setPermissionRequestHandler((web,permission,cb)=>cb(permission==='fullscreen'&&web===win.webContents));
 const win=new BrowserWindow({width:430,height:760,minWidth:380,minHeight:600,backgroundColor:'#edf2f7',title:'공공 AX · Tape Studio',autoHideMenuBar:true,titleBarStyle:'hidden',titleBarOverlay:{color:'#183653',symbolColor:'#e4edf6',height:38},roundedCorners:true,webPreferences:{nodeIntegration:false,contextIsolation:true,sandbox:true,preload:path.join(__dirname,'preload.cjs')}});
 let state={phase:'connecting',busy:false,message:'엔진 연결 중',events:[]},videoPath='',videoId=0,closing=false;
 protocol.handle('axmedia',request=>{if(!videoPath||new URL(request.url).pathname!=='/'+videoId)return new Response('Not found',{status:404});return net.fetch(pathToFileURL(videoPath).toString(),{headers:request.headers});});
 const bridge=require('./native-bridge.cjs')(next=>{const wasBusy=state.busy;if(next.videoPath&&next.videoPath!==videoPath){videoPath=next.videoPath;videoId++;}state={...next,source:next.videoPath?'axmedia://video/'+videoId:'',name:next.videoPath?path.basename(next.videoPath):'새 업무'};if(!win.isDestroyed()){win.webContents.send('ax:state',state);if(wasBusy&&!next.busy&&win.isMinimized()){win.restore();win.focus();}}});
 const guard=event=>{if(event.sender!==win.webContents||event.senderFrame!==win.webContents.mainFrame)throw Error('Invalid sender');};
 win.webContents.setWindowOpenHandler(()=>({action:'deny'}));win.webContents.on('will-navigate',e=>e.preventDefault());
 ipcMain.handle('ax:set-mode',(event,mode)=>{guard(event);return applyMode(win,mode);});
 ipcMain.handle('ax:state',event=>{guard(event);return state;});
 ipcMain.handle('ax:command',async(event,action,data={})=>{
  guard(event);
  if(!['record','stop','run','open','save','edit','delete','add'].includes(action))throw Error('Unknown command');
  if(action==='open'){const result=await dialog.showOpenDialog(win,{properties:['openFile'],filters:[{name:'업무 기록 · 영상',extensions:['json','mp4','webm','mov']}]});if(result.canceled)return;data={path:result.filePaths[0]};}
  if(action==='run'&&(!Number.isInteger(data.repeats)||data.repeats<1||data.repeats>999))throw Error('반복 횟수: 1~999');
  await bridge.command(action,data);
  if(action==='record'||action==='run')win.minimize();
 });
 win.on('close',event=>{if(closing)return;event.preventDefault();if(state.busy){dialog.showMessageBox(win,{type:'info',message:'진행 중인 작업을 중지한 후 닫아 주세요.'});return;}closing=true;bridge.close();bridge.child.once('exit',()=>win.destroy());setTimeout(()=>{if(!win.isDestroyed()){closing=false;dialog.showMessageBox(win,{type:'warning',message:'엔진 종료를 기다리고 있습니다. 저장 상태를 확인하세요.'});}},10000);});
 app.on('second-instance',()=>{win.show();win.restore();win.focus();});
 win.webContents.once('did-finish-load',()=>{win.show();win.focus();});
 win.loadFile(path.join(__dirname,'dist/index.html'));
});
app.on('window-all-closed',()=>app.quit());
