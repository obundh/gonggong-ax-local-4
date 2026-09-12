# Electron 검증 · 2026-09-12

## 결과

- TypeScript 검사 및 Vite production build: 통과
- C# Release x64 build, 경고를 오류로 처리: 경고 0 · 오류 0
- xUnit: 37개 통과 · 실패 0 · 건너뜀 0
- 대기 정책 경계값: 0.1초·3600초 허용, 범위 밖·NaN·Infinity·빈 값 거부
- Electron 숨겨진 창 전환: 스튜디오 1440×960, 컴팩트 430×760, 두 차례 왕복 통과
- preload/IPC 연결 및 작업 영역 내 창 배치: 통과
- 소개 만화 2장: 한국어, 반복 1~999회, 대기 0.1~3600초 및 예시 표시 확인

## 검증 범위

이번 게시 준비에서는 실제 업무 화면의 녹화·키 입력·마우스 재생을 다시 실행하지 않았습니다.
자동 테스트는 엔진 정책과 기존 단위 테스트, 창 크기 전환을 확인하며 전체 업무 자동화의
성공을 보장하지 않습니다. 한글 IME·관리자 권한 앱·다중 모니터는 별도 검증 대상입니다.
새 Electron 설치파일은 이번 변경에 포함되지 않습니다.

## 명령

```powershell
npm --prefix studio run build
dotnet build Series4.Desktop.csproj -c Release -p:Platform=x64 --no-restore -warnaserror
dotnet test tests/Series4.Desktop.Tests/Series4.Desktop.Tests.csproj -c Release -p:Platform=x64
cd studio
node_modules/.bin/electron check-window-modes.cjs
```

.NET 검증에는 global.json에 고정한 SDK 10.0.302를 사용했습니다.
