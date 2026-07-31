# 공공 AX 로컬 시리즈 4

화면을 녹화하면서 마우스와 키보드 이벤트를 함께 기록하고, 영상과 로그를
검토·편집한 뒤 실제 Windows 프로그램에서 다시 실행하는 로컬 업무 매크로입니다.

## 처음 설치하기

1. [최신 GitHub Release](https://github.com/obundh/gonggong-ax-local-4/releases/latest)를 엽니다.
2. Assets에서 **`GonggongAX-Series4-Setup-x64-v4.0.1.exe`**를 받습니다.
3. 설치 후 시작 메뉴에서 `공공 AX 업무 매크로`를 실행합니다.
4. 별도의 .NET 또는 개발 도구는 설치할 필요가 없습니다.

화면 녹화용 Microsoft Visual C++ x64 런타임이 없는 PC에서는 설치 프로그램이
Microsoft 공식 서버에서 런타임을 내려받습니다. 앱 자체는 현재 사용자 영역에
설치되지만, 이 선행 런타임 설치에 한해 Windows 관리자 승인 창이 나타날 수 있습니다.

설치하지 않고 압축만 풀어 쓰려면
`GonggongAX-Series4-Portable-x64-v4.0.1.zip`을 받으세요. GitHub가 자동으로
표시하는 `Source code (zip)`은 실행 프로그램이 아닙니다.

> 현재 공개 EXE는 코드 서명 인증서로 서명되지 않았습니다. Microsoft Defender
> SmartScreen 경고가 보이면 공식 저장소의 Release인지와 `SHA256SUMS.txt`의
> 해시가 같은지 확인해 신뢰할 때만 실행하세요. 회사 정책이 계속 차단하면
> 보안 기능을 끄지 말고 IT 관리자에게 설치 승인을 요청하세요.

![공공 AX 로컬 시리즈 4 소개 이미지](docs/assets/series4-comic/v2/01-problem.png)

## 5분 사용 흐름

1. 메모장을 열고 앱에서 시작 지연을 `3초`로 선택합니다.
2. `화면 녹화 시작`을 누른 뒤 메모장으로 이동해 짧은 문장을 입력하고 클릭합니다.
3. 앱으로 돌아와 녹화를 종료합니다.
4. 영상 위 이벤트 표시와 오른쪽 로그를 확인하고 필요하면 시간·순서를 고칩니다.
5. 빈 메모장을 준비하고 `매크로 실행`을 눌러 실제 입력이 재현되는지 확인합니다.
6. 실행 중에는 언제든 `Ctrl + Shift + F12`로 중지할 수 있습니다.

처음에는 반드시 테스트 문서와 메모장에서 확인하세요. 자세한 화면별 설명은
[초보자 안내서](docs/BEGINNER_GUIDE.md)에 있습니다.

## 무엇을 할 수 있나요?

- 주 모니터 화면을 MP4로 녹화
- 녹화 중 발생한 마우스 클릭·휠과 키보드 입력을 시간순으로 기록
- 영상 위에 이벤트 종류와 발생 위치를 오버레이로 표시
- 이벤트 삭제, 순서 변경, 시간 수정
- 텍스트·키 조합·클릭·휠 동작을 원하는 시점에 수동 삽입
- 영상 재생 속도 조절
- 편집한 이벤트를 시간순으로 실제 실행
- 실행 중 `Ctrl + Shift + F12` 긴급 중지
- 날짜별 기록 저장소에서 이전 영상과 이벤트 불러오기

녹화 영상과 이벤트 JSON은 날짜별 폴더에 함께 저장됩니다. 미완료 녹화,
영상 길이 밖 시점, 화면 크기 변화, 창 전환 키, 민감정보 의심 항목은 경고로
표시하지만 실행을 막지 않습니다. 깨진 입력 데이터나 현재 엔진이 재생할 수 없는
개별 동작만 실행 대상에서 제외됩니다.

## 현재 한계

이 프로그램은 화면 의미를 이해하지 않고 녹화한 좌표와 키 입력을 재생합니다.
버튼 이름을 찾아 클릭하는 UI 자동화 엔진은 아닙니다.

- Windows 10/11 x64 전용입니다.
- 한 사용자 세션에서 한 인스턴스만 실행합니다.
- 현재 주 모니터 한 대만 녹화합니다.
- 마우스 이동 경로와 드래그는 아직 실행하지 않습니다.
- 녹화 때와 실행 때 화면 위치·해상도가 달라도 경고 후 기존 절대 좌표로 실행합니다.
- 관리자 권한 프로그램은 일반 권한으로 실행한 앱이 조작하지 못할 수 있습니다.
- 조건 대기, OCR/OpenCV 요소 탐색, 분기와 재시도는 아직 없습니다.
- 이벤트 오버레이는 앱의 영상 플레이어에서 표시되며 MP4 자체에 합성되지 않습니다.
- 영상과 입력 이벤트에는 개인정보가 포함될 수 있습니다. 텍스트 의심 형식은
  로컬에서 경고하지만 영상 마스킹이나 암호화 저장은 제공하지 않습니다.

경고는 사용자의 실행 선택을 차단하지 않습니다. 다만 이미 실행된 저장·삭제·전송
동작을 긴급 중지가 되돌려 주지는 않습니다.

## 파일 무결성 확인

Release의 `SHA256SUMS.txt`와 받은 파일의 SHA-256을 비교할 수 있습니다.

```powershell
Get-FileHash .\GonggongAX-Series4-Setup-x64-v4.0.1.exe -Algorithm SHA256
```

표시된 해시가 Release 문서의 값과 다르면 실행하지 말고 다시 다운로드하세요.

## Windows 선행 구성요소

ScreenRecorderLib의 네이티브 녹화 엔진은 Microsoft Visual C++ x64 Runtime과
Windows Media Foundation을 사용합니다.

- 설치형은 Visual C++ Runtime이 없을 때 Microsoft 공식 URL에서 내려받아 서명을
  확인한 뒤 설치합니다. Microsoft 파일을 이 저장소가 재배포하지는 않습니다.
- ZIP형은 PC에 Visual C++ x64 Runtime이 이미 설치되어 있어야 합니다.
- Windows N/KN은 Windows 설정의 선택적 기능에서 Media Feature Pack이 필요합니다.
- Windows Server의 화면 녹화는 현재 지원 범위가 아닙니다.

Microsoft 공식 다운로드는
[최신 Visual C++ Redistributable 안내](https://learn.microsoft.com/en-us/cpp/windows/latest-supported-vc-redist)에서 확인할 수 있습니다.

## 개발자용 소스 빌드

### 요구 환경

- Windows 10 또는 Windows 11 x64
- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- PowerShell 또는 Windows Terminal

```powershell
dotnet restore .\tests\Series4.Desktop.Tests\Series4.Desktop.Tests.csproj --locked-mode
dotnet build .\Series4.Desktop.csproj -c Release -p:Platform=x64 --no-restore
dotnet test .\tests\Series4.Desktop.Tests\Series4.Desktop.Tests.csproj -c Release -p:Platform=x64 --no-restore
```

로컬에서 배포 패키지를 재현하려면 Inno Setup 6과 Git이 필요합니다.

```powershell
.\scripts\build-release.ps1
```

이 스크립트는 self-contained 폴더, 설치 EXE, portable ZIP, 체크섬과
SharpHook/libuiohook 대응 소스를 `artifacts/release`에 만듭니다. 태그
`v4.0.1`을 푸시하면 GitHub Actions가 같은 검증을 거쳐 draft Release를 만들며,
관리자가 자산과 체크섬을 확인한 뒤 공개합니다.

내부 동작과 파일 구조는 [아키텍처 문서](docs/ARCHITECTURE.md), 녹화 데이터 범위는
[개인정보 및 보안 가이드](docs/PRIVACY_AND_SECURITY.md)를 확인하세요.

## 라이선스

프로젝트 자체는 [MIT 라이선스](LICENSE)로 제공합니다. Release에는
ScreenRecorderLib·SharpHook·libuiohook·.NET의 고지와 라이선스, 배포한
`uiohook.dll`의 정확한 대응 소스가 함께 들어갑니다. 자세한 내용은
[제3자 소프트웨어 고지](THIRD_PARTY_NOTICES.md)를 확인하세요.

## 기여할 때

- 경고는 사용자의 실행 선택을 막지 않고 기술적으로 실행 불가능한 이벤트만 제외합니다.
- 긴급 중지와 입력 상태 정리 실패는 전체 실행 중지 조건으로 유지합니다.
- 저장 포맷을 바꿀 때는 기존 `.series4.json` 호환성을 검증합니다.
- 녹화·실행·저장 실패를 성공으로 표시하지 않습니다.
- 실제 외부 프로그램에서 검증하지 않은 동작을 검증 완료라고 표현하지 않습니다.
