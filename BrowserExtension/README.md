# QAFlow Recorder — Browser Extension

Standalone Chrome/Edge Manifest V3 edition of QAFlow Recorder for web QA sessions.

## Included browser features
- Start, pause and finish web QA sessions.
- Capture clicks, inputs (values are not stored), selects, submits and SPA navigations.
- DOM-aware element names and selectors.
- Automatic visual evidence for relevant web interactions.
- Manual screenshots.
- Flow annotations with optional screenshot.
- Formal bug records with expected/actual result, severity, frequency and screenshot.
- Review captured steps before export.
- Local session history (up to the most recent 50 sessions).
- Offline HTML QA report and JSON session record export.

## Privacy
All session data stays in `chrome.storage.local`. Text typed into web inputs is never recorded by the extension. Screenshots are stored locally in the browser profile until the session is cleared by browser storage management.

## Install for development
1. Open `chrome://extensions` or `edge://extensions`.
2. Enable Developer mode.
3. Choose **Load unpacked**.
4. Select this `BrowserExtension` folder.

The desktop QAFlow Recorder remains the universal Windows recorder. This browser edition only records activity inside browser tabs.
