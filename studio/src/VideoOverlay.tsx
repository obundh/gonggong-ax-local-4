import {useEffect,useState,RefObject} from 'react';
import type {Action} from './Project';

export function VideoOverlay({video,events,cursor}:{video:RefObject<HTMLVideoElement|null>;events:Action[];cursor:number}){
 const [box,setBox]=useState({left:0,top:0,width:0,height:0});
 useEffect(()=>{
  const v=video.current;if(!v)return;
  const measure=()=>{if(!v.videoWidth||!v.videoHeight)return;const scale=Math.min(v.clientWidth/v.videoWidth,v.clientHeight/v.videoHeight);const width=v.videoWidth*scale,height=v.videoHeight*scale;setBox({left:v.offsetLeft+(v.clientWidth-width)/2,top:v.offsetTop+(v.clientHeight-height)/2,width,height});};
  const observer=new ResizeObserver(measure);observer.observe(v);v.addEventListener('loadedmetadata',measure);measure();
  return()=>{observer.disconnect();v.removeEventListener('loadedmetadata',measure);};
 },[video]);
 const active=events.filter(e=>e.kind!=='None'&&e.at<=cursor&&cursor-e.at<1.6).sort((a,b)=>a.at-b.at).slice(-1)[0];
 if(!active||!box.width)return null;
 const width=active.captureWidth||video.current?.videoWidth||1,height=active.captureHeight||video.current?.videoHeight||1;
 const nx=active.x==null?null:(active.x-(active.captureLeft||0))/width,ny=active.y==null?null:(active.y-(active.captureTop||0))/height;
 const pointed=nx!=null&&ny!=null&&nx>=0&&nx<=1&&ny>=0&&ny<=1;
 const label=active.label||({MouseLeftClick:'왼쪽 클릭',MouseRightClick:'오른쪽 클릭',MouseMiddleClick:'가운데 클릭',MouseDrag:'드래그',MouseWheel:'스크롤',KeyStroke:'키 입력',TextEntry:'텍스트 입력',Wait:'대기'} as Record<string,string>)[active.kind||'']||active.name;
 return <div className="video-action-overlay" style={box} aria-hidden="true">
  <div className="video-action-label" style={pointed&&nx!<.5?{left:'auto',right:8}:undefined} title={active.detail}><b>{label}</b><span>{active.detail}</span></div>
  {pointed&&<span key={String(active.id)+':'+active.at} className="video-click-ring" style={{left:(nx!*100)+'%',top:(ny!*100)+'%'}}><i/></span>}
 </div>;
}
