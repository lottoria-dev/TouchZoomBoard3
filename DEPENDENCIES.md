# 의존성 및 라이선스 검토

## 함께 배포되는 외부 라이브러리

없음.

프로젝트에는 `PackageReference`, NuGet 패키지, Python 패키지, 네이티브 제3자 DLL이 없습니다.

## 사용하는 Windows/.NET 구성요소

| 구성요소 | 용도 | 배포 방식 |
|---|---|---|
| .NET Framework 4.8 WPF | 패널과 필기 UI | Windows에 설치된 런타임 사용 |
| Windows Forms NotifyIcon | 시스템 트레이 | .NET Framework 구성요소 사용 |
| Magnification.dll | 라이브 줌 | Windows 시스템 DLL 직접 호출 |
| dwmapi.dll | Windows 11 Acrylic·둥근 모서리·화면 합성 | Windows 시스템 DLL 직접 호출 |
| user32.dll | 창 배치 및 갱신 | Windows 시스템 DLL 직접 호출 |
| Shcore.dll | 모니터 DPI 조회 | Windows 시스템 DLL 직접 호출 |

위 구성요소는 사용자 PC의 Windows와 .NET Framework에서 제공되며 배포 ZIP에 복사하지 않습니다.

## 자체 제작 자산

- `TouchZoomBoard3.ico`: lottoria-dev 프로젝트 자산
- 사각형·타원·선분·화살표: WPF 기본 벡터 도형으로 코드에서 생성
- UI 글자와 도형: 외부 아이콘·이모지 파일을 사용하지 않음

## 향후 의존성 정책

외부 라이브러리가 반드시 필요한 경우 MIT, BSD-2-Clause, BSD-3-Clause 또는 Apache-2.0 라이선스를 우선 검토합니다. GPL, AGPL, 출처 불명 자산, 상업 배포 제한 자산은 사용하지 않습니다.

이 문서는 개발상 라이선스 점검 기록이며 법률 자문을 대신하지 않습니다.

## GitHub 빌드 자동화 전용 도구

다음 GitHub Actions는 저장소의 빌드·배포 자동화에서만 사용하며 최종 프로그램에 포함하거나 함께 재배포하지 않습니다.

| Action | 버전 | 용도 | 라이선스 |
|---|---:|---|---|
| `actions/checkout` | v4 | 저장소 소스 체크아웃 | MIT |
| `microsoft/setup-msbuild` | v2 | Windows runner의 MSBuild 경로 설정 | MIT |
| `actions/upload-artifact` | v4 | 빌드 결과 보관 | MIT |

Release 초안 생성에는 GitHub-hosted runner에 포함된 GitHub CLI와 저장소 기본 토큰만 사용하며 별도 비밀 키를 요구하지 않습니다.
