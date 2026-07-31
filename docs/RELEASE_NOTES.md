처음 설치하는 분은 **`GonggongAX-Series4-Setup-x64-v4.0.1.exe`** 하나를 받으세요.
압축만 풀어 쓰려면 `GonggongAX-Series4-Portable-x64-v4.0.1.zip`을 받으면 됩니다.
GitHub가 자동으로 붙이는 `Source code` ZIP은 실행 프로그램이 아닙니다.

### 이번 배포에 포함된 것

- .NET을 따로 설치하지 않아도 실행되는 Windows x64 self-contained 앱
- 앱 자체는 관리자 권한을 요구하지 않는 현재 사용자용 설치·제거 프로그램
- 화면 영상, 마우스·키보드 이벤트 기록, 로그 편집과 실제 재실행
- 영상 위 이벤트 위치·종류 오버레이와 실행 결과 로그
- 경고를 차단으로 바꾸지 않는 실행 정책과 `Ctrl + Shift + F12` 긴급 중지
- 라이선스 전문, .NET 고지, SharpHook/libuiohook 정확한 대응 소스

### 설치 전에

이 배포의 EXE는 **코드 서명 인증서로 서명되지 않았습니다.** Microsoft Defender
SmartScreen 경고가 나타날 수 있습니다. 공식 저장소의 이 Release인지와 아래
SHA-256이 `SHA256SUMS.txt`와 같은지 확인해 신뢰할 때만 실행하세요. 조직 정책이
계속 차단하면 보안 기능을 끄지 말고 IT 관리자에게 설치 승인을 요청하세요.

지원 환경은 Windows 10/11 x64입니다. 처음에는 메모장과 테스트 문서로
`녹화 → 이벤트 확인 → 실행 → 긴급 중지`를 확인하세요.

화면 녹화 엔진에는 Microsoft Visual C++ x64 Runtime과 Windows Media Foundation이
필요합니다. 설치형은 Visual C++ Runtime이 없는 경우 Microsoft 공식 서버에서
내려받아 Microsoft 서명을 확인한 뒤 설치하며, 이 단계에 한해 관리자 승인이 나타날
수 있습니다. Microsoft 파일은 Release에 재배포하지 않습니다. Windows N/KN은
Media Feature Pack을 Windows 선택적 기능에서 설치해야 합니다.
