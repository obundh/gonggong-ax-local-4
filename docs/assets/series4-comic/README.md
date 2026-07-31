# Series 4 고양이 안내 카드

## 최신 5장 캐러셀

현재 대표 카드 5장은 `v2/`에 있는 1254×1254 PNG입니다.

1. `v2/01-problem.png` — 반복 업무 문제와 시연 기록
2. `v2/02-record.png` — 화면 영상과 입력 이벤트 동시 기록
3. `v2/03-edit.png` — 순서·시간·속도와 수동 이벤트 편집
4. `v2/04-execute.png` — 실제 마우스·키보드 실행과 긴급 정지
5. `v2/05-complete.png` — 로컬 저장과 Series 4 완성 요약

루트 [`README.md`](../../../README.md)의 대표 이미지는 최신 1장을 사용합니다.

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
