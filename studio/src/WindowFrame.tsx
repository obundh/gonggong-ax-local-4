import './window-frame.css';
export function WindowFrame(){
 const desktop=Boolean((window as Window & {axShell?:{desktop:boolean}}).axShell?.desktop);
 document.documentElement.classList.toggle('desktop-shell',desktop);
 return <><div className="window-rail"><div className="rail-brand"><span className="rail-notch"/>AX<span className="rail-divider"/>LOCAL SERIES<span className="rail-number">04</span></div><span className="rail-center">TAPE STUDIO</span>{!desktop&&<span className="rail-preview">프레임 미리보기</span>}</div><div className="shell-outline" aria-hidden="true"/><div className="shell-corner" aria-hidden="true"/></>;
}
