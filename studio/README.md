# Electron 스튜디오

공공 AX 로컬 시리즈 4의 Windows 데스크톱 UI.
React · TypeScript · Vite · Electron과 기존 C# 기록·재생 엔진을 연결합니다.
Three.js는 초기 디자인 의존성으로 남아 있으며 현재 업무 화면의 필수 렌더러는 아닙니다.

## 실행

Windows x64 · .NET SDK 10.0.302 · Node.js 22.12 이상.

```powershell
# 저장소 루트
dotnet build Series4.Desktop.csproj -c Release -p:Platform=x64 -o artifacts/bridge
cd studio
npm ci
npm run build
npm run desktop
```

빌드 이후에는 `실행.cmd`로 실행. 새 Electron 설치파일은 아직 배포하지 않습니다.
`npm run dev`는 루프백 브라우저 미리보기이며 전역 입력 기록·재생을 지원하지 않습니다.
최초 의존성 다운로드 이후 앱 동작에는 외부 AI 서비스가 필요하지 않습니다.

## 기능

- 스튜디오·컴팩트 자동 크기 조절과 작업 공유
- IBM Plex Sans KR · IBM Plex Mono
- 주 모니터 영상과 클릭·드래그·스크롤·키 입력 기록
- 영상 옆 행동 로그 · 영상 위 클릭 표시
- 시간·좌표·텍스트 편집, 삭제, 대기·텍스트·클릭 추가
- 반복 횟수 1~999회 · 동작 사이 대기 0.1~3600초
- 3초 준비 후 시작, 자동 최소화, Ctrl + Shift + F12 긴급 정지

영상 이해·AI 학습·자동 화면 초기화는 지원하지 않습니다.
같은 창 위치·배율·시작 화면에서 먼저 1회 시험하세요.
녹화 중 PC 성능과 디스크 사용량에 영향이 있습니다.
세부 설명: [로컬 엔진](NATIVE.md).

## 검증

- `npm run build`: TypeScript 검사와 배포용 번들
- `node_modules/.bin/electron check-window-modes.cjs`: 숨겨진 테스트 창의 크기 전환과 preload/IPC 확인
- 저장소 루트: `dotnet test tests/Series4.Desktop.Tests/Series4.Desktop.Tests.csproj -c Release -p:Platform=x64`

나머지 `check-*.cjs`는 로컬 녹화·재생 실험용입니다. 일부는 Git에 포함되지 않는
`output/` fixture와 실제 화면 제어가 필요하므로 자동 CI 테스트로 실행하지 마세요.
현재 개발 앱과 동시에 실행하지 마세요.

## 배포 범위

이 폴더는 프로그램 소스와 글꼴 라이선스를 포함합니다.
`node_modules/`, `dist/`, `output/`, 업무 녹화·실행 로그는 저장소에 포함하지 않습니다.
폰트는 빌드 시 로컬 번들에 포함되며 CDN에서 로드하지 않습니다.
Electron renderer는 sandbox, context isolation을 사용하고 Node integration은 끕니다.
