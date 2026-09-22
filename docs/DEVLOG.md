# WinCal 개발 로그 — 블로그 작성용 자료 정리

> 이 문서는 win-cal 프로젝트의 개발 전 과정을 정리한 것이다.
> 블로그 포스트 작성, 회고, 추후 개선 계획의 원천 자료로 사용한다.

## 1. 프로젝트 개요

- **이름**: WinCal (win-cal)
- **한 줄 소개**: Windows 바탕화면에 달력을 "박아넣는" 데스크톱 위젯. Google 캘린더를 ICS로 읽어와 배경화면 위/아이콘 아래에 표시한다.
- **영감**: DesktopCal (바탕화면에 달력이 붙어있는 유틸리티). 유사하지만 Google 캘린더 동기화와 커스터마이징에 집중한 오픈소스 대안.
- **리포지토리**: https://github.com/jaymunsh/win-cal
- **라이선스**: MIT
- **첫 릴리스**: v0.1.0 (GitHub Releases, 단일 exe)
- **면책**: Google과 무관한 비공식 프로젝트.

### 개발 환경

- Windows 11, .NET SDK 8.x, C# WPF (`net8.0-windows`)
- Windows Forms 연동 (`NotifyIcon` 트레이 아이콘)
- Win32 P/Invoke, `WH_MOUSE_LL` 저수준 마우스 훅
- UI Automation (바탕화면 아이콘 hit-test)
- Ical.Net 5.2.3 (ICS 파싱)
- Pretendard 폰트 번들 (SIL OFL)
- Git + GitHub (Git Credential Manager, HTTPS push)

## 2. 최종 기능 목록

- 바탕화면 임베디드 월간 달력 (배경화면 위, 바탕화면 아이콘 아래)
- 작업표시줄/Alt+Tab에 안 뜨는 창, 트레이 아이콘으로만 제어
- Google 캘린더 ICS 다중 구독 (한 줄에 하나씩, 공휴일 캘린더 포함)
- 캘린더별 색상: ICS의 `COLOR`/`X-WR-CALCOLOR` 우선, 없으면 팔레트 순서
- 종일 일정 = 캘린더 색 배경 칩 + 흰 글씨 (Google 캘린더 스타일)
- 시간 일정 = 색 점 + 시간 + 제목
- 동기화 상태 표시: "동기화 중…" → "HH:mm 동기화됨" / 실패 시 사유 + 트레이 알림
- 플로팅 스티커 메모 (색상 로테이션, 드래그 이동/리사이즈, `stickers.json` 영속화)
- 우측 고정 메모 패널 (더블클릭 → 인라인 편집)
- 위치 조정 모드: 잠금 해제 → 드래그 이동 / 그립 리사이즈 → 완료 시 잠금+저장
- 테마 4종: 다크 / 라이트 / 미니멀 / 웜
- 한국어/영어 UI (월 이름, 요일, 트레이 메뉴 포함 전체)
- 설정: 모니터 선택, UI 배율, 셀/전체 불투명도(최대 100%), 주 시작 요일, 새로고침 주기, 시작 프로그램 등록, 전체화면/영역 모드, 기본값 복원(모양만)
- 단일 exe 배포 (~72MB, .NET 불필요)

## 3. 아키텍처와 핵심 구현

### 파일 구조

```
WinCal/
  App.xaml.cs           — 시작/종료, 트레이 메뉴, 단일 인스턴스 뮤텍스
  MainWindow.xaml(.cs)  — 투명 오버레이 창, 달력 그리드, 스티커/메모 레이어
  CalendarService.cs    — ICS 다운로드/파싱, 반복 전개, 색상 할당, 캐시
  SettingsStore.cs      — AppSettings, JSON 영속화, 시작프로그램 레지스트리
  SettingsWindow        — 설정 UI (다중 ICS URL, 테마, 언어 등)
  StickerStore.cs       — 스티커 모델/영속화 (stickers.json)
  StickerEditWindow     — 스티커 텍스트/삭제
  Themes.cs             — 테마 정의 (브러시 팔레트)
  Loc.cs                — ko/en 문자열 테이블 + CultureInfo
  DesktopAttacher.cs    — WorkerW/XAML island 아래로 창 부착 + 워치독
  DesktopIcons.cs       — UI Automation으로 바탕화면 아이콘 hit-test
  MouseHook.cs          — WH_MOUSE_LL 전역 마우스 훅
  Native/Win32.cs       — P/Invoke 선언
  Debug.cs              — 선택적 디버그 로그 (기본 OFF)
  Assets/               — AppIcon.ico, Pretendard 폰트 + 라이선스
scripts/
  make-icon.ps1         — 멀티사이즈 ico 생성기 (System.Drawing)
```

런타임 데이터는 전부 `%AppData%\WinCal` 아래: `settings.json`, `cache.ics`/`cache.json`, `stickers.json`, `debug.log`(켰을 때만). 리포지토리에는 사용자 ICS URL 같은 민감 정보가 일절 없다.

### 3.1 바탕화면 임베딩 (가장 흥미로운 부분)

목표: 창이 "바탕화면의 일부"처럼 보이게 — 배경화면 위, 바탕화면 아이콘 **아래**.

- 창: `WindowStyle=None`, `AllowsTransparency=True`, `ShowInTaskbar=False`, `ShowActivated=False`, `Topmost=False`
- Windows 11의 바탕화면은 클래식 `Progman → WorkerW → SHELLDLL_DefView` 구조와 **XAML Islands 기반 신형 데스크톱** 두 가지가 공존한다. `DesktopAttacher`가 두 경로를 모두 지원:
  - 클래식: `SHELLDLL_DefView`를 WorkerW로 보내고, 우리 창을 그 아래 z-order에 배치
  - 신형: XAML island 호스트 창을 찾아 그 뒤에 배치
- 5초 주기 워치독: Progman 핸들을 매번 다시 찾아(탐색기 재시작 대응), 위치/z-order가 틀어졌을 때만 재부착. 정상이면 스킵 — 최적화 때 "무조건 MoveWindow"에서 "필요할 때만"으로 바꿨다.
- Win+D(바탕화면 보기)로 창이 최소화되면 5초 내 자동 복구.

### 3.2 클릭 통과 + 전역 마우스 훅

잠금 상태의 위젯은 **클릭 통과**(레이어드 창 + WS_EX_TRANSPARENT)라 WPF 입력이 안 온다. 그래서:

- `WH_MOUSE_LL` 저수준 훅으로 전역 마우스 이벤트를 받는다.
- 스크린 좌표 hit-region 목록(헤더 버튼, 날짜 셀, 스티커, 메모 패널)과 대조.
- 셀 더블클릭이 감지되면: UI Automation으로 그 좌표에 **바탕화면 아이콘이 있는지** 검사 — 아이콘 위면 이벤트를 그대로 흘려보내고(아이콘이 열림), 빈 공간이면 우리가 삼켜서 처리.
- **성능 주의점**: 저수준 훅 콜백이 느리면 Windows가 훅을 강제 제거한다. 그래서 hit-test를 먼저 하고, 실제로 영역에 걸렸을 때만 (느린) UIA 아이콘 조회를 실행하도록 순서를 바꿨다 — 최적화의 핵심 변경.
- 위치 조정 모드에서는 클릭 통과를 끄고 창을 아이콘 위로 올려서 일반 드래그/리사이즈가 되게 한다.

### 3.3 더블클릭이 안 잡혔던 문제 (트러블슈팅 하이라이트)

- 증상: `WM_LBUTTONDBLCLK`를 훅에서 잡도록 했는데 로그에 아예 안 찍힘.
- 원인 추정: Windows 11 신형 데스크톱(XAML island)의 창 클래스에 `CS_DBLCLKS` 스타일이 없어서, 시스템이 더블클릭 메시지 자체를 생성하지 않는다.
- 해결: 시스템 더블클릭 메시지 대신 **마우스 업(up) 이벤트 두 번을 직접 감지** — 시스템 더블클릭 시간(GetDoubleClickTime) 안에 같은 셀에서 up이 두 번 오면 더블클릭으로 판정. OS 레이어 차이에 의존하지 않아 더 견고하다.

### 3.4 캘린더 처리 (CalendarService)

- 설정된 모든 ICS URL을 병렬 다운로드 → Ical.Net으로 파싱 → 반복 일정 전개 → 타임존 변환 → 멀티데이 일정은 날짜별 엔트리로 확장.
- 캘린더 색상: ICS에 `COLOR`/`X-WR-CALCOLOR`가 있으면 사용, 없으면 URL 순서 팔레트(파랑/주황/초록/분홍…). Google ICS export에는 이 속성이 없는 경우가 많다.
- 최적화: `(월 범위, 데이터 버전)` 키로 전개 결과를 캐시 — 매 렌더마다 반복 일정을 다시 전개하지 않음. 브러시도 색상 hex별로 캐시.
- 동기화 재진입 방지: 자동 타이머 틱이 진행 중인 fetch 위에 겹치지 않게 가드.
- **Google ICS 지연 주의**: Google이 피드를 캐시해서 캘린더 변경이 ICS에 반영되기까지 몇 분~몇 시간 걸릴 수 있다. "새로고침했는데 안 바뀐다"는 대부분 이것.

### 3.5 스티커 / 메모 패널

- 원래 "날짜 셀 더블클릭 → 로컬 메모" 기능이 있었는데, Google 캘린더에 반영되지 않는다는 이유로 사용자가 제거를 원했다 → 플로팅 스티커 + 우측 메모 패널로 대체.
- 스티커: 트레이 "스티커 추가" 또는 빈 셀 더블클릭으로 생성. 잠금 상태에선 표시만, 더블클릭하면 텍스트 편집 창. 위치 조정 모드에서는 상단 바 드래그=이동, 우하단 그립=크기, 본문 직접 타이핑, ✕=삭제. 색은 6색 로테이션.
- 우측 메모 패널: 달력 오른쪽 ~190px 고정 메모장. **더블클릭하면 패널만 인라인 편집 모드로 전환** — 위젯이 잠깐 입력 가능 상태가 되고 Enter=저장/Esc=취소/다른 곳 클릭=자동 저장. 별도 창 없이 바로 타이핑 가능하게 한 게 포인트. (이때 워치독이 창을 아이콘 아래로 내리지 않도록 `IsEditingInline` 가드 추가.)

### 3.6 테마 / 지역화

- `Themes.cs`: 테마 객체가 셀 배경/텍스트/헤더/요일색/버튼/액센트 브러시를 통째로 제공. 미니멀 테마는 셀 배경을 의도적으로 연하게(30% 계수) 유지.
- `Loc.cs`: `Loc.T("key")` 문자열 테이블 + `Loc.Culture`(ko-KR/en-US). 트레이 메뉴는 WinForms라 언어 변경 시 리빌드.
- 함정: `MMMM` 포맷이 OS 문화권을 따르는 바람에 영어 UI에서도 "9월"이 나왔다 → `Loc.Culture`로 포맷하도록 수정 ("September 2026").
- 설정 저장 시 테마를 이름 문자열이 아닌 인덱스로 저장하도록 바꿔 언어 독립성 확보.

### 3.7 전체 불투명도가 안 먹던 문제

- `Window.Opacity`를 썼는데 클릭 통과 레이어드 창에서는 무시됐다 → 루트 비주얼(`Root.Opacity`)에 적용해서 해결. 투명 창에서는 창 속성이 아니라 콘텐츠에 opacity를 걸어야 한다는 교훈.

### 3.8 기본값 복원이 "앱을 끈" 것처럼 보인 문제

- 기본값 복원이 `화면 전체 사용`까지 true로 되돌려서 위젯이 보조 모니터의 사용자 지정 영역에서 주 모니터 전체화면으로 점프했다. 사용자 입장에선 "꺼졌다"로 보임.
- 수정: 복원 대상을 모양 관련만으로 제한(폰트/테마/배율/불투명도/주 시작/새로고침). 위치·모니터·레이아웃·시작프로그램·ICS URL은 유지.

## 4. 배포 이야기

### 4.1 단일 exe

```
dotnet publish WinCal -c Release -r win-x64 --self-contained \
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true \
  -p:EnableCompressionInSingleFile=true
```

- self-contained라 .NET 런타임 설치 불필요.
- 압축 옵션(`EnableCompressionInSingleFile`)으로 160MB → **약 72MB**로 축소.
- 산출물: `WinCal/bin/Release/net8.0-windows/win-x64/publish/WinCal.exe`

### 4.2 아이콘

- 원래 트레이 아이콘은 코드로 그린 단순 비트맵이었고 exe 아이콘은 없었다.
- `scripts/make-icon.ps1` — PowerShell + System.Drawing으로 16/24/32/48/64/128/256 PNG를 그려 ICO 컨테이너(PNG payload, Vista+ 지원)로 조립. 디자인: 라운드 흰 페이지 + 파란 그라데이션 헤더 + 바인더 링 2개 + 회색 날짜 그리드 + 오렌지 "오늘" 셀.
- 적용 위치: `<ApplicationIcon>`(exe 자체 → 탐색기/SmartScreen/릴리스 목록), 트레이 아이콘(리소스 스트림 로드), 설정/스티커 창 제목 표시줄.

### 4.3 GitHub Releases + SHA256

- v0.1.0 태그 → Releases에 exe 첨부.
- 미서명이므로 최소한의 신뢰 장치로 **SHA256 체크섬**을 릴리스 노트에 표기: `certutil -hashfile WinCal.exe SHA256`로 사용자가 검증 가능.
- README/릴리스 노트에 "SmartScreen 경고가 뜨면 추가 정보 → 실행" 안내 포함.

## 5. 코드 서명 검토 — 하지 않기로 한 결정과 그 근거

배포용 exe의 최대 걸림돌은 SmartScreen. 검토한 옵션 전부:

| 옵션 | 비용 | SmartScreen 효과 | 결론 |
|---|---|---|---|
| **미서명** | 무료 | 초기 "Windows에서 PC를 보호했습니다" 경고. 해시별 다운로드 평판이 쌓이면 사라짐 | **채택** (v0.1.0) |
| **SignPath Foundation** (무료 OSS 서명) | 무료, 심사 있음 | OV급 인증서. 발급자명 "SignPath Foundation". 소스↔바이너리 연결 증명 | 조건 충족하지만 **시간/절차 이유로 패스** — 필요하면 추후 신청 |
| **Azure Artifact Signing** (구 Trusted Signing) | ~$9.99/월 + 유료 Azure 구독 필요 | 마찬가지로 평판 축적 필요 (즉시 무경고 아님) | **지역 제한으로 한국 개인 신청 불가** — 개인은 미국/캐나다만, 조직은 미/캐/EU/영국만 |
| 상용 OV 인증서 | ~$80-300/년 | 평판 쌓여야 함 | 비용 대비 효과 낮음 |
| EV 인증서 | ~$300-500/년 + USB 토큰 | 즉시 평판 | 개인 OSS에 오버킬 |

### 판단 근거 (상세)

1. **SmartScreen의 평판 모델**: 파일 해시 평판 + 서명 발급자 평판 두 축. EV가 아니면 서명해도 초기엔 경고가 뜬다 — 즉 유료 인증서를 사도 "경고 제로"가 아니다.
2. **한국 개발자의 현실적 선택지**: Microsoft 추천 경로(Azure Artifact Signing)가 지역 제한으로 개인에게 닫혀 있음. SignPath가 사실상 유일한 무료 서명 경로.
3. **SignPath가 실제로 쓰이는 곳**: Flameshot, GitExtensions, DB Browser for SQLite 등 유명 OSS가 사용. 조건: OSI 라이선스(MIT라 OK), 공개 리포, 무료 배포, **CI 빌드 아티팩트만 서명 가능**(GitHub Actions 연동 필요), README에 "Free code signing provided by SignPath.io" 표기 의무. 신청→심사에 시간 소요.
4. **결론**: v0.1.0은 미서명 + SHA256 + GitHub Releases로 배포. CI 워크플로우를 먼저 만들어두면 나중에 SignPath 승인 시 서명 단계만 추가하면 된다.

### 신뢰를 높이는 무서명 배포 수단

- 공개 리포 + 공개 README(빌드 방법 명시) → 사용자가 직접 빌드해 대조 가능
- 릴리스 노트의 SHA256 → 다운로드 파일 변조 검증
- (미래) GitHub Actions CI 빌드 + `actions/attest-build-provenance` → "이 바이너리는 이 커밋에서 CI로 빌드됐다"는 GitHub 공인 증명. 사실상 서명 없이 할 수 있는 가장 강력한 provenance.

## 6. 검증 내역과 미검증 항목

### 완료한 검증

- `dotnet build` 0 경고 0 오류
- publish된 단일 exe 실행 확인 (프로세스 목록에서 확인)
- 셀 더블클릭 → 스티커 생성 동작 (로그 + 사용자 확인)
- 메모 패널 인라인 편집 동작
- git push / 태그 push 정상
- 재부착 워치독 로그로 스킵 동작 확인

### 아직 안 한 것 (블로그에 "한계"로 쓸 수 있는 부분)

- **클린 Windows 머신에서 실행** — 개발 PC에는 SDK가 있으므로 진짜 "아무것도 없는 PC" 테스트가 안 됨
- **SmartScreen 실제 동작** — 실제 배포 후 사용자 환경에서 경고 문구 확인 필요
- 단일 파일 모드에서 리소스(폰트/아이콘) 추출 경로 — WPF Resource는 메모리에서 읽으므로 문제없을 것으로 판단하나 실측 안 함
- 듀얼모니터에서 위치 조정이 위쪽 모니터까지 자연스럽게 드래그되는지
- 장기 실행 시 메모리/훅 안정성
- 다른 DPI 배율(125%, 150%)에서의 렌더링

## 7. 향후 개선 로드맵

### 배포/신뢰

- [ ] GitHub Actions release 워크플로우: 태그 push → CI에서 publish → 릴리스 자동 생성 + exe 첨부 + SHA256 자동 계산 + artifact attestation
- [ ] SignPath Foundation 신청 → 승인되면 CI에 서명 단계 추가
- [ ] winget-pkgs 매니페스트 제출 (`winget install jaymunsh.WinCal`)
- [ ] (장기) Microsoft Store 배포 — Store 서명으로 SmartScreen 완전 회피, 개인 개발자 계정 필요

### 기능

- [ ] 멀티데이 일정을 날짜별 반복 칩이 아니라 연속 바(span)로 렌더링
- [ ] 마우스 휠로 월 이동 (훅에서 WM_MOUSEWHEEL 처리)
- [ ] 일정 클릭 → 상세 팝오버 (지금은 읽기만)
- [ ] 셀 표시 줄 수 자동 계산 (현재 5줄 고정)
- [ ] 새 버전 알림 (GitHub Releases API 폴링)
- [ ] ICS 주기적 백그라운드 diff → 변경분만 재렌더
- [ ] 스티커 색상 선택 UI (현재는 자동 로테이션)

### 기술 부채

- [ ] 단위 테스트 없음 — CalendarService의 전개/색상 로직은 분리되어 있어 테스트 추가 여지 큼
- [ ] 버전 번호 관리 (csproj `Version` + 태그 일치)
- [ ] crash 리포팅 (현재는 debug.log 수동 활성화)

## 8. 블로그 글감 아이디어

1. **"바탕화면 아이콘 아래에 창을 넣는 법"** — WorkerW/XAML island, Win11의 두 데스크톱 구조. 국내 자료 거의 없는 주제.
2. **"클릭 통과 창인데 클릭은 받아야 할 때"** — WH_MOUSE_LL + 스크린 좌표 hit-test + UIA 아이콘 검사 조합.
3. **"Windows 11에서 더블클릭 메시지가 사라진 사건"** — CS_DBLCLKS와 XAML island, 수동 더블클릭 감지로 우회.
4. **"개인 개발자의 코드 서명 딜레마"** — 한국 개인은 Azure Trusted Signing 불가, EV는 비쌈, SignPath라는 무료 OSS 경로, 그리고 "서명해도 SmartScreen은 바로 안 사라진다"는 사실.
5. **"160MB를 72MB로"** — self-contained 단일 파일 + 압축 옵션.
6. **개발 과정 자체** — AI 페어 프로그래밍으로 스캐폴딩부터 배포까지 간 회고 포인트.

## 9. 참고 링크

- 리포지토리: https://github.com/jaymunsh/win-cal
- 릴리스: https://github.com/jaymunsh/win-cal/releases
- SignPath Foundation: https://signpath.org/
- Azure Artifact Signing 제한: Microsoft Learn "Code signing options for Windows app developers"
- Ical.Net: https://github.com/rianjs/ical.net
- Pretendard: https://github.com/orioncactus/pretendard (SIL OFL)
- DesktopCal (영감의 원천): desktopcal.com
