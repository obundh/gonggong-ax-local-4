const { contextBridge, ipcRenderer } = require('electron');
contextBridge.exposeInMainWorld('axShell', Object.freeze({ desktop: true,
 command:(action,data)=>ipcRenderer.invoke('ax:command',action,data),
 getState:()=>ipcRenderer.invoke('ax:state'),
 onState:callback=>{const listener=(_event,state)=>callback(state);ipcRenderer.on('ax:state',listener);return()=>ipcRenderer.removeListener('ax:state',listener);},
 setMode:(mode)=>{
  if(mode!=='studio'&&mode!=='compact')return Promise.reject(new TypeError('Unknown mode'));
  return ipcRenderer.invoke('ax:set-mode',mode);
} }));
