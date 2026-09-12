import {useState} from 'react';
import '@fontsource/noto-sans-kr/400.css';
import '@fontsource/noto-sans-kr/600.css';
import '@fontsource/noto-sans-kr/700.css';
import '@fontsource/ibm-plex-sans-kr/400.css';
import '@fontsource/ibm-plex-sans-kr/600.css';
import '@fontsource/ibm-plex-sans-kr/700.css';
import './font-picker.css';
const options=[{id:'pretendard',name:'Pretendard',family:'Pretendard',label:'단정한'},{id:'noto',name:'Noto Sans KR',family:'Noto Sans KR',label:'또렷한'},{id:'plex',name:'IBM Plex',family:'IBM Plex Sans KR',label:'개성 있는'}];
export function FontPicker(){
 const [selected,setSelected]=useState(()=>{try{const stored=localStorage.getItem('ax-font');return options.some(o=>o.id===stored)?stored!:'pretendard';}catch{return 'pretendard';}});
 const [expanded,setExpanded]=useState(false);
 const selectedFont=options.find(o=>o.id===selected)!;
 document.documentElement.style.setProperty('--font-ui',`'${selectedFont.family}', sans-serif`);
 return <section className="font-picker" aria-label="글꼴 선택"><button className="font-toggle" aria-expanded={expanded} aria-controls="font-options" onClick={()=>setExpanded(!expanded)}><span>가 Aa</span>글꼴 · {selectedFont.name}<span>{expanded?'−':'＋'}</span></button><div id="font-options" hidden={!expanded}><div className="font-options">{options.map(option=><button key={option.id} aria-pressed={selected===option.id} onClick={()=>{setSelected(option.id);try{localStorage.setItem('ax-font',option.id);}catch{/* Session-only selection remains usable. */}}}><span className="font-example" style={{'--preview-font':`'${option.family}'`} as React.CSSProperties}>공공 AX</span><b>{option.name}</b><small>{option.label}{selected===option.id?' · 선택됨':''}</small></button>)}</div><p>두 모드에 함께 적용 · 시간·번호는 고정폭 유지</p></div></section>;
}
