import React from 'react';
import {createRoot} from 'react-dom/client';
import './style.css';
import './responsive.css';
import {Intro} from './Intro';
import {WindowFrame} from './WindowFrame';
import './studio-public.css';
import {Compact} from './Compact';
import {ProjectProvider,useProject,ProjectToolbar,SharedVideo,EventList} from './Project';
import '@fontsource/ibm-plex-sans-kr/400.css';
import '@fontsource/ibm-plex-sans-kr/600.css';
import '@fontsource/ibm-plex-sans-kr/700.css';
import '@fontsource/ibm-plex-mono/latin-400.css';
import './typography.css';
function App(){const p=useProject();return <div className="app"><header><div className="brand">공공 AX<span>LOCAL / 04</span></div><div className="edition">TAPE STUDIO<span>업무 기록 · 자동화</span></div><span className="sample">{p.connected?'로컬 엔진':'영상 플레이어'}</span></header><main><div className="section-heading"><div><div className="eyebrow">WORKSPACE</div><h1>{p.name}<span className="index">/ STUDIO</span></h1></div></div><ProjectToolbar/><section className="editor"><div className="preview"><div className="panel-head"><h2>업무 영상</h2><span>LOCAL VIDEO</span></div>{p.source?<SharedVideo view="studio"/>:<div className="native-video-empty"><b>{p.native.busy?'기록 진행 중':'새 기록'}</b><span>업무 보여주기 → 업무 시연 → 종료</span><small>종료: Ctrl + Shift + F12</small></div>}</div><aside><div className="panel-head"><h2>행동 로그</h2><span>{p.events.length}개</span></div><EventList/></aside></section><section className="native-timeline"><h2>타임라인</h2><div>{p.events.map((a,i)=><button key={i} onClick={()=>p.setCursor(a.at)}>{String(i+1).padStart(2,'0')}<span>{a.at.toFixed(1)}s</span></button>)}</div></section><footer><span>LOCAL FIRST · IBM PLEX</span><span>영상 · 행동 · 실행 로그</span></footer></main></div>;}
createRoot(document.getElementById('root')!).render(<ProjectProvider><WindowFrame/><Compact/><App/><Intro/></ProjectProvider>);
