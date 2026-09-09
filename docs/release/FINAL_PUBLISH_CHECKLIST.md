# TouchZoomBoard3 3.0.2 공개 체크리스트

## 완료된 정적 항목

- [x] 코드·정보창·트레이 표시 버전 `3.0.2`
- [x] `AssemblyInformationalVersion("3.0.2")`
- [x] README·Release Notes·홈페이지·라이선스 정식판 전환
- [x] GitHub Actions와 배포 스크립트 버전 `3.0.2`
- [x] 오류·필기 진단 로그 파일 생성 코드 제거
- [x] 트레이 진단 로그 메뉴 제거
- [x] 소스 패키지와 SHA-256 생성

## Windows에서 완료할 항목

- [ ] `Release / x64` 오류 0, 경고 0
- [ ] Windows 10 22H2 x64 실행
- [ ] Windows 11 x64 실행
- [ ] 100·125·150% DPI UI 확인
- [ ] HDMI 복제 + USB Touch 전자칠판 확인
- [ ] 전자칠판에서 직선→펜 가벼운 터치 10회 및 펜 길게 누르기 메뉴 확인
- [ ] `VALIDATION_GUIDE.md`의 100·150·200·300% 필기
- [ ] 굴절광 프리셋·사용자 지정 색 저장과 재시작 복원
- [ ] 로그 파일이 새로 생성되지 않음
- [ ] 새 폴더에서 배포 ZIP 실행
- [ ] EXE·ZIP SHA-256 일치

## GitHub 공개

```bash
git tag -a v3.0.2 -m "TouchZoomBoard3 3.0.2"
git push origin v3.0.2
```

- [ ] GitHub Actions 성공
- [ ] Release 초안에 EXE·Windows x64 ZIP·SHA-256 첨부
- [ ] `RELEASE_NOTES.md` 내용 확인
- [ ] Pre-release 표시가 꺼져 있음
- [ ] Release 공개 후 홈페이지 다운로드 링크 확인
