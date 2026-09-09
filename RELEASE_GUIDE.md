# GitHub 배포 안내

## 정식 3.0.2

1. 정리된 소스를 `main` 또는 릴리스 준비 브랜치에 반영합니다.
2. GitHub Actions의 Build가 성공하는지 확인합니다.
3. Windows 실제 장치에서 `VALIDATION_GUIDE.md`를 수행합니다.
4. EXE·ZIP·SHA-256 파일 이름과 버전이 모두 `3.0.2`인지 확인합니다.
5. 다음 태그로 GitHub Release 초안을 만듭니다.

```bash
git tag -a v3.0.2 -m "TouchZoomBoard3 3.0.2"
git push origin v3.0.2
```

워크플로는 실행 파일, Windows x64 ZIP과 SHA-256을 첨부한 정식 Release 초안을 만듭니다. 새 폴더에서 ZIP을 풀어 최종 실행하고 SHA-256을 다시 확인한 뒤 공개합니다. 홈페이지 링크는 `v3.0.2` Release를 가리켜야 합니다.
