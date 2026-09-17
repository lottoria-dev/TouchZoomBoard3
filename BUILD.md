# 빌드 안내

## 준비

- Visual Studio 또는 Build Tools
- `.NET 데스크톱 개발` 워크로드
- .NET Framework 4.8 SDK와 Targeting Pack
- Windows x64 환경

C/C++ 워크로드와 외부 NuGet 패키지는 필요하지 않습니다.

## 정식 3.0.5 빌드

1. `TouchZoomBoard3.sln`을 엽니다.
2. `Release / x64`를 선택합니다.
3. 솔루션을 다시 빌드합니다.
4. 저장소 루트의 `build_release.bat`을 실행합니다.

또는 PowerShell에서 직접 실행합니다.

```powershell
.\build_release.ps1 -Version "3.0.5"
```

`dist`에 다음 파일이 생성됩니다.

- `TouchZoomBoard3.exe`
- `TouchZoomBoard3_3.0.5_Windows_x64.zip`
- `TouchZoomBoard3_3.0.5_SHA256.txt`

빌드 스크립트는 `AssemblyInformationalVersion` 일치 여부를 확인하고 ZIP에서 PDB, 로그, 소스와 내부 개발 문서를 제외합니다.

## 자동 회귀 테스트

`build_release.bat`은 앱을 빌드한 뒤 `tests/TouchZoomBoard3.RegressionTests.csproj`를 빌드하고 21개 테스트 그룹을 실행합니다. 실제 코드·WPF 객체를 사용하며 테스트용 외부 NuGet 패키지는 필요하지 않습니다. 실패하면 배포 폴더 준비 전에 중단합니다. 실패한 실행 후 남아 있는 이전 `dist` 파일을 새 배포로 올리지 마십시오.

테스트만 다시 실행하려면 Visual Studio 개발자 PowerShell에서 실행합니다.

```powershell
msbuild .\tests\TouchZoomBoard3.RegressionTests.csproj /t:Rebuild /p:Configuration=Release /p:Platform=x64
.\tests\bin\Release\TouchZoomBoard3.RegressionTests.exe
```

자동 테스트는 실제 장치의 터치 입력이나 앱 간 포커스 전환을 대체하지 않습니다. 배포 전에는 사용 환경에서 원 그리기, 지우개·전체 지움 되돌리기, 프레젠터 다음/이전 슬라이드와 팝업 표시를 확인합니다.
