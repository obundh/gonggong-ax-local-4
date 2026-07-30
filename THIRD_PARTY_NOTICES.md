# 제3자 소프트웨어 및 이미지 고지

이 저장소는 **소스 코드만 공개**합니다. NuGet 패키지, .NET 런타임,
`uiohook.dll`, 실행 파일, PDB는 Git에 포함하지 않습니다.

## NuGet 의존성

### ScreenRecorderLib 6.6.0

- 용도: Windows 화면 녹화
- 저작권자: Sverre Kristoffer Skodje
- 라이선스: MIT
- 공식 프로젝트: <https://github.com/sskodje/ScreenRecorderLib>
- 전문: [`licenses/ScreenRecorderLib-MIT.txt`](licenses/ScreenRecorderLib-MIT.txt)

### SharpHook 7.1.3

- 용도: 전역 마우스·키보드 이벤트 수집 및 입력 재생
- 저작권자: Anatoliy Pylypchuk
- 라이선스: MIT
- 패키지 소스 커밋:
  <https://github.com/TolikPylypchuk/SharpHook/tree/bcb41a4b4a1901ef6c3d4de129e273c8bdafbbd4>
- 전문: [`licenses/SharpHook-MIT.txt`](licenses/SharpHook-MIT.txt)

SharpHook는 네이티브 라이브러리 **libuiohook**를 래핑합니다. SharpHook
7.1.3의 소스 커밋이 가리키는 libuiohook 버전은
`104624bfd3c69e558e56fd8aff11ea61bc24b224`이며 LGPL-3.0-or-later입니다.

- 정확한 소스:
  <https://github.com/TolikPylypchuk/libuiohook/tree/104624bfd3c69e558e56fd8aff11ea61bc24b224>
- LGPL 전문:
  [`licenses/libuiohook-LGPL-3.0.txt`](licenses/libuiohook-LGPL-3.0.txt)
- LGPL이 참조하는 GPL 전문:
  [`licenses/libuiohook-GPL-3.0.txt`](licenses/libuiohook-GPL-3.0.txt)

이 저장소를 바탕으로 실행 파일을 다시 배포할 때에는 LGPL 조건을 별도로
검토해야 합니다. 최소한 라이선스 전문과 저작권 고지를 동봉하고, 배포한
libuiohook의 정확한 대응 소스를 제공하며, 사용자가 수정 버전으로 교체하거나
재링크할 수 있는 조건을 방해하지 않아야 합니다. 이 저장소는 그 검토가 끝나지
않은 바이너리를 공개 릴리스로 제공하지 않습니다.

## .NET

소스 빌드는 Microsoft .NET SDK를 사용합니다. 이 저장소에는 .NET 런타임
바이너리가 없습니다. 향후 self-contained 실행 파일을 배포하면 해당 .NET
Runtime/WPF 버전의 MIT 라이선스 및 `THIRD-PARTY-NOTICES`를 배포물에 함께
넣어야 합니다.

## 안내 이미지

`docs/assets/series4-comic/`의 고양이 안내 이미지는 이 프로젝트를 위해 새로
생성한 결과물입니다. 참고 이미지의 파일·캐릭터·문구·로고는 저장소에 포함하지
않았고, 생성 결과의 한글 제목과 설명은 프로젝트 스크립트로 별도 합성했습니다.

OpenAI 이용약관은 적용 법률이 허용하는 범위에서 이용자가 출력물을 소유한다고
정하지만, AI 출력은 고유하지 않을 수 있다고도 밝힙니다. 따라서 공개 전 사람의
유사성 검토를 거쳤고, 알려진 제3자 로고·상표·서명은 넣지 않았습니다.

- OpenAI Terms of Use: <https://openai.com/policies/terms-of-use/>

## 이 프로젝트 자체의 라이선스

별도 표시된 제3자 구성요소를 제외한 이 저장소의 소스, 문서, 프로젝트 전용
이미지는 루트 [`LICENSE`](LICENSE)의 MIT 조건으로 제공합니다.
