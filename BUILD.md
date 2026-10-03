# TouchZoomBoard3 3.0.7 빌드 및 게시

## Windows에서 빌드

Visual Studio 또는 Build Tools의 `.NET 데스크톱 개발` 워크로드와 .NET Framework 4.8 SDK/Targeting Pack이 필요합니다. 플랫폼은 Windows x64이며 외부 NuGet 패키지와 C/C++ 워크로드는 필요하지 않습니다.

`TouchZoomBoard3.sln`을 `Release / x64`로 빌드합니다. 앱 빌드·회귀 테스트·배포 파일 생성을 한 번에 진행하려면 저장소 루트의 `build_release.bat`을 실행합니다. PowerShell에서도 실행할 수 있습니다.

```powershell
.\build_release.ps1 -Version "3.0.7"
```

앱과 `tests/TouchZoomBoard3.RegressionTests.csproj`를 빌드하고 Windows WPF 회귀 테스트 31개 그룹을 실행합니다. 실패하면 배포 폴더 준비 전에 중단합니다. 성공하면 `dist`에 다음 파일을 생성합니다.

- `TouchZoomBoard3.exe`
- `TouchZoomBoard3_3.0.7_Windows_x64.zip`
- `TouchZoomBoard3_3.0.7_SHA256.txt`

SHA-256 파일에는 실행 파일과 사용자용 ZIP의 해시를 함께 기록합니다. 배포 ZIP에는 실행 파일·설정 파일·사용 안내·라이선스만 포함하고 소스·PDB·로그·홈페이지·개발 검토 문서는 제외합니다. 소스 ZIP에는 미리 빌드한 실행 파일이 없습니다.

## GitHub에 소스 등록

1. 공개용 소스 묶음을 압축 해제합니다.
2. 그 안의 `TouchZoomBoard3.sln`, `TouchZoomBoard3` 폴더, 문서·빌드 스크립트·`.github` 폴더를 기존 저장소의 루트에 반영합니다. ZIP의 바깥 묶음 폴더가 한 단계 더 들어가지 않도록 합니다.
3. Visual Studio의 Git 변경 내용에서 변경 파일을 확인하고 커밋한 뒤 `lottoria-dev/TouchZoomBoard3`의 `main`에 푸시합니다.

홈페이지와 내부 검토 파일은 공개용 소스 묶음에 포함하지 않습니다. 기존 Git 이력이나 legacy 브랜치는 별도로 유지합니다.

## Release 수동 게시

1. GitHub 저장소의 Releases → 새 릴리스를 엽니다.
2. 태그 **`v3.0.7`**, 제목 **`TouchZoomBoard3 3.0.7`**을 지정합니다. 태그가 이번 소스 커밋을 가리키도록 합니다.
3. `RELEASE_NOTES.md`의 내용을 릴리스 본문에 넣습니다.
4. `dist/TouchZoomBoard3_3.0.7_Windows_x64.zip`과 `dist/TouchZoomBoard3_3.0.7_SHA256.txt`를 첨부합니다. 필요하면 `dist/TouchZoomBoard3.exe`도 함께 첨부합니다.
5. 정식 릴리스로 게시하고 첨부 파일의 공개 다운로드를 확인합니다.

GitHub 등록용 소스 ZIP과 사용자용 Windows ZIP은 용도가 다릅니다. 사용자는 `Windows_x64.zip`을 내려받습니다.

## GitHub Actions 사용

`main` 푸시·PR과 수동 실행은 `build.yml`에서 Windows 빌드·테스트를 수행하고 결과를 보관합니다. **`v3.0.7` 태그 푸시**는 `release.yml`에서 빌드·테스트 후 EXE·사용자용 ZIP·SHA-256이 포함된 **Release 초안**을 생성합니다. 초안 내용을 확인하고 Publish release를 눌러 게시합니다. 이미 수동으로 게시했다면 동일 태그 생성 흐름을 중복 실행하지 않습니다.

## Mathtime 홈페이지 반영

Release를 게시한 뒤 별도로 제공된 3.0.7 HTML의 파일 이름을 `touchzoomboard.html`로 바꾸고 홈페이지의 기존 같은 파일을 교체합니다. 기존 `/js/redirect.js`와 `data-page-key="touchzoomboard"`를 사용하는 SPA 페이지 형식을 유지합니다. 홈페이지 다운로드 버튼은 아래 파일을 가리킵니다.

- `https://github.com/lottoria-dev/TouchZoomBoard3/releases/download/v3.0.7/TouchZoomBoard3_3.0.7_Windows_x64.zip`
- `https://github.com/lottoria-dev/TouchZoomBoard3/releases/download/v3.0.7/TouchZoomBoard3_3.0.7_SHA256.txt`

태그·파일 이름이 다르거나 Release가 초안 상태이면 홈페이지의 다운로드 링크가 연결되지 않습니다.

## 회귀 테스트만 실행

Visual Studio 개발자 PowerShell에서 실행합니다.

```powershell
msbuild .\tests\TouchZoomBoard3.RegressionTests.csproj /t:Rebuild /p:Configuration=Release /p:Platform=x64
.\tests\bin\Release\TouchZoomBoard3.RegressionTests.exe
```

자동 회귀 테스트는 적외선 USB 터치·실제 앱 간 포커스 전환의 모든 상황을 대체하지 않습니다. 설정창 이동, 도구 순서 저장, 확대 필기, 되돌리기와 프레젠터 동작을 실제 수업 장치에서도 확인합니다.
