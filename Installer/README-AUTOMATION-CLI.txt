QAFlow Recorder 1.0 - Hybrid Phase 9
CI / scheduled execution entry point

The application can execute a suite without opening the normal UI:

QAFlowRecorder.exe --run-suite <PROJECT_GUID> <SUITE_GUID> --environment <ENVIRONMENT_GUID> --noninteractive

Exit codes:
0  Suite passed
1  Suite failed or aborted
2  Suite needs review / contains blocked results
11 Project was not found
12 Suite was not found
13 Environment/Base URL is invalid

Scheduled executions created in Automation Workspace > Environments & Scheduling use this same entry point through Windows Task Scheduler.

Important:
- Automation remains WEB ONLY.
- Recorder Only remains independent and continues to capture Windows desktop activity.
- Manual checkpoints cannot be completed in headless mode and therefore lead to Needs Review/Failed depending on the test policy.

Browser override:
  QAFlowRecorder.exe --run-suite <PROJECT_GUID> <SUITE_GUID> --environment <ENV_GUID> --browser Chrome --noninteractive
  Supported browser values: Chrome, Edge, Firefox.

Exit code 14: governance approval required; one or more Assisted/Automated cases do not have the current version approved.

Browser headless real (Chrome/Edge/Firefox):
  QAFlowRecorder.exe --run-suite <PROJECT_GUID> <SUITE_GUID> --environment <ENV_GUID> --browser Chrome --noninteractive --browser-headless true

Notes:
- --noninteractive runs QAFlow without opening its main WPF window.
- --browser-headless controls whether Chrome/Edge/Firefox itself is visible.
- Legacy --headless remains accepted as a non-interactive compatibility flag, but browser headless is intentionally explicit.
