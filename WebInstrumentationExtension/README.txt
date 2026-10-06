QAFlow Web Instrumentation — Phase 4/5

Chrome / Edge (developer installation)
1. Open chrome://extensions or edge://extensions
2. Enable Developer mode.
3. Choose Load unpacked.
4. Select this WebInstrumentationExtension folder.
5. Keep QAFlow Recorder running in Hybrid Testing mode with a valid Base URL.

Privacy
- The extension sends metadata only to 127.0.0.1:47831 on the same PC.
- It never sends typed field values.
- It only emits events when the active page is inside the QAFlow configured Base URL or allowed domains.
- Recorder Only mode disables acceptance of web instrumentation events.
