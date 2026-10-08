# QAFlow Recorder 1.0

QAFlow Recorder is a Windows QA evidence recorder and optional hybrid web-testing workspace. It is designed to let QA professionals work naturally while the application records desktop interactions, visual evidence, annotations, defects, and execution context, then generates structured reports.

## Product modes

### Recorder Only
The native recorder can document interactions with visible Windows applications, including browsers and desktop software. It supports session evidence, screenshots, annotations, bug documentation, session review, evidence timelines, folders/tags for session organization, privacy masking and fixed privacy zones, checkpoints, visual baselines, quality insights, coverage, risk analysis, and PDF reporting.

### Hybrid Testing · Web Automation
Automation is optional and can be disabled from **Settings**. When enabled, automation is currently limited to configured web applications. A project requires a Base URL and allowed domains. Activity outside the configured web scope can remain evidence, but is not silently converted into automation.

The hybrid workspace includes Projects, Suites, Test Cases, reusable flows/components, DOM selector intelligence, Selenium execution, assertions, test data and secure secrets, recovery/self-healing, Suite Runs, HTML reports, report delivery, environments, scheduling/CI, run queues, browser matrix execution, quality intelligence, versioning, approvals, and audit history.

## Main workflow

1. Create or name a QA session.
2. Start recording and work in the software under test.
3. Use **Annotation** for a short contextual note inside the execution flow.
4. Use **Bug** for a formal defect record with marked evidence, expected/actual result, severity, frequency, and dedicated defect reporting.
5. Finish the session and review captured steps/evidence.
6. Export the QA execution report and, when applicable, the separate bug report.

Long recordings can be split into associated multipart sessions so reports remain manageable.

## Privacy

QAFlow is local-first. Session data and evidence are stored on the local PC. Typed characters are not recorded by the desktop recorder. Privacy masking can be configured to protect sensitive visual evidence. **Privacy Zones** let the user draw fixed areas that must be hidden before screenshots are written to disk; zones can optionally be scoped to matching window titles. Secure automation secrets are stored through Windows Credential Manager rather than in project JSON files.

## Session organization and evidence review

Recorder sessions can optionally be organized with a folder/project label and comma-separated tags. History search includes the session name, folder, tags, and date. The **Evidence Timeline** provides chronological visual review of captured screenshots before opening or sharing reports.

## Reports

Depending on the workflow, QAFlow can produce:

- QA execution PDF
- Separate QA defect/bug PDF
- Offline HTML test execution reports for automated runs
- Suite and browser-matrix HTML reports
- Quality intelligence reports
- Session/evidence export packages

Annotations appear as contextual notes in the normal QA execution report. Bugs remain separate controlled defect records.

## Technology

- **.NET 10** (`net10.0-windows10.0.19041.0`)
- **WPF** for the Windows desktop UI
- **C#**
- **Windows UI Automation** and Win32 APIs for desktop interaction/context capture
- **Microsoft WebView2** for PDF/report rendering support
- **Selenium WebDriver** for web automation
- Local HTML/JSON-based reporting and persistence
- **Windows Credential Manager** for protected automation secrets
- Optional local browser instrumentation extension for DOM metadata and selector intelligence
- **Inno Setup 6** build scripts for the Windows installer

## Storage

Recorder data is stored under the current user's local application-data area in the QAFlow Recorder workspace. Automation projects, runs, governance data, evidence, and configuration remain local unless the user explicitly configures report delivery.

## Requirements

- Windows 10/11 x64
- Microsoft Edge WebView2 Runtime
- Chrome and/or Edge when using web automation
- .NET is included when using the self-contained published build

## Building

For development, open `QAFlowRecorder.csproj` with a compatible Visual Studio/.NET 10 environment.

For the packaged x64 installer, use:

`Installer\Build-Installer.cmd`

The installer build scripts publish QAFlow Recorder as a self-contained Windows application and compile the setup package with Inno Setup.

## Scope note

Recorder mode is application-agnostic and can capture web or graphical Windows workflows. Automation v1 is intentionally web-only. Desktop/mobile/API automation are not presented as active capabilities until dedicated automation drivers are implemented.


## Browser extension edition

This source package also includes `BrowserExtension`, a standalone Manifest V3 edition for Chrome and Edge. It provides browser-native QA recording features that are appropriate inside a web tab: web interaction capture, DOM-aware selectors, screenshots, flow annotations, bug records, session review, local history, and offline HTML/JSON export. Input values are never recorded.

The desktop application remains the universal Windows recorder. The browser extension does not attempt desktop capture or desktop automation. Use the **⬇ EXT** button in the desktop title bar to copy the packaged extension ZIP to the user's Downloads folder.
