# WinCal

[English](README.md)

Windows **바탕화면에 박히는** 월간 캘린더 위젯. DesktopCal 스타일로 배경화면 위·바탕화면 아이콘 아래에 렌더링되며, 작업표시줄/Alt+Tab에 나타나지 않고 트레이 아이콘으로만 상주합니다.

## 기능

- 월간 달력 그리드를 바탕화면에 표시
  - Win11 최신 데스크톱(XAML island): Progman 바로 위에 z-order 고정 + 전체 클릭 통과
  - 구형 데스크톱(SHELLDLL_DefView): WorkerW 자식 창으로 부착 (자동 감지)
- Google 캘린더 **읽기 전용** 연동 (ICS 비밀 주소) — **OAuth 불필요**
  - 여러 캘린더 지원 (한 줄에 URL 하나), 캘린더별 색상 점/칩 구분
  - 종일 일정은 색상 칩, 시간 일정은 `HH:mm` + 제목으로 표시
- **스티커**: 위젯 위에 자유롭게 놓는 반투명 메모 (셀 더블클릭으로 생성)
- **우측 메모 패널**: 달력 옆 고정 메모장 — 더블클릭하면 바로 타이핑 가능
- 상단 바: 이전/다음 달, 오늘, 새로고침, Google 캘린더, 설정
- 트레이 메뉴: 새로고침, Google 캘린더, 스티커 추가, **위치 조정**(드래그/리사이즈 후 잠금), 설정, 종료
- 설정: ICS URL, 폰트(기본 Pretendard 번들), 테마(다크/라이트/미니멀/웜),
  언어(한국어/English), 표시 모니터, 새로고침 주기, 주 시작 요일,
  화면 전체/영역 지정, 크기 배율, 셀/전체 불투명도, 시작 프로그램 등록
- 반복 일정(RRULE) 확장, 시간대 변환, 며칠짜리 일정 펼침 (Ical.Net)

## 설치

**그냥 실행하면 됩니다** — [Releases](https://github.com/jaymunsh/win-cal/releases)에서
`WinCal.exe`를 받아 더블클릭. 설치 과정도, .NET 설치도 필요 없습니다
(단일 파일 self-contained, 약 72MB). 실행하면 달력이 바탕화면에 붙고, 앱은 트레이에만 상주합니다.

## 사용법

1. Google 캘린더 → 설정 → 해당 캘린더 → "캘린더 통합" → **"비밀 주소(iCal 형식)"** 복사
2. WinCal 실행 → 설정 창에 URL 붙여넣기 (공휴일 등 구독 캘린더는 "공개 주소(iCal 형식)")
3. 위치/크기 조정: 트레이 메뉴 → "위치 조정" → 드래그 후 "완료 (잠금)"
4. 일정 편집은 "Google 캘린더" 버튼으로 웹에서 수행

## 소스에서 빌드

```bash
dotnet build win-cal.sln

# 단일 파일 배포 (~72MB, .NET 없이 실행 가능)
dotnet publish WinCal -c Release -r win-x64 --self-contained `
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
  -p:EnableCompressionInSingleFile=true
```

## 구조

- `DesktopAttacher.cs` — 데스크톱 부착 (클래식 WorkerW / 모던 z-order 핀 자동 선택)
- `MouseHook.cs` — WH_MOUSE_LL로 데스크톱 클릭 감지 (창이 클릭 통과라 입력을 못 받으므로)
- `DesktopIcons.cs` — UI Automation으로 바탕화면 아이콘 영역 조회 (SysListView32 + XAML island 모두 지원)
- `CalendarService.cs` — ICS 다운로드 + Ical.Net occurrence 확장 (월별 캐시)
- `StickerStore.cs` / `SettingsStore.cs` — `%AppData%\WinCal\` JSON 저장
- `Themes.cs` — 테마 팔레트, `Loc.cs` — 한/영 문자열 테이블
- 디버깅: `settings.json`에 `"DebugLogEnabled": true` → `debug.log` 기록

## 제약 / 알려진 이슈

- ICS 비밀 주소는 Google 캐시 때문에 일정 반영이 몇 분~몇 시간 지연될 수 있음
- "모든 모니터" 모드는 달력이 화면들에 걸쳐 늘어남 (모니터별 별도 위젯은 미지원)
- 읽기 전용: 일정 편집은 Google 캘린더에서 (쓰기 지원은 OAuth 필요)

## 라이선스

- 코드: [MIT](LICENSE)
- 번들 폰트 Pretendard: SIL Open Font License 1.1 (`WinCal/Assets/Fonts/LICENSE-Pretendard.txt`)

Google과 무관한 프로젝트입니다.
