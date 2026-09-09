# 빌드 안내

## 준비

- Visual Studio 또는 Build Tools
- `.NET 데스크톱 개발` 워크로드
- .NET Framework 4.8 SDK와 Targeting Pack
- Windows x64 환경

C/C++ 워크로드와 외부 NuGet 패키지는 필요하지 않습니다.

## 정식 3.0.2 빌드

1. `TouchZoomBoard3.sln`을 엽니다.
2. `Release / x64`를 선택합니다.
3. 솔루션을 다시 빌드합니다.
4. 저장소 루트의 `build_release.bat`을 실행합니다.

또는 PowerShell에서 직접 실행합니다.

```powershell
.\build_release.ps1 -Version "3.0.2"
```

`dist`에 다음 파일이 생성됩니다.

- `TouchZoomBoard3.exe`
- `TouchZoomBoard3_3.0.2_Windows_x64.zip`
- `TouchZoomBoard3_3.0.2_SHA256.txt`

빌드 스크립트는 `AssemblyInformationalVersion` 일치 여부를 확인하고 ZIP에서 PDB, 로그, 소스와 내부 개발 문서를 제외합니다.
