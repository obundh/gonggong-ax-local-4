# 오픈소스 구성요소

공공 AX 업무 매크로 v4.0.1 Windows x64 배포물의 런타임 구성요소입니다.

| 구성요소 | 버전 또는 커밋 | 라이선스 | 배포 파일 |
|---|---|---|---|
| ScreenRecorderLib | 6.6.0 | MIT | `ScreenRecorderLib.dll` |
| SharpHook | 7.1.3 / `bcb41a4b4a1901ef6c3d4de129e273c8bdafbbd4` | MIT | `SharpHook.dll` |
| libuiohook | `104624bfd3c69e558e56fd8aff11ea61bc24b224` | LGPL-3.0-or-later | `uiohook.dll` |
| Microsoft .NET/Windows Desktop Runtime | runtimeconfig에 기록된 10.0.x | MIT 및 제3자 고지 | self-contained runtime 파일 |

`uiohook.dll`은 EXE 옆에 있는 독립 네이티브 라이브러리입니다. ABI가 호환되는
수정 빌드로 교체할 수 있으며 앱은 해시 검사로 교체를 막지 않습니다. 배포한
바이너리와 정확히 대응하는 전체 소스는 다음 위치에 있습니다.

```text
licenses/corresponding-source/SharpHook-7.1.3-Corresponding-Source.zip
```

위 ZIP에는 SharpHook 7.1.3 소스와 실제 사용한 libuiohook 서브모듈 소스가 함께
들어갑니다. GitHub Release에서도 같은 이름의 독립 asset으로 받을 수 있습니다.

각 라이선스 전문과 저작권 고지는 `licenses/`와 `THIRD_PARTY_NOTICES.md`, 실제
.NET 런타임 팩 고지는 `licenses/dotnet/`, 기계 판독용 목록은 `SBOM.spdx.json`을
확인하세요.

## 별도 Windows 선행조건

화면 녹화용 `ScreenRecorderLib.dll`은 Microsoft Visual C++ x64 Runtime과 Windows
Media Foundation을 사용합니다. Microsoft CRT 바이너리는 이 배포물에 포함하지
않습니다. 설치형은 누락 시 Microsoft 공식 서버에서 직접 내려받고 서명을 확인하며,
ZIP형은 해당 Runtime이 이미 설치된 PC를 전제로 합니다. Windows N/KN은 Windows
선택적 기능의 Media Feature Pack이 필요합니다.
