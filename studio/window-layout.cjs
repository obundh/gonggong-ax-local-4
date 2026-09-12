const {screen}=require('electron');
function applyMode(win, mode){
  if(mode!=='studio' && mode!=='compact')throw new TypeError('Unknown window mode');
  const current=win.getBounds();
  const area=screen.getDisplayMatching(current).workArea;
  if(win.isFullScreen())win.setFullScreen(false);
  if(win.isMaximized())win.unmaximize();
  const width=Math.min(mode==='studio'?1440:430,area.width);
  const height=Math.min(mode==='studio'?960:760,area.height);
  win.setMinimumSize(Math.min(mode==='studio'?760:380,area.width),Math.min(600,area.height));
  const x=Math.max(area.x,Math.min(Math.round(current.x+(current.width-width)/2),area.x+area.width-width));
  const y=Math.max(area.y,Math.min(Math.round(current.y+(current.height-height)/2),area.y+area.height-height));
  win.setBounds({x,y,width,height});
  win.setBackgroundColor('#edf2f7');
  win.setTitleBarOverlay({color:'#183653',symbolColor:'#e4edf6',height:38});
  return win.getBounds();
}
module.exports={applyMode};
