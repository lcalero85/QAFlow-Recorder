QAFlow Recorder 1.0 - Self-contained installer build

1. Install .NET 10 SDK and Inno Setup 6 on Windows.
2. Right-click Build-Installer.ps1 and run with PowerShell.
3. The script publishes a self-contained win-x64 build; the target PC does NOT need .NET installed.
4. The final installer is created in Installer\Output\QAFlow_Recorder_1.0_Setup_x64.exe.

The installer includes the QAFlow executable, WebView2 package assets from publish output, application icon and all runtime dependencies emitted by dotnet publish.
