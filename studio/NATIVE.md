# 로컬 엔진

## 실행

Windows에서 실행.cmd. 브라우저는 영상 확인용이며 전역 입력 기록·재생은 Electron 전용입니다.

## 기능

- IBM Plex Sans KR 고정, 숫자 IBM Plex Mono
- 주 모니터 MP4 녹화 + 전역 클릭·드래그·스크롤·키 입력 기록
- 3초 준비 시간, 자동 최소화, 완료 후 복원
- Ctrl + Shift + F12 긴급 정지. 단축키 등록 실패 시 시작 차단
- 영상/기록 열기, 자동 저장, 수동 저장
- 스튜디오·컴팩트 작업 공유
- 행동 시간/좌표 수정·삭제, 영상 장면 탐색
- 영상 위 클릭 위치·동작 표시, 표시 켜기/끄기, 전체 화면
- 로그의 ＋: 대기(0.1~3600초)·텍스트 입력·왼쪽/오른쪽 클릭 추가
- 대기는 실행 시간에 추가되며 원본 영상 시점을 변경하지 않음. 같은 시점은 기존 행동 뒤에 추가
- 1~999회 반복, 이벤트 실패 시 다음 반복 중단
- MP4 옆 .series4.json 및 .runs.jsonl 실행 결과

## 한계

- 영상 내용 분석이나 AI 학습이 아니라 기록된 입력을 재생합니다.
- 창 이동·배율·해상도·입력 언어가 달라지면 실패할 수 있습니다. 좌표가 정확히 일치하지 않으면 해당 이벤트를 건너뜁니다.
- 한글 IME·관리자 권한 프로그램·보안 화면·다중 모니터는 별도 검증이 필요합니다.
- 편집은 시간과 시작 좌표 중심입니다. 복잡한 키 조합·드래그 경로 편집 UI는 아직 없습니다.
- 반복 작업은 화면 상태를 자동으로 초기화하지 않습니다.
- v4.2.0 설치 EXE·포터블 ZIP에 Electron과 self-contained 기록 엔진이 포함됩니다.
- 녹화 중 CPU/GPU/디스크 사용량이 증가합니다. 녹화에 암호·민감한 입력을 포함하지 마세요.

## 빌드

저장소 루트에서 .NET 10 SDK: dotnet build Series4.Desktop.csproj -c Release -p:Platform=x64 -o artifacts/bridge

studio에서 npm run build, npm run desktop.

## 검증

- node check-native.cjs: 실제 녹화, MP4·sidecar 저장, 열기/저장. 실제 주 모니터를 짧게 녹화하므로 업무 입력을 멈춘 상태에서만 실행.
- node check-replay.cjs: output 폴더의 격리된 테스트 창/fixture를 이용한 실제 클릭 2회. 개발환경용 fixture 경로 확인 필요.
- electron check-desktop.cjs: preload 연결, IBM 적용, MP4 재생, 창 크기 전환. Electron 정상 실행본과 동시 실행 금지.

기존 WPF 모드는 유지되며 --bridge 인자로만 숨겨진 엔진 모드가 시작됩니다.
