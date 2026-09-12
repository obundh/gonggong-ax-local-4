import {useState} from 'react';
import {useProject,SharedVideo,ProjectToolbar,EventList,shell} from './Project';
import './compact.css';
import './messenger.css';
import './public-theme.css';
export function Compact(){
 const p=useProject(),compact=p.mode==='compact';const [error,setError]=useState('');
 document.documentElement.classList.toggle('compact-mode',compact);
 const changeMode=(next:boolean)=>{p.setMode(next?'compact':'studio');shell?.setMode(next?'compact':'studio').catch(()=>setError('창 크기 변경 실패'));};
 return <><nav className="mode-switch" aria-label="화면 모드"><button aria-pressed={!compact} onClick={()=>changeMode(false)}>스튜디오</button><button aria-pressed={compact} onClick={()=>changeMode(true)}>컴팩트</button></nav><div className="compact-workspace" hidden={!compact}><div className="compact-heading"><div><span className="compact-eyebrow">LOCAL / 04</span><h1>업무 플레이어<span>COMPACT</span></h1></div><span className="compact-status">{p.connected?'연결됨':'영상 전용'}</span></div><ProjectToolbar/><div className="compact-grid"><section className="compact-video" aria-label="영상 플레이어">{p.source?<SharedVideo view="compact"/>:<div className="native-video-empty"><b>{p.native.busy?'기록 진행 중':'업무 보여주기'}</b><span>종료 후 영상 확인</span></div>}</section><aside className="compact-events"><div className="compact-events-title"><h2>행동</h2><span>{p.events.length}개</span></div><EventList/></aside></div>{error&&<p role="alert">{error}</p>}<footer className="compact-footer"><span>LOCAL FIRST</span><span>영상 · 행동 · 실행 로그</span></footer></div></>;
}
