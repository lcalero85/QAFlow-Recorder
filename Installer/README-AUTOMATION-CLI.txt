QAFlow Recorder 1.0 - Hybrid Phase 9
CI / scheduled execution entry point

The application can execute a suite without opening the normal UI:

QAFlowRecorder.exe --run-suite <PROJECT_GUID> <SUITE_GUID> --environment <ENVIRONMENT_GUID> --headless

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
  QAFlowRecorder.exe --run-suite <PROJECT_GUID> <SUITE_GUID> --environment <ENV_GUID> --browser Chrome --headless
  Supported browser values: Chrome, Edge.

Exit code 14: governance approval required; one or more Assisted/Automated cases do not have the current version approved.
