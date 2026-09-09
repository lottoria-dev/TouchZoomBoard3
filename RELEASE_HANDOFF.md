# TouchZoomBoard3 3.0.2 릴리스 인수인계

## 릴리스 상태

- 표시 버전, 정보 창, 트레이, 빌드 스크립트와 GitHub Actions를 `3.0.2`으로 통일했습니다.
- `AssemblyInformationalVersion`은 `3.0.2`입니다.
- 홈페이지 다운로드·릴리스·SHA-256 링크는 `v3.0.2`을 가리킵니다.
- 정식판에서 로그 파일 생성과 진단 로그 메뉴를 제거했습니다.

## 최종 수정

- `adaptive-v8` SmoothStep 끝단 이징으로 필기 종료 시 돌출을 완화했습니다.
- 최종 제공 기록의 완료 획 30개에서 하드 클램프 0회, 끝단 잔여 보정 평균 0.107 DIP·최대 0.582 DIP를 확인했습니다.
- 굴절광 선택색을 패널의 옅은 표면색, 방향성 가장자리, 국부 반사광과 버튼 재질에 함께 반영했습니다.
- 흰 테두리 느낌을 줄이고 강화유리 두께감이 드러나도록 가장자리 광학 레이어를 조정했습니다.
- 일반 글자와 아이콘 선 굵기를 낮추고 픽셀 스냅을 해제해 125% DPI에서의 뭉침을 완화했습니다.
- 짧게 누르기와 길게 누르기를 함께 쓰는 버튼의 이동 허용치를 일반 버튼과 같은 12 DIP로 통일해 적외선 전자칠판의 직선→펜 전환 탭이 취소되는 현상을 완화했습니다.

## 설정 호환성

- `VisualDesignVersion`: 22
- 유지 값: 대상 화면, 패널 위치·표시 상태, 유리판 농도, 기본 배율, 툴팁, 도구별 색과 굵기
- 굴절광 값: `PastelTheme`, `UseCustomGlassLightColor`, `CustomGlassLightColorArgb`
- 레지스트리 경로와 자동 실행 이름은 변경하지 않았습니다.

## 배포 절차

```powershell
.\build_release.ps1 -Version "3.0.2"
```

1. Windows에서 `Release / x64` 빌드와 실제 실행을 확인합니다.
2. `VALIDATION_GUIDE.md`와 `docs/release/FINAL_PUBLISH_CHECKLIST.md`를 완료합니다.
3. `v3.0.2` 태그를 푸시합니다.
4. GitHub Release 초안의 EXE·Portable ZIP·SHA-256을 확인합니다.
5. Release를 공개하고 홈페이지를 반영합니다.

## 현재 검증 한계

이 작성 환경에는 .NET Framework 4.8 WPF용 MSBuild와 Windows 그래픽·터치 장치가 없어 EXE 빌드와 실기 시험을 수행하지 못했습니다. 소스 구조, XML, YAML, HTML, 버전 문자열, 문서 링크와 소스 패키지는 정적으로 검증했습니다.
