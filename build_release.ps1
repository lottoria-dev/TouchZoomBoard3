param(
    [string]$Version = "3.0.2"
)

$ErrorActionPreference = "Stop"
if ($Version -ne '3.0.2') {
    throw "정식 배포 버전은 3.0.2여야 합니다: $Version"
}
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$solution = Join-Path $root "TouchZoomBoard3.sln"
$assemblyInfo = Join-Path $root "TouchZoomBoard3\Properties\AssemblyInfo.cs"
$projectOutput = Join-Path $root "TouchZoomBoard3\bin\Release"
$dist = Join-Path $root "dist"
$packageName = "TouchZoomBoard3_${Version}_Windows_x64"
$packageDir = Join-Path $dist $packageName
$zipPath = Join-Path $dist ($packageName + ".zip")
$checksumPath = Join-Path $dist ("TouchZoomBoard3_${Version}_SHA256.txt")

function Find-MSBuild {
    $vswhere = Join-Path ${env:ProgramFiles(x86)} "Microsoft Visual Studio\Installer\vswhere.exe"
    if (Test-Path $vswhere) {
        $candidate = & $vswhere -latest -products * -requires Microsoft.Component.MSBuild -find "MSBuild\**\Bin\MSBuild.exe" | Select-Object -First 1
        if ($candidate) { return $candidate }
    }

    $command = Get-Command msbuild.exe -ErrorAction SilentlyContinue
    if ($command) { return $command.Source }
    throw "MSBuild를 찾지 못했습니다. Visual Studio에서 .NET 데스크톱 개발 워크로드를 설치하십시오."
}

$expectedInformationalVersion = '[assembly: AssemblyInformationalVersion("' + $Version + '")]'
if (!(Select-String -Path $assemblyInfo -SimpleMatch $expectedInformationalVersion -Quiet)) {
    throw "AssemblyInformationalVersion이 배포 버전과 일치하지 않습니다: $Version"
}

Write-Host "[1/5] Release x64 빌드"
$msbuild = Find-MSBuild
& $msbuild $solution /t:Rebuild /p:Configuration=Release /p:Platform=x64 /m /nologo
if ($LASTEXITCODE -ne 0) { throw "MSBuild가 오류 코드 $LASTEXITCODE 로 종료되었습니다." }

$exe = Join-Path $projectOutput "TouchZoomBoard3.exe"
$config = Join-Path $projectOutput "TouchZoomBoard3.exe.config"
if (!(Test-Path $exe)) { throw "빌드 결과 파일을 찾지 못했습니다: $exe" }
if (!(Test-Path $config)) { throw "설정 파일을 찾지 못했습니다: $config" }

Write-Host "[2/5] 배포 폴더 준비"
if (Test-Path $dist) { Remove-Item $dist -Recurse -Force }
New-Item $packageDir -ItemType Directory -Force | Out-Null

$packageFiles = @(
    $exe,
    $config,
    (Join-Path $root "README.md"),
    (Join-Path $root "LICENSE.txt"),
    (Join-Path $root "PRIVACY.md"),
    (Join-Path $root "DEPENDENCIES.md"),
    (Join-Path $root "THIRD_PARTY_NOTICES.txt"),
    (Join-Path $root "SUPPORT.md"),
    (Join-Path $root "RELEASE_NOTES.md")
)
foreach ($file in $packageFiles) { Copy-Item $file $packageDir -Force }
Copy-Item $exe (Join-Path $dist "TouchZoomBoard3.exe") -Force

$forbidden = Get-ChildItem $packageDir -Recurse -File | Where-Object {
    $_.Extension -in @('.pdb', '.cs', '.log') -or
    $_.Name -match '^(BETA|DEVELOPMENT_)' -or
    $_.Name -eq 'touchzoomboard.html'
}
if ($forbidden) {
    throw "배포 폴더에 제외 대상 파일이 있습니다: $($forbidden.FullName -join ', ')"
}

Write-Host "[3/5] ZIP 생성"
Compress-Archive -Path (Join-Path $packageDir "*") -DestinationPath $zipPath -CompressionLevel Optimal

Write-Host "[4/5] SHA-256 생성"
$exeHash = (Get-FileHash (Join-Path $dist "TouchZoomBoard3.exe") -Algorithm SHA256).Hash.ToLowerInvariant()
$zipHash = (Get-FileHash $zipPath -Algorithm SHA256).Hash.ToLowerInvariant()
@(
    "$exeHash *TouchZoomBoard3.exe",
    "$zipHash *$($packageName).zip"
) | Set-Content $checksumPath -Encoding ASCII

Write-Host "[5/5] 완료"
Write-Host "실행 파일 : $(Join-Path $dist 'TouchZoomBoard3.exe')"
Write-Host "배포 ZIP  : $zipPath"
Write-Host "SHA-256   : $checksumPath"
