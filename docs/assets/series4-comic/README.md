# Series 4 고양이 안내 카드

최종 카드 5장은 1080×1350 PNG입니다.

1. `01-cover.png` — 제품 개요
2. `02-record.png` — 지연 녹화와 업무 시연
3. `03-events.png` — 영상과 입력 이벤트 연결
4. `04-library.png` — 날짜별 기록 저장소
5. `05-safety.png` — 긴급중지와 검토 경계

`source/`의 그림은 이 프로젝트를 위해 새로 생성했으며, 참고 이미지는 포함하지
않았습니다. 글자가 생성 과정에서 깨지지 않도록 제목과 설명은
`scripts/build-series4-comic.mjs`가 후처리로 합성합니다.

카드를 다시 만들려면 Node.js와 `sharp`가 필요합니다.

```powershell
npm install --no-save sharp
node .\scripts\build-series4-comic.mjs
```

이미지와 라이선스 범위는
[`THIRD_PARTY_NOTICES.md`](../../../THIRD_PARTY_NOTICES.md)를 확인하세요.
