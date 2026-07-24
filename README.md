# ActionFit Connectivity (`com.actionfit.connectivity`)

게임 UI와 SDK에 의존하지 않는 인터넷 연결 상태, 즉시 검사, 재시도, 복구 대기와 자동 모니터링 API를 제공합니다.

## 주요 기능

- `Unknown`, `Checking`, `Online`, `Offline` 상태를 제공하는 `ConnectivityService`
- OS reachability 조기 판정과 주입 가능한 실제 네트워크 probe
- 즉시 1회 검사와 설정된 횟수만큼 재시도하는 검사 분리
- 마지막 안정 상태가 Online일 때 자동 모니터링 실패를 일정 시간 재확인하는 disconnect grace
- grace 시작과 `Recovered`, `ConfirmedOffline`, `Cancelled` 종료 결과를 제공하는 중립 이벤트
- 연결 복구를 기다리는 `WaitForOnlineAsync`
- 자동 모니터링의 시작, 중지, Pause, Resume
- Unity용 `Application.internetReachability`, ICMP Ping, `UnityWebRequest` HEAD 어댑터
- 캐시 우회 HTTPS 요청에서 표준 `Date`, 선택적 `Age`, HTTP 코드와 왕복 시간을 반환하는 bounded observation
- UI, 광고, 분석, Firebase, 게임 초기화 코드와 분리된 인터페이스

## 기본 사용법

```csharp
using ActionFit.Connectivity;

var service = new ConnectivityService(
    new UnityReachabilityProvider(),
    new FallbackConnectivityProbe(
        new UnityPingConnectivityProbe("8.8.8.8"),
        new UnityWebRequestConnectivityProbe()),
    new ConnectivityOptions(
        "https://clients3.google.com/generate_204",
        probeTimeoutSeconds: 5f,
        checkIntervalSeconds: 10f,
        retryIntervalSeconds: 3f,
        maxRetryCount: 4,
        monitoringDisconnectGraceSeconds: 2f));

service.StateChanged += state => UnityEngine.Debug.Log(state);
service.MonitoringDisconnectGraceStarted += () => UnityEngine.Debug.Log("grace started");
service.MonitoringDisconnectGraceEnded += outcome => UnityEngine.Debug.Log(outcome);
service.StartMonitoring();
```

서버 시간처럼 응답 메타데이터가 필요한 소비자는 `IConnectivityObservationProbe.ObserveAsync`를 사용합니다. `bypassCache=true`이면 `Cache-Control`과 `Pragma` 캐시 우회 지시를 추가하며, `ConnectivityObservation.HasFreshServerDate`는 성공 응답, 유효한 `Date`, 유효한 `Age`, `Age` 없음 또는 0을 모두 요구합니다. 응답 본문과 임의의 raw header는 노출하지 않습니다.

`CheckNowAsync`는 즉시 1회만 검사합니다. `CheckWithRetryAsync`는 첫 검사 실패 후 `MaxRetryCount`만큼 추가 검사합니다. `WaitForOnlineAsync`는 자동 모니터링을 암묵적으로 시작하지 않으므로, 호출자가 먼저 `StartMonitoring`을 실행하거나 별도 검사를 발생시켜야 합니다.

자동 모니터링은 마지막 안정 상태가 `Online`일 때 첫 실패를 감지하면 `MonitoringDisconnectGraceStarted`를 한 번 발행합니다. `MonitoringDisconnectGrace` 안에 복구되면 `Recovered`로 끝나고 `Offline`을 발행하지 않습니다. 시간이 만료되면 `ConfirmedOffline` 종료 이벤트를 먼저 발행한 뒤 `Offline`으로 전환합니다. Pause, 중지, cancellation, 예외는 `Cancelled`로 끝나며 이전 안정 상태를 복원합니다. 초기 `Unknown`, 이미 `Offline`, 명시적 `CheckNowAsync`/`CheckWithRetryAsync`에는 이 grace가 적용되지 않습니다.

## 이벤트 연결 가이드

- `StateChanged`는 `Checking`을 포함한 모든 상태 전이를 알립니다. 연결 상태 표시처럼 전체 상태가 필요한 소비자는 그대로 구독하고, 팝업이나 SDK gate처럼 안정 상태만 필요한 어댑터는 `Online`과 `Offline`만 매핑하세요.
- `MonitoringDisconnectGraceStarted`에는 진행 중인 입력을 안전하게 취소하거나 scoped 입력 잠금을 획득하는 등 되돌릴 수 있는 보호 작업을 연결하세요. 이 시점에는 아직 끊김이 확정되지 않았으므로 재연결 팝업을 열지 않습니다.
- `MonitoringDisconnectGraceEnded`에는 결과와 관계없이 grace가 소유한 입력 잠금과 임시 자원을 해제하는 작업을 연결하세요.
  - `Recovered`: 잠금을 해제하고 기존 Online UI를 유지합니다. Offline 상태는 발행되지 않습니다.
  - `ConfirmedOffline`: 종료 이벤트가 `StateChanged(Offline)`보다 먼저 발행됩니다. 잠금을 먼저 해제한 뒤 Offline 처리에서 재연결 팝업을 열 수 있습니다.
  - `Cancelled`: 잠금을 해제하고 이전 안정 상태를 유지합니다. 재연결 팝업을 열지 않습니다.
- 시작 연결 대기 UI가 필요하면 프로젝트 어댑터가 `WaitForOnlineAsync` 호출 전후의 UI와 lifecycle을 소유하세요. 패키지는 대기 UI 이벤트를 별도로 발행하지 않습니다.
- 이벤트 구독과 해제는 같은 서비스·화면 lifecycle에서 짝지으세요. `StopMonitoring`은 반복 검사만 중지하며 구독자를 제거하지 않습니다.

## 프로젝트 어댑터

Cat Merge Cafe는 기존 정적 `InternetCheck` API와 `InternetCheckSO` 직렬화 설정을 호환 facade로 유지합니다. 다른 프로젝트에서는 UI 표시, SDK 대기, 앱 lifecycle 전달을 각 프로젝트 어댑터에서 연결하세요.

패키지는 HTTP HEAD probe를 위해 Unity 기본 모듈 `com.unity.modules.unitywebrequest` 1.0.0을 자동 의존합니다.

## 설치

현재 Cat Merge Cafe에서는 embedded package로 사용합니다. 수동 게시 후 다른 프로젝트의 `Packages/manifest.json`에는 다음 Git UPM 주소를 사용합니다.

```json
"com.actionfit.connectivity": "https://github.com/ActionFit-Editor/Connectivity.git#1.0.7"
```

## Agent Skill 안내

Custom Package Manager의 `Install or Refresh Agent Skills`를 실행하면 Codex와 Claude에 다음 read-only 진입점이 설치됩니다.

- `connectivity-help`: 상태, probe, retry, 모니터링과 프로젝트 어댑터 경계를 설명합니다.
- `connectivity-audit`: 실제 네트워크 요청 없이 cancellation 복구, probe 성공 기준, retry 횟수와 모니터링 계약을 소스 기준으로 점검합니다.

스킬은 네트워크 검사를 실행하거나 프로젝트 파일·데이터·패키지 게시 상태를 변경하지 않습니다.

## Unity 메뉴

- Package root: `Tools > Package > ActionFit Connectivity`
- README: `Tools > Package > ActionFit Connectivity > README`

## 테스트

Unity Test Framework의 EditMode에서 `com.actionfit.connectivity.Editor.Tests`를 실행하면 상태 전이, OS offline 조기 판정, probe 결과, 재시도, monitoring disconnect grace의 복구·만료·취소, Pause/Resume, fallback 순서와 HTTPS observation 파싱을 검증할 수 있습니다.

## 주의사항

- reachability가 가능하다는 사실만으로 Online을 확정하지 말고 실제 probe 성공을 함께 확인하세요.
- probe endpoint는 민감한 응답 본문이나 인증이 필요 없는 안정적인 HTTP/HTTPS 주소를 사용하세요.
- 상태 변경 이벤트에서 게임 UI나 SDK를 직접 참조하지 말고 프로젝트 어댑터 계층에서 연결하세요.
- `StopMonitoring`은 구독자를 제거하지 않습니다. 서비스 소유자가 구독 lifecycle을 별도로 관리해야 합니다.
- 패키지 공개 async 계약은 다른 Unity 프로젝트에서 추가 패키지 없이 사용할 수 있도록 .NET `Task`를 사용합니다. Cat Merge Cafe의 기존 facade는 이를 `UniTask`로 await합니다.
