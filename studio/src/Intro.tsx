import {useEffect,useRef,useState} from 'react';
import './intro.css';
import './brand-intro.css';

export function Intro(){
 const [visible,setVisible]=useState(()=>!matchMedia('(prefers-reduced-motion: reduce)').matches);
 const button=useRef<HTMLButtonElement>(null);
 useEffect(()=>{
  if(!visible)return;
  const previous=document.activeElement as HTMLElement|null;
  const content=document.querySelectorAll('.app, .compact-workspace, .mode-switch, .font-picker');
  content.forEach(node=>node.setAttribute('inert',''));
  button.current?.focus();
  const end=()=>setVisible(false);
  const key=(e:KeyboardEvent)=>{if(e.key==='Escape')end();};
  const preference=matchMedia('(prefers-reduced-motion: reduce)');
  const onPreference=()=>{if(preference.matches)end();};
  const timer=setTimeout(end,4200);
  document.addEventListener('keydown',key);
  preference.addEventListener('change',onPreference);
  return()=>{
   clearTimeout(timer);document.removeEventListener('keydown',key);
   preference.removeEventListener('change',onPreference);
   content.forEach(node=>node.removeAttribute('inert'));
   if(previous&&previous!==document.body)previous.focus();
   else (document.querySelector('.mode-switch button[aria-pressed="true"]') as HTMLElement|null)?.focus();
  };
 },[visible]);
 if(!visible)return null;
 return <div className="intro" role="dialog" aria-modal="true" aria-label="공공 AX 로컬 시리즈 오프닝" onClick={()=>setVisible(false)}>
  <div className="intro-corner" aria-hidden="true">PUBLIC AX<span>LOCAL / 04</span></div>
  <div className="brand-motion" aria-hidden="true">
   <div className="brand-orbit"/>
   <div className="brand-korean"><span>공</span><span>공</span></div>
   <div className="brand-ax"><span>A</span><span>X</span></div>
   <div className="brand-rule"/>
   <div className="brand-series"><span>로컬</span><span>시리즈</span><b>04</b></div>
   <div className="brand-coordinate">PUBLIC AUTOMATION / LOCAL SERIES</div>
  </div>
  <div className="intro-bottom"><span aria-hidden="true">PUBLIC AX — LOCAL SERIES / 04</span><button ref={button} onClick={()=>setVisible(false)}>건너뛰기 <kbd>ESC</kbd></button></div>
  <div className="intro-progress" aria-hidden="true"/>
 </div>;
}
