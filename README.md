# Civ6 Codex Companion

Civilization VI 플레이 중 현재 화면을 캡처해 Codex CLI에 분석을 맡기고, 작은 항상-위 오버레이에 다음 행동 후보와 주의점을 보여 주는 Windows 전용 미리보기 도구

## 주요 기능

- F7: 현재 Civilization VI 전경 창의 화면을 분석 대기열에 저장합니다. 대기열은 최대 6장입니다.
- F8: 대기열에 있는 화면과 현재 화면을 함께 Codex CLI에 보내 분석합니다.
- 오버레이: 화면 유형, 지금 할 일, 다음 단계, 주의점, 5턴 목표, 추가 화면 필요 여부를 표시합니다.
- 채팅: 오버레이 하단 입력창에서 질문을 보낼 수 있습니다. Enter는 전송, Shift+Enter는 줄바꿈입니다.
- 제어: 다시 분석, 취소, 새 게임, 숨기기, 종료 버튼을 제공합니다.
- 캡처 백엔드: Windows Graphics Capture를 우선 사용하고, 실패하거나 사용할 수 없는 프레임이면 GDI 캡처로 대체합니다.

## 요구사항

- Windows 10/11 x64
- Civilization VI
- .NET 8 SDK
- Visual Studio 또는 Visual Studio Build Tools
  - Desktop development with C++ workload
  - Windows 10/11 SDK
  - 설치된 Visual Studio가 제공하는 기본 MSVC x64 도구 체인
- Codex CLI 설치 및 로그인

앱 프로젝트는 빌드 중 `src/Civ6Companion.WgcNative/build-native.ps1`을 실행해 `Civ6Companion.WgcNative.dll`을 자동으로 빌드하고 앱 출력 폴더로 복사합니다.

## 빌드와 테스트

```powershell
dotnet restore Civ6CodexCompanion.sln
dotnet build Civ6CodexCompanion.sln -c Release -p:Platform=x64
dotnet test Civ6CodexCompanion.sln -c Release -p:Platform=x64 --no-restore
dotnet list Civ6CodexCompanion.sln package --vulnerable --include-transitive
dotnet run --project src/Civ6Companion.App/Civ6Companion.App.csproj -c Release -p:Platform=x64
```

빌드가 네이티브 DLL 단계에서 실패하면 Visual Studio Build Tools의 C++ 워크로드와 Windows SDK가 설치되어 있는지 먼저 확인하세요.

## 사용법

1. Civilization VI를 실행하고 게임 창을 전경에 둡니다.
2. Civ6 Codex Companion을 실행합니다.
3. F7을 눌러 참고할 화면을 저장합니다. 최대 6장까지 모입니다.
4. F8을 눌러 저장된 화면과 현재 화면을 함께 분석합니다.
5. 오버레이의 제안이 부족하면 추가 화면을 저장하거나 채팅 입력창에 질문을 보냅니다.

기본 분석 단축키는 F8입니다. F7은 화면 저장 단축키로 별도 등록됩니다. Codex CLI 경로를 직접 지정해야 하는 환경에서는 로컬 설정 파일의 `CodexPath` 값을 사용할 수 있습니다.

## 로컬 데이터와 개인정보

이 앱은 화면 캡처와 채팅을 처리하기 위해 로그인된 Codex CLI 프로세스를 실행합니다. 분석·채팅 시 Civilization VI 화면 스크린샷뿐 아니라 사용자가 입력한 질문, 최근 대화, 게임 진행 요약이 Codex 요청에 포함되어 서비스로 전송될 수 있습니다. 화면에 개인 정보나 알림, 다른 앱 창이 보이지 않도록 주의하고, 민감한 내용을 채팅에 입력하지 마세요.

로컬 데이터는 기본적으로 `%LOCALAPPDATA%\Civ6CodexCompanion` 아래에 저장됩니다.

- `Settings/settings.json`: 단축키, 오버레이 위치/너비, 스크린샷 보존 여부, Codex CLI 경로 설정
- `State`: 현재 및 보관된 게임 대화/요약 상태
- `Captures`: 임시 PNG 캡처 파일
- `CodexWork`: Codex CLI 결과 파일을 잠시 쓰는 작업 폴더

`Settings`와 `State` 데이터는 암호화되지 않은 로컬 JSON 파일입니다. 스크린샷은 기본 설정에서 작업이 끝나면 best-effort 방식으로 삭제됩니다. `CodexWork`의 결과 파일도 작업 후 best-effort 방식으로 삭제되지만, 앱이나 Codex CLI가 비정상 종료되면 남을 수 있습니다. `KeepScreenshots`를 켜면 캡처 파일이 남을 수 있습니다. 로컬 데이터를 지우려면 앱을 종료한 뒤 `%LOCALAPPDATA%\Civ6CodexCompanion` 폴더를 삭제하세요.

## 제한

- Windows 전용입니다.
- Civilization VI 창을 찾지 못하면 캡처할 수 없습니다.
- Codex CLI가 설치되어 있고 로그인되어 있어야 합니다.
- 현재 Codex CLI 호출은 `gpt-5.6-sol`과 low reasoning effort로 고정되어 있어 해당 모델을 사용할 수 있는 계정/환경이 필요합니다.
- 분석 품질은 캡처된 화면과 Codex 응답에 의존합니다.
- 게임 로고, 아트워크, 공식 데이터 파일은 포함하지 않습니다.
- 화면 캡처와 AI 분석에는 지연 시간과 사용량 비용이 생길 수 있습니다.

## 문제 해결

- "Codex CLI를 찾지 못했습니다": Codex CLI 설치와 로그인 상태를 확인하거나 `CodexPath`를 설정하세요.
- "문명 6 창을 찾지 못했습니다": Civilization VI를 실행하고 게임 창을 전경에 둔 뒤 다시 시도하세요.
- 네이티브 빌드 실패: Visual Studio Build Tools, Desktop development with C++ workload, Windows SDK, x64 도구 체인을 확인하세요.
- 분석 실패 또는 시간 초과: Codex CLI가 터미널에서 정상 실행되는지 확인하고 잠시 후 다시 시도하세요.
- 오버레이가 닫히지 않는 것처럼 보임: 닫기 버튼은 숨기기입니다. 완전 종료는 오버레이의 `종료` 버튼을 사용하세요.

## 비공식 프로젝트 고지

Civ6 Codex Companion은 비공식 팬/개인 프로젝트입니다. Civilization VI, Sid Meier's Civilization, Firaxis Games, 2K 및 관련 상표는 각 소유자의 자산입니다. 이 저장소는 Firaxis Games 또는 2K와 제휴, 승인, 후원 관계가 없습니다. 사용시 발생하는 모든 행동에 대해 책임지지 않습니다.

## 라이선스

이 프로젝트의 소스 코드는 MIT License로 배포됩니다. 자세한 내용은 [LICENSE](LICENSE)를 참고하세요.
