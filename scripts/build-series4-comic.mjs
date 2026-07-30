import fs from "node:fs/promises";
import path from "node:path";
import { fileURLToPath } from "node:url";
import sharp from "sharp";

const scriptDir = path.dirname(fileURLToPath(import.meta.url));
const projectDir = path.resolve(scriptDir, "..");
const assetDir = path.join(projectDir, "docs", "assets", "series4-comic");
const sourceDir = path.join(assetDir, "source");

const width = 1080;
const height = 1350;
const colors = {
  cream: "#FFF8E8",
  paper: "#FFFDF8",
  navy: "#153A4A",
  teal: "#14808A",
  coral: "#EB684A",
  gold: "#F2B44B",
  muted: "#536B75",
};

const cards = [
  {
    source: "00-cover-source.png",
    output: "01-cover.png",
    step: "공공 AX 로컬 · SERIES 4",
    title: ["업무 시연이", "다시 실행된다"],
    caption: ["영상과 마우스·키보드 이벤트를", "한 타임라인에 기록하는 로컬 매크로"],
    accent: colors.coral,
  },
  {
    source: "01-record-source.png",
    output: "02-record.png",
    step: "1 · 녹화 시작",
    title: ["몇 초 뒤,", "업무를 그대로 시연"],
    caption: ["화면 녹화와 입력 이벤트 수집이", "같은 시각 기준으로 함께 시작됩니다."],
    accent: colors.coral,
  },
  {
    source: "02-events-source.png",
    output: "03-events.png",
    step: "2 · 이벤트도 함께",
    title: ["어디서 무엇을 했는지", "영상 위에서 확인"],
    caption: ["클릭·키보드 조작을 시간과 위치에 연결해", "로그와 영상 오버레이로 보여줍니다."],
    accent: colors.teal,
  },
  {
    source: "03-library-source.png",
    output: "04-library.png",
    step: "3 · 날짜별 기록 저장소",
    title: ["지난 업무 기록도", "선택해서 다시 열기"],
    caption: ["날짜와 시간별로 저장된 기록을 찾아", "영상·로그·실행 순서를 함께 불러옵니다."],
    accent: colors.gold,
  },
  {
    source: "04-safety-source.png",
    output: "05-safety.png",
    step: "4 · 실행 전 안전 확인",
    title: ["중지키는 항상 준비,", "애매한 동작은 검토"],
    caption: ["Ctrl + Shift + F12로 즉시 중지하고", "드래그와 민감 입력은 실행 전에 확인합니다."],
    accent: colors.teal,
  },
];

function escapeXml(value) {
  return value
    .replaceAll("&", "&amp;")
    .replaceAll("<", "&lt;")
    .replaceAll(">", "&gt;");
}

function textLines(lines, x, y, lineHeight, className) {
  return lines
    .map(
      (line, index) =>
        `<text x="${x}" y="${y + index * lineHeight}" class="${className}">${escapeXml(line)}</text>`,
    )
    .join("\n");
}

function overlaySvg(card) {
  return Buffer.from(`
    <svg width="${width}" height="${height}" viewBox="0 0 ${width} ${height}" xmlns="http://www.w3.org/2000/svg">
      <style>
        text {
          font-family: "Malgun Gothic", "Noto Sans CJK KR", sans-serif;
          text-rendering: geometricPrecision;
        }
        .step { font-size: 29px; font-weight: 800; fill: ${colors.paper}; letter-spacing: 1px; }
        .title { font-size: 61px; font-weight: 900; fill: ${colors.navy}; }
        .caption { font-size: 31px; font-weight: 700; fill: ${colors.muted}; }
        .brand { font-size: 24px; font-weight: 800; fill: ${colors.teal}; letter-spacing: 1px; }
      </style>

      <rect width="${width}" height="${height}" fill="${colors.cream}"/>
      <rect x="42" y="34" width="996" height="1282" rx="38" fill="none" stroke="${colors.navy}" stroke-width="5"/>
      <rect x="76" y="65" width="410" height="52" rx="26" fill="${card.accent}"/>
      <text x="281" y="101" text-anchor="middle" class="step">${escapeXml(card.step)}</text>

      ${textLines(card.title, 78, 177, 71, "title")}

      <rect x="70" y="1120" width="940" height="166" rx="27" fill="${colors.paper}" stroke="${card.accent}" stroke-width="5"/>
      ${textLines(card.caption, 106, 1182, 47, "caption")}
      <text x="973" y="1297" text-anchor="end" class="brand">공공 AX 업무 매크로</text>
    </svg>
  `);
}

await fs.mkdir(assetDir, { recursive: true });

for (const card of cards) {
  const illustration = await sharp(path.join(sourceDir, card.source))
    .resize(930, 770, {
      fit: "contain",
      background: colors.cream,
      withoutEnlargement: false,
    })
    .png()
    .toBuffer();

  await sharp({
    create: {
      width,
      height,
      channels: 4,
      background: colors.cream,
    },
  })
    .composite([
      { input: overlaySvg(card), left: 0, top: 0 },
      { input: illustration, left: 75, top: 330 },
    ])
    .png({ compressionLevel: 9, adaptiveFiltering: true })
    .toFile(path.join(assetDir, card.output));
}

console.log(`Built ${cards.length} cards in ${assetDir}`);
