# Series 4 고양이 안내 카드

## 최신 5장 캐러셀

현재 대표 카드 5장은 `v3/`에 있는 1254×1254 PNG입니다.

1. `v3/01-intro.png` — 한 번 시연하고 그대로 반복하는 흐름
2. `v3/02-record.png` — 시작 지연 뒤 입력·클릭·드래그 기록
3. `v3/03-review.png` — 영상 옆 이벤트 확인·순서·시간 편집
4. `v3/04-run.png` — 메모장과 그림판 실제 실행, 긴급 정지
5. `v3/05-test-download.png` — 설치와 절대 좌표 실행 환경 맞추기

루트 [`README.md`](../../../README.md)의 대표 이미지는 최신 1장을 사용합니다.

## 실제 동작 캡처

`actual-demo/`에는 새 빈 테스트 창에서 다시 확인한 결과만 저장합니다.
제목 표시줄, 탭, 사용자 경로와 다른 화면은 포함하지 않습니다.

1. `actual-demo/01-notepad-replay.png` — 메모장 `z9final7` 재생 결과
2. `actual-demo/02-macro-success.png` — 텍스트 입력 실행 완료 로그
3. `actual-demo/03-paint-recording.png` — 그림판 삼각형 3획 기록 결과
4. `actual-demo/04-paint-events.png` — 기록된 드래그 이벤트
5. `actual-demo/05-paint-replay.png` — 빈 캔버스에 다시 그린 삼각형
6. `actual-demo/06-paint-replay-success.png` — 드래그 실행 완료 로그 일부
7. `actual-demo/07-run-summary.png` — `성공 3 · 실패 0` 실행 요약

## 기존 합성 카드

이 폴더 루트의 기존 1080×1350 카드와 `source/`는 이전 합성 버전입니다.
제목과 설명은 `scripts/build-series4-comic.mjs`가 후처리로 합성합니다.

기존 카드를 다시 만들려면 Node.js와 `sharp`가 필요합니다.

```powershell
npm install --no-save sharp
node .\scripts\build-series4-comic.mjs
```

이미지와 라이선스 범위는
[`THIRD_PARTY_NOTICES.md`](../../../THIRD_PARTY_NOTICES.md)를 확인하세요.
