# 제3자 소프트웨어 및 이미지 고지

이 저장소는 소스 코드를 공개하며 GitHub Release에서 Windows x64 self-contained
설치 파일과 portable ZIP도 제공합니다. NuGet 패키지와 생성 바이너리는 Git에
커밋하지 않고 Release 빌드에서 잠금 파일에 기록된 버전으로 복원합니다.

## Release에 동봉하는 고지와 소스

Electron 개발 소스는 `studio/`에 별도로 포함합니다. React·React DOM·Three.js·Electron은 MIT,
lucide-react는 ISC, IBM Plex 글꼴은 OFL-1.1입니다. 패키지 버전은
`studio/package-lock.json`, 글꼴 전문은 `studio/public/licenses/`를 참조하세요.
현재 새 Electron 바이너리 설치파일은 배포하지 않습니다. 향후 바이너리 배포 시에는
Electron/Chromium 및 포함 패키지의 라이선스·제3자 고지도 함께 동봉해야 합니다.

각 바이너리 배포물에는 다음 항목이 들어갑니다.

- 프로젝트 `LICENSE.txt`와 이 `THIRD_PARTY_NOTICES.md`
- `licenses/` 아래 ScreenRecorderLib, SharpHook, libuiohook, Inno Setup 라이선스
- `licenses/dotnet/` 아래 실제 배포된 .NET Runtime 팩과 Windows Desktop Runtime
  팩의 버전별 라이선스·제3자 고지
- `licenses/corresponding-source/SharpHook-7.1.3-Corresponding-Source.zip`

대응 소스 ZIP은 SharpHook `v7.1.3`과 그 버전이 고정한 libuiohook 서브모듈 전체를
포함합니다. 같은 파일을 GitHub Release의 독립 asset으로도 제공합니다.

## NuGet 런타임 의존성

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

SharpHook는 네이티브 라이브러리 **libuiohook**를 래핑합니다. SharpHook 7.1.3이
가리키는 libuiohook 커밋은
`104624bfd3c69e558e56fd8aff11ea61bc24b224`이며 LGPL-3.0-or-later입니다.

- 정확한 소스:
  <https://github.com/TolikPylypchuk/libuiohook/tree/104624bfd3c69e558e56fd8aff11ea61bc24b224>
- LGPL 전문:
  [`licenses/libuiohook-LGPL-3.0.txt`](licenses/libuiohook-LGPL-3.0.txt)
- LGPL이 참조하는 GPL 전문:
  [`licenses/libuiohook-GPL-3.0.txt`](licenses/libuiohook-GPL-3.0.txt)

Release는 단일 파일 합성이나 native trimming을 사용하지 않습니다. `uiohook.dll`은
EXE 옆의 독립 파일로 배포되어 사용자가 ABI 호환 수정 버전으로 교체할 수 있으며,
설치 프로그램도 그 교체를 기술적으로 막지 않습니다. 정확한 대응 소스와 라이선스
전문을 배포물 안에 함께 넣어 LGPL 재배포 조건을 충족하도록 구성했습니다.

### 테스트 전용 의존성

다음 패키지는 `tests/Series4.Desktop.Tests`에서만 사용하며 런타임 배포물에는
포함하지 않습니다.

- Microsoft.NET.Test.Sdk 18.8.1 — MIT — <https://github.com/microsoft/vstest>
- xunit 2.9.3 — Apache-2.0 — <https://github.com/xunit/xunit>
- xunit.runner.visualstudio 3.1.5 — Apache-2.0 — <https://github.com/xunit/visualstudio.xunit>

## .NET

Release는 .NET 10 Windows Desktop Runtime을 self-contained 형태로 포함합니다.
빌드에 사용된 .NET 루트의 `LICENSE.txt`와 `ThirdPartyNotices.txt`를 그대로
`licenses/dotnet/`에 복사합니다. 일반 사용자는 .NET을 별도로 설치할 필요가 없습니다.

## Inno Setup 6.7.3

설치 EXE는 Inno Setup 6.7.3으로 만듭니다. Inno Setup의 원 저작자를 앱의 저작자로
표현하지 않으며 라이선스 전문을 [`licenses/Inno-Setup.txt`](licenses/Inno-Setup.txt)에
동봉합니다. Inno Setup은 설치 파일을 만드는 빌드 도구이며 portable ZIP에는
해당 설치 런타임이 들어가지 않습니다.

## Microsoft Visual C++ x64 Runtime 선행조건

ScreenRecorderLib 6.6.0의 네이티브 DLL은 `CONCRT140.dll`, `MSVCP140.dll`,
`VCRUNTIME140.dll`, `VCRUNTIME140_1.dll`을 사용합니다. 이 프로젝트는 Visual C++
Redistributable이나 개별 Microsoft CRT DLL을 Release에 포함하지 않습니다.

설치 프로그램은 대상 PC에 x64 Runtime이 없을 때 Microsoft 공식 주소
`https://aka.ms/vc14/vc_redist.x64.exe`에서 직접 내려받고, Windows가 확인한
Authenticode 서명의 발급 대상이 Microsoft인지 검사한 뒤 Microsoft 설치 프로그램을
실행합니다. 따라서 이 저장소가 Microsoft 바이너리를 재배포하지 않습니다. ZIP형은
해당 런타임이 이미 설치된 PC를 전제로 합니다.

- Microsoft 공식 안내:
  <https://learn.microsoft.com/en-us/cpp/windows/latest-supported-vc-redist>

## 안내 이미지

`docs/assets/series4-comic/`의 고양이 안내 이미지는 이 프로젝트를 위해 새로 생성한
결과물입니다. 참고 이미지의 파일·캐릭터·문구·로고는 저장소에 포함하지 않았고,
생성 결과의 한글 제목과 설명은 프로젝트 스크립트로 별도 합성했습니다.

OpenAI 이용약관은 적용 법률이 허용하는 범위에서 이용자가 출력물을 소유한다고
정하지만 AI 출력은 고유하지 않을 수 있다고도 밝힙니다. 공개 전 사람의 유사성
검토를 거쳤고 알려진 제3자 로고·상표·서명은 넣지 않았습니다.

- OpenAI Terms of Use: <https://openai.com/policies/terms-of-use/>

## 이 프로젝트 자체의 라이선스

별도 표시된 제3자 구성요소를 제외한 이 저장소의 소스, 문서, 프로젝트 전용 이미지는
루트 [`LICENSE`](LICENSE)의 MIT 조건으로 제공합니다.
