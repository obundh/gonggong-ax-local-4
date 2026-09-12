# v4.2.0 배포 검증

검증일: 2026-09-12 · Windows x64

## 통과

- TypeScript 검사·Vite production build
- .NET SDK 10.0.302 Release x64 빌드: 경고 0 · 오류 0
- xUnit 37개: 통과 37 · 실패 0
- 포터블 ZIP 전체 압축 해제 후 실행
- 설치 EXE의 별도 임시 경로 설치 후 실행
- 두 배포본: packaged=true, 앱 버전 4.2.0, 동봉한 엔진 연결 idle
- 두 배포본: IBM 글꼴, 반복 입력 범위 1~999, 행동 추가 버튼, 두 모드의 창 크기 전환
- 별도 테스트 설치 제거
- 합성 단색 영상으로 엔진 대기 추가·수정·저장·다시 열기: 0.2초 확인
- 대기 동작만 2회 반복: 실행 완료
- SHA-256 manifest와 배포 파일 일치

## 범위

새 패키지의 실제 설치·엔진 연결·기본 UI와 대기 반복을 검증했습니다.
실제 업무 화면 녹화나 마우스·키 입력 재생은 이번 검증에서 수행하지 않았습니다.
공인 코드 서명, 모든 Windows 버전, IME·관리자 권한 앱·다중 모니터는 검증 범위 밖입니다.
설치 테스트는 제거 등록과 바로가기를 만들지 않는 별도 테스트 옵션을 사용했습니다.
기존 사용자 설치·비디오 폴더 기록은 삭제하지 않았습니다.

## 재현

- scripts/build-electron-release.ps1
- scripts/test-electron-release.ps1 -Version 4.2.0
- scripts/test-packaged-engine.mjs: 엔진 EXE와 별도로 만든 합성 MP4 경로 필요
- studio/package-smoke.cjs: 배포 EXE의 --smoke-test 옵션, 화면 녹화·입력 재생 없음

Node 22의 native cpSync가 한글 Windows 경로에서 비정상 종료하여,
패키징 복사는 경로별 디렉터리 생성·파일 복사 방식으로 변경했습니다.
rcedit 5.0.2는 유지보수 중단 표시가 있는 빌드 전용 의존성이며, 잠금 파일로 고정하고 배포물에서는 제외합니다.
