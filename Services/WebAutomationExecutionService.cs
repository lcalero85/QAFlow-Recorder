using System.Net.Http;
using OpenQA.Selenium;
using OpenQA.Selenium.Chrome;
using OpenQA.Selenium.Edge;
using OpenQA.Selenium.Firefox;
using OpenQA.Selenium.Interactions;
using QAFlowRecorder.Models;

namespace QAFlowRecorder.Services;

public sealed class WebAutomationExecutionService
{
    private readonly AutomationRunStore _store;
    public WebAutomationExecutionService(AutomationRunStore store) => _store = store;

    public async Task<AutomationTestRun> ExecuteAsync(
        AutomationProject project,
        AutomationSuite suite,
        AutomationTestCase testCase,
        AutomationDataSet? dataSet,
        Func<AutomationTestStep, Task<bool>> manualCheckpoint,
        Action<AutomationTestRun, AutomationStepRun>? progress,
        CancellationToken cancellationToken,
        bool browserHeadless = false)
    {
        ValidateProject(project);
        var run = new AutomationTestRun
        {
            ProjectId = project.Id,
            SuiteId = suite.Id,
            TestCaseId = testCase.Id,
            ProjectName = project.Name,
            SuiteName = suite.Name,
            TestCaseKey = testCase.Key,
            TestCaseName = testCase.Name,
            TestCaseVersion = testCase.Version,
            BaseUrl = project.BaseUrl,
            Browser = project.DefaultBrowser,
            DataSetName = dataSet?.Name ?? "",
            Status = AutomationRunStatus.Running,
            StartedAt = DateTimeOffset.Now
        };
        _store.CreateRunFolder(run);
        await _store.SaveAsync(run);

        IWebDriver? driver = null;
        try
        {
            driver = CreateDriver(project.DefaultBrowser, browserHeadless);
            driver.Manage().Timeouts().ImplicitWait = TimeSpan.FromMilliseconds(250);
            if (!browserHeadless) driver.Manage().Window.Maximize();
            driver.Navigate().GoToUrl(project.BaseUrl);
            await WaitReadyAsync(driver, cancellationToken, 10000, 250);
            EnsureTechnicalDiagnostics(driver);

            var executionSequence = BuildExecutionSequence(project, testCase);
            var primaryFlowStopped = false;
            var sequenceOrder = 0;
            foreach (var item in executionSequence)
            {
                var step = item.Step;
                var lifecycleStage = item.Stage;
                if (primaryFlowStopped && !lifecycleStage.Equals("AfterTest", StringComparison.OrdinalIgnoreCase)) continue;
                cancellationToken.ThrowIfCancellationRequested();
                sequenceOrder++;
                var stepRun = new AutomationStepRun
                {
                    TestStepId = step.Id,
                    Order = sequenceOrder,
                    Action = step.Action,
                    Target = step.Target,
                    Selector = step.SelectorProfile.PrimaryDisplay,
                    Status = AutomationStepRunStatus.Running,
                    StartedAt = DateTimeOffset.Now,
                    Url = SafeUrl(driver),
                    LifecycleStage = lifecycleStage
                };
                run.Steps.Add(stepRun);
                progress?.Invoke(run, stepRun);
                await _store.SaveAsync(run);

                try
                {
                    if (!await ShouldExecuteStepAsync(driver, step, project, dataSet, cancellationToken))
                    {
                        stepRun.Status = AutomationStepRunStatus.Skipped;
                        stepRun.Message = "Skipped because the configured advanced condition evaluated to false.";
                        stepRun.Url = SafeUrl(driver);
                        continue;
                    }

                    var mustBeManual = testCase.Mode is AutomationCaseMode.Manual or AutomationCaseMode.Guided || step.IsManualCheckpoint || RequiresManualData(step);
                    if (mustBeManual)
                    {
                        run.Status = AutomationRunStatus.Paused;
                        progress?.Invoke(run, stepRun);
                        var passed = await manualCheckpoint(step);
                        stepRun.Status = passed ? AutomationStepRunStatus.ManualPassed : AutomationStepRunStatus.ManualFailed;
                        stepRun.Message = passed ? "Manual checkpoint confirmed by QA." : "Manual checkpoint failed by QA.";
                        run.Status = passed ? AutomationRunStatus.Running : AutomationRunStatus.Failed;
                        stepRun.FinishedAt = DateTimeOffset.Now;
                        stepRun.Url = SafeUrl(driver);
                        stepRun.ScreenshotPath = Capture(driver, run.RunFolder, stepRun.Order, passed ? "manual-pass" : "manual-fail");
                        progress?.Invoke(run, stepRun);
                        await _store.SaveAsync(run);
                        if (!passed && !lifecycleStage.Equals("AfterTest", StringComparison.OrdinalIgnoreCase)) primaryFlowStopped = true;
                        continue;
                    }

                    EnsureInScope(SafeUrl(driver), project);
                    Exception? executionError = null;
                    var repeatCount = Math.Clamp(step.RepeatCount, 1, 20);
                    for (var repeatIndex = 1; repeatIndex <= repeatCount; repeatIndex++)
                    {
                        var attempts = Math.Max(1, step.MaxRetries + 1);
                        executionError = null;
                        for (var attempt = 1; attempt <= attempts; attempt++)
                        {
                            try
                            {
                                ResetTechnicalDiagnostics(driver);
                                await ExecuteStepAsync(driver, step, project, dataSet, stepRun, run.StartedAt, cancellationToken);
                                EnsureTechnicalDiagnostics(driver);
                                stepRun.Url = SafeUrl(driver);
                                EnsureInScope(stepRun.Url, project);
                                stepRun.Assertions.Clear();
                                await EvaluateAssertionsAsync(driver, step, stepRun, dataSet, run.RunFolder, cancellationToken);
                                if (stepRun.Assertions.Any(a => a.Status == AutomationAssertionRunStatus.Failed))
                                    throw new InvalidOperationException("One or more assertions failed.");
                                executionError = null;
                                break;
                            }
                            catch (Exception ex)
                            {
                                executionError = ex;
                                if (attempt < attempts)
                                {
                                    stepRun.RetryCount++;
                                    stepRun.Message = $"Iteration {repeatIndex}/{repeatCount} · retry {attempt}/{step.MaxRetries}: {FriendlyError(ex)}";
                                    progress?.Invoke(run, stepRun);
                                    await Task.Delay(Math.Max(100, step.RetryDelayMs), cancellationToken);
                                }
                            }
                        }

                        if (executionError is not null) break;
                        if (repeatIndex < repeatCount && step.RepeatDelayMs > 0)
                            await Task.Delay(step.RepeatDelayMs, cancellationToken);
                    }

                    if (executionError is null && step.CloseCurrentWindowAfterStep)
                    {
                        CloseCurrentWindowAndReturn(driver, project);
                        stepRun.Url = SafeUrl(driver);
                    }

                    if (executionError is not null)
                    {
                        if (step.OnFailure == AutomationFailureAction.ManualCheckpoint)
                        {
                            run.Status = AutomationRunStatus.Paused;
                            progress?.Invoke(run, stepRun);
                            var passed = await manualCheckpoint(step);
                            stepRun.Status = passed ? AutomationStepRunStatus.ManualPassed : AutomationStepRunStatus.ManualFailed;
                            stepRun.Message = passed ? "Automatic execution could not complete; QA confirmed the recovery checkpoint." : FriendlyError(executionError);
                            run.Status = passed ? AutomationRunStatus.Running : AutomationRunStatus.Failed;
                            stepRun.ScreenshotPath = Capture(driver, run.RunFolder, stepRun.Order, passed ? "recovered-manual" : "fail");
                            if (!passed) run.FailureMessage = stepRun.Message;
                            goto StepCompleted;
                        }
                        throw executionError;
                    }

                    stepRun.Status = AutomationStepRunStatus.Passed;
                    var healingSuffix = stepRun.SelectorHealed ? " Selector self-healing was applied." : "";
                    var repeatSuffix = repeatCount > 1 ? $" {repeatCount} controlled iteration(s) completed." : "";
                    stepRun.Message = (stepRun.Assertions.Count == 0 ? "Step completed successfully." : $"Step completed. {stepRun.Assertions.Count} assertion(s) passed.") + repeatSuffix + healingSuffix;
                    stepRun.ScreenshotPath = AutomationDataResolver.UsesSecret(step.ValueExpression, dataSet) ? "" : Capture(driver, run.RunFolder, stepRun.Order, "pass");
                    StepCompleted: ;
                }
                catch (Exception ex)
                {
                    stepRun.Status = AutomationStepRunStatus.Failed;
                    stepRun.Message = FriendlyError(ex);
                    stepRun.Url = SafeUrl(driver);
                    stepRun.ScreenshotPath = Capture(driver, run.RunFolder, stepRun.Order, "fail");
                    if (step.OnFailure == AutomationFailureAction.ContinueTest)
                    {
                        run.Status = AutomationRunStatus.NeedsReview;
                        if (string.IsNullOrWhiteSpace(run.FailureMessage)) run.FailureMessage = stepRun.Message;
                    }
                    else
                    {
                        run.Status = AutomationRunStatus.Failed;
                        run.FailureMessage = stepRun.Message;
                    }
                }
                finally
                {
                    stepRun.FinishedAt = DateTimeOffset.Now;
                    progress?.Invoke(run, stepRun);
                    await _store.SaveAsync(run);
                }

                if (stepRun.Status is AutomationStepRunStatus.Failed or AutomationStepRunStatus.ManualFailed && step.OnFailure != AutomationFailureAction.ContinueTest)
                {
                    if (lifecycleStage.Equals("AfterTest", StringComparison.OrdinalIgnoreCase)) break;
                    primaryFlowStopped = true;
                }
            }

            if (run.Status is not AutomationRunStatus.Failed and not AutomationRunStatus.Aborted)
            {
                var hasFailures = run.Steps.Any(s => s.Status is AutomationStepRunStatus.Failed or AutomationStepRunStatus.ManualFailed);
                run.Status = hasFailures ? AutomationRunStatus.NeedsReview : AutomationRunStatus.Passed;
            }
        }
        catch (OperationCanceledException)
        {
            run.Status = AutomationRunStatus.Aborted;
            run.FailureMessage = "Execution cancelled by the user.";
        }
        catch (Exception ex)
        {
            run.Status = AutomationRunStatus.Failed;
            run.FailureMessage = FriendlyError(ex);
        }
        finally
        {
            try { driver?.Quit(); } catch { }
            driver?.Dispose();
            run.FinishedAt = DateTimeOffset.Now;
            run.HtmlReportPath = await AutomationHtmlReportService.GenerateAsync(run);
            await _store.SaveAsync(run);
        }

        return run;
    }

    private static List<(AutomationTestStep Step, string Stage)> BuildExecutionSequence(AutomationProject project, AutomationTestCase testCase)
    {
        var result = new List<(AutomationTestStep Step, string Stage)>();
        var before = AutomationLifecycleService.Component(project, testCase.BeforeTestComponentId);
        if (before is not null) result.AddRange(before.Steps.OrderBy(x => x.Order).Select(x => (x, "BeforeTest")));
        result.AddRange(testCase.Steps.OrderBy(x => x.Order).Select(x => (x, "Test")));
        var after = AutomationLifecycleService.Component(project, testCase.AfterTestComponentId);
        if (after is not null) result.AddRange(after.Steps.OrderBy(x => x.Order).Select(x => (x, "AfterTest")));
        return result;
    }

    private static IWebDriver CreateDriver(string browser, bool headless)
    {
        if (browser.Equals("Edge", StringComparison.OrdinalIgnoreCase))
        {
            var options = new EdgeOptions();
            options.AddArgument("--disable-notifications");
            if (headless)
            {
                options.AddArgument("--headless=new");
                options.AddArgument("--window-size=1920,1080");
                options.AddArgument("--disable-gpu");
            }
            return new EdgeDriver(options);
        }

        if (browser.Equals("Firefox", StringComparison.OrdinalIgnoreCase))
        {
            var options = new FirefoxOptions();
            if (headless)
            {
                options.AddArgument("-headless");
                options.AddArgument("--width=1920");
                options.AddArgument("--height=1080");
            }
            return new FirefoxDriver(options);
        }

        if (browser.Equals("Chrome", StringComparison.OrdinalIgnoreCase))
        {
            var options = new ChromeOptions();
            options.AddArgument("--disable-notifications");
            if (headless)
            {
                options.AddArgument("--headless=new");
                options.AddArgument("--window-size=1920,1080");
                options.AddArgument("--disable-gpu");
            }
            return new ChromeDriver(options);
        }

        throw new InvalidOperationException($"Unsupported browser '{browser}'. Supported values: Chrome, Edge, Firefox.");
    }

    private static async Task<bool> ShouldExecuteStepAsync(IWebDriver driver, AutomationTestStep step, AutomationProject project, AutomationDataSet? dataSet, CancellationToken ct)
    {
        if (step.ConditionType == AutomationStepConditionType.Always) return true;

        await ApplyBrowserContextAsync(driver, step, project, ct);
        var expression = AutomationDataResolver.Resolve(step.ConditionExpression, dataSet).Trim();
        var expected = AutomationDataResolver.Resolve(step.ConditionExpected, dataSet).Trim();

        switch (step.ConditionType)
        {
            case AutomationStepConditionType.IfUrlContains:
                return SafeUrl(driver).Contains(expression, StringComparison.OrdinalIgnoreCase);
            case AutomationStepConditionType.IfTitleContains:
                return (driver.Title ?? string.Empty).Contains(expression, StringComparison.OrdinalIgnoreCase);
            case AutomationStepConditionType.IfVariableEquals:
                return string.Equals(expression, expected, StringComparison.Ordinal);
            case AutomationStepConditionType.IfVariableNotEquals:
                return !string.Equals(expression, expected, StringComparison.Ordinal);
            case AutomationStepConditionType.IfElementExists:
            case AutomationStepConditionType.IfElementNotExists:
            {
                bool exists;
                try
                {
                    exists = !string.IsNullOrWhiteSpace(expression)
                        ? driver.FindElements(By.CssSelector(expression)).Any()
                        : (await ResolveElementAsync(driver, step, ct)).Element is not null;
                }
                catch
                {
                    exists = false;
                }
                return step.ConditionType == AutomationStepConditionType.IfElementExists ? exists : !exists;
            }
            default:
                return true;
        }
    }

    private static async Task ExecuteStepAsync(IWebDriver driver, AutomationTestStep step, AutomationProject project, AutomationDataSet? dataSet, AutomationStepRun stepRun, DateTimeOffset runStartedAt, CancellationToken ct)
    {
        await ApplyBrowserContextAsync(driver, step, project, ct);
        var action = step.Action.Trim().ToLowerInvariant();
        if (action == "navigate")
        {
            var targetUrl = string.IsNullOrWhiteSpace(step.SourceUrl) ? project.BaseUrl : step.SourceUrl;
            EnsureInScope(targetUrl, project);
            driver.Navigate().GoToUrl(targetUrl);
            await WaitReadyAsync(driver, ct, step.WaitTimeoutMs, step.WaitPollMs);
            EnsureTechnicalDiagnostics(driver);
            return;
        }

        if (action == "scroll")
        {
            if (driver is IJavaScriptExecutor js) js.ExecuteScript("window.scrollBy(0, Math.max(300, window.innerHeight * 0.65));");
            await Task.Delay(250, ct);
            return;
        }

        if (action is "validatedownload" or "validate download")
        {
            if (string.IsNullOrWhiteSpace(step.ValueExpression))
                throw new InvalidOperationException($"ValidateDownload step {step.Order} requires a file name or wildcard such as invoice.pdf or *.pdf.");
            var expectedPattern = AutomationDataResolver.Resolve(step.ValueExpression, dataSet);
            var downloaded = await WaitForDownloadedFileAsync(expectedPattern, runStartedAt, step.WaitTimeoutMs, step.WaitPollMs, ct);
            stepRun.Message = $"Downloaded file validated: {Path.GetFileName(downloaded)}";
            return;
        }

        if (action is "apirequest" or "api request")
        {
            var resolvedUrl = AutomationDataResolver.Resolve(step.ApiUrl, dataSet).Trim();
            if (string.IsNullOrWhiteSpace(resolvedUrl))
                throw new InvalidOperationException($"ApiRequest step {step.Order} requires a URL or relative endpoint.");
            if (!Uri.TryCreate(resolvedUrl, UriKind.Absolute, out var apiUri))
            {
                if (!Uri.TryCreate(project.BaseUrl, UriKind.Absolute, out var baseUri))
                    throw new InvalidOperationException("The project Base URL is not valid for a relative API endpoint.");
                apiUri = new Uri(baseUri, resolvedUrl);
            }
            EnsureInScope(apiUri.ToString(), project);

            var methodText = string.IsNullOrWhiteSpace(step.ApiMethod) ? "GET" : step.ApiMethod.Trim().ToUpperInvariant();
            var method = methodText switch
            {
                "GET" => HttpMethod.Get,
                "POST" => HttpMethod.Post,
                "PUT" => HttpMethod.Put,
                "PATCH" => HttpMethod.Patch,
                "DELETE" => HttpMethod.Delete,
                "HEAD" => HttpMethod.Head,
                _ => throw new InvalidOperationException($"Unsupported API method: {methodText}")
            };

            using var client = new HttpClient { Timeout = TimeSpan.FromMilliseconds(Math.Clamp(step.WaitTimeoutMs, 500, 120000)) };
            using var request = new HttpRequestMessage(method, apiUri);
            var body = AutomationDataResolver.Resolve(step.ApiBody ?? "", dataSet);
            if (!string.IsNullOrWhiteSpace(body) && method != HttpMethod.Get && method != HttpMethod.Head)
                request.Content = new StringContent(body, System.Text.Encoding.UTF8, string.IsNullOrWhiteSpace(step.ApiContentType) ? "application/json" : step.ApiContentType);

            foreach (var rawLine in (step.ApiHeadersExpression ?? "").Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                var resolvedLine = AutomationDataResolver.Resolve(rawLine, dataSet);
                var eq = resolvedLine.IndexOf('=');
                if (eq <= 0) continue;
                var name = resolvedLine[..eq].Trim();
                var value = resolvedLine[(eq + 1)..].Trim();
                if (string.IsNullOrWhiteSpace(name)) continue;
                if (!request.Headers.TryAddWithoutValidation(name, value) && request.Content is not null)
                    request.Content.Headers.TryAddWithoutValidation(name, value);
            }

            using var response = await client.SendAsync(request, ct);
            var expectedStatus = step.ApiExpectedStatus is >= 100 and <= 599 ? step.ApiExpectedStatus : 200;
            if ((int)response.StatusCode != expectedStatus)
                throw new InvalidOperationException($"API {methodText} {apiUri.PathAndQuery} returned {(int)response.StatusCode} {response.ReasonPhrase}; expected {expectedStatus}.");
            stepRun.Message = $"API {methodText} {apiUri.PathAndQuery} returned expected HTTP {expectedStatus}.";
            return;
        }

        if (action is "setcookie" or "set cookie")
        {
            EnsureInScope(SafeUrl(driver), project);
            var resolved = AutomationDataResolver.Resolve(step.ValueExpression, dataSet);
            var (name, value) = ParseNameValue(resolved, "SetCookie", step.Order);
            driver.Manage().Cookies.AddCookie(new OpenQA.Selenium.Cookie(name, value));
            stepRun.Message = $"Cookie set: {name}";
            return;
        }

        if (action is "deletecookie" or "delete cookie")
        {
            EnsureInScope(SafeUrl(driver), project);
            var name = AutomationDataResolver.Resolve(step.ValueExpression, dataSet).Trim();
            if (string.IsNullOrWhiteSpace(name))
                throw new InvalidOperationException($"DeleteCookie step {step.Order} requires the cookie name in the value expression.");
            driver.Manage().Cookies.DeleteCookieNamed(name);
            stepRun.Message = $"Cookie deleted: {name}";
            return;
        }

        if (action is "clearcookies" or "clear cookies")
        {
            EnsureInScope(SafeUrl(driver), project);
            driver.Manage().Cookies.DeleteAllCookies();
            stepRun.Message = "Browser cookies cleared.";
            return;
        }

        if (action is "setlocalstorage" or "set local storage")
        {
            EnsureInScope(SafeUrl(driver), project);
            var resolved = AutomationDataResolver.Resolve(step.ValueExpression, dataSet);
            var (key, value) = ParseNameValue(resolved, "SetLocalStorage", step.Order);
            ExecuteStorageScript(driver, "localStorage", "set", key, value);
            stepRun.Message = $"localStorage value set: {key}";
            return;
        }

        if (action is "removelocalstorage" or "remove local storage")
        {
            EnsureInScope(SafeUrl(driver), project);
            var key = AutomationDataResolver.Resolve(step.ValueExpression, dataSet).Trim();
            if (string.IsNullOrWhiteSpace(key))
                throw new InvalidOperationException($"RemoveLocalStorage step {step.Order} requires a key in the value expression.");
            ExecuteStorageScript(driver, "localStorage", "remove", key, null);
            stepRun.Message = $"localStorage value removed: {key}";
            return;
        }

        if (action is "clearlocalstorage" or "clear local storage")
        {
            EnsureInScope(SafeUrl(driver), project);
            ExecuteStorageScript(driver, "localStorage", "clear", null, null);
            stepRun.Message = "localStorage cleared.";
            return;
        }

        if (action is "setsessionstorage" or "set session storage")
        {
            EnsureInScope(SafeUrl(driver), project);
            var resolved = AutomationDataResolver.Resolve(step.ValueExpression, dataSet);
            var (key, value) = ParseNameValue(resolved, "SetSessionStorage", step.Order);
            ExecuteStorageScript(driver, "sessionStorage", "set", key, value);
            stepRun.Message = $"sessionStorage value set: {key}";
            return;
        }

        if (action is "removesessionstorage" or "remove session storage")
        {
            EnsureInScope(SafeUrl(driver), project);
            var key = AutomationDataResolver.Resolve(step.ValueExpression, dataSet).Trim();
            if (string.IsNullOrWhiteSpace(key))
                throw new InvalidOperationException($"RemoveSessionStorage step {step.Order} requires a key in the value expression.");
            ExecuteStorageScript(driver, "sessionStorage", "remove", key, null);
            stepRun.Message = $"sessionStorage value removed: {key}";
            return;
        }

        if (action is "clearsessionstorage" or "clear session storage")
        {
            EnsureInScope(SafeUrl(driver), project);
            ExecuteStorageScript(driver, "sessionStorage", "clear", null, null);
            stepRun.Message = "sessionStorage cleared.";
            return;
        }

        await WaitForConditionAsync(driver, step, ct);
        var resolution = await ResolveElementAsync(driver, step, ct);
        var element = resolution.Element;
        if (resolution.Healed)
        {
            stepRun.SelectorHealed = true;
            stepRun.HealingMessage = resolution.Message;
            stepRun.HealingScore = resolution.HealingScore;
            stepRun.HealingStrategy = resolution.HealingStrategy;
            stepRun.Selector = step.LearnHealedSelector ? step.SelectorProfile.PrimaryDisplay : "Recovered selector (not learned)";
        }
        if (element is null) throw new InvalidOperationException($"Target not found: {step.Target}");

        if (action is "click" or "submit")
        {
            ClickElement(driver, element, step.AllowJavaScriptClickFallback);
            await Task.Delay(300, ct);
            return;
        }

        if (action is "doubleclick" or "double click")
        {
            new Actions(driver).MoveToElement(element).DoubleClick(element).Perform();
            await Task.Delay(220, ct);
            return;
        }

        if (action is "rightclick" or "right click" or "contextclick")
        {
            new Actions(driver).MoveToElement(element).ContextClick(element).Perform();
            await Task.Delay(220, ct);
            return;
        }

        if (action == "hover")
        {
            new Actions(driver).MoveToElement(element).Perform();
            await Task.Delay(180, ct);
            return;
        }

        if (action is "clear" or "clearfield" or "clear field")
        {
            element.Clear();
            await Task.Delay(120, ct);
            return;
        }

        if (action == "check")
        {
            if (!element.Selected) ClickElement(driver, element, step.AllowJavaScriptClickFallback);
            await Task.Delay(150, ct);
            return;
        }

        if (action == "uncheck")
        {
            if (element.Selected) ClickElement(driver, element, step.AllowJavaScriptClickFallback);
            await Task.Delay(150, ct);
            return;
        }

        if (action is "selectradio" or "radio" or "select radio")
        {
            if (!element.Selected) ClickElement(driver, element, step.AllowJavaScriptClickFallback);
            await Task.Delay(150, ct);
            return;
        }

        if (action is "scrolltoelement" or "scroll to element")
        {
            if (driver is not IJavaScriptExecutor jsScroll) throw new InvalidOperationException("The browser does not support JavaScript execution.");
            jsScroll.ExecuteScript("arguments[0].scrollIntoView({behavior:'instant',block:'center',inline:'nearest'});", element);
            await Task.Delay(180, ct);
            return;
        }

        if (action is "keypress" or "key press" or "key")
        {
            if (string.IsNullOrWhiteSpace(step.ValueExpression))
                throw new InvalidOperationException($"KeyPress step {step.Order} requires a key expression such as ENTER, ESCAPE, TAB or CTRL+A.");
            var keyExpression = AutomationDataResolver.Resolve(step.ValueExpression, dataSet);
            element.SendKeys(ToKeySequence(keyExpression));
            await Task.Delay(160, ct);
            return;
        }

        if (action is "uploadfile" or "upload file")
        {
            if (string.IsNullOrWhiteSpace(step.ValueExpression))
                throw new InvalidOperationException($"UploadFile step {step.Order} requires a local file path in the value expression.");
            var path = ResolveUploadPath(AutomationDataResolver.Resolve(step.ValueExpression, dataSet));
            if (!File.Exists(path)) throw new FileNotFoundException($"Upload file was not found: {path}", path);
            element.SendKeys(path);
            await Task.Delay(220, ct);
            return;
        }

        if (action == "input")
        {
            if (string.IsNullOrWhiteSpace(step.ValueExpression))
                throw new InvalidOperationException($"Input step {step.Order} requires a value expression such as ${{username}}.");
            var value = AutomationDataResolver.Resolve(step.ValueExpression, dataSet);
            element.Clear();
            element.SendKeys(value);
            await Task.Delay(180, ct);
            return;
        }

        if (action == "select")
        {
            if (string.IsNullOrWhiteSpace(step.ValueExpression))
                throw new InvalidOperationException($"Select step {step.Order} requires a value expression.");
            var value = AutomationDataResolver.Resolve(step.ValueExpression, dataSet);
            if (driver is not IJavaScriptExecutor js) throw new InvalidOperationException("The browser does not support JavaScript execution.");
            var changed = js.ExecuteScript(@"var el=arguments[0],v=arguments[1]; if(!el||!el.options)return false; var hit=false; for(var i=0;i<el.options.length;i++){var o=el.options[i]; if(o.value===v || (o.text||'').trim()===v){el.selectedIndex=i; hit=true; break;}} if(hit){el.dispatchEvent(new Event('input',{bubbles:true}));el.dispatchEvent(new Event('change',{bubbles:true}));} return hit;", element, value);
            if (changed is not bool ok || !ok) throw new InvalidOperationException($"Option '{value}' was not found for step {step.Order}.");
            await Task.Delay(180, ct);
            return;
        }

        throw new InvalidOperationException($"Action '{step.Action}' is not supported by the current web execution engine.");
    }


    private static (string Name, string Value) ParseNameValue(string expression, string actionName, int stepOrder)
    {
        var raw = (expression ?? string.Empty).Trim();
        var separator = raw.IndexOf('=');
        if (separator <= 0)
            throw new InvalidOperationException($"{actionName} step {stepOrder} requires value expression in key=value format.");
        var name = raw[..separator].Trim();
        var value = raw[(separator + 1)..];
        if (string.IsNullOrWhiteSpace(name))
            throw new InvalidOperationException($"{actionName} step {stepOrder} requires a non-empty key/name before '='.");
        return (name, value);
    }

    private static void ExecuteStorageScript(IWebDriver driver, string storageName, string operation, string? key, string? value)
    {
        if (driver is not IJavaScriptExecutor js)
            throw new InvalidOperationException("The browser does not support JavaScript execution required for Web Storage operations.");

        if (storageName is not ("localStorage" or "sessionStorage"))
            throw new InvalidOperationException("Unsupported Web Storage target.");

        switch (operation)
        {
            case "set":
                js.ExecuteScript($"window.{storageName}.setItem(arguments[0], arguments[1]);", key ?? string.Empty, value ?? string.Empty);
                break;
            case "remove":
                js.ExecuteScript($"window.{storageName}.removeItem(arguments[0]);", key ?? string.Empty);
                break;
            case "clear":
                js.ExecuteScript($"window.{storageName}.clear();");
                break;
            default:
                throw new InvalidOperationException("Unsupported Web Storage operation.");
        }
    }

    private static void ClickElement(IWebDriver driver, IWebElement element, bool allowJavaScriptFallback)
    {
        try
        {
            element.Click();
            return;
        }
        catch (Exception ex) when (allowJavaScriptFallback && ex is WebDriverException)
        {
            if (driver is not IJavaScriptExecutor js) throw;
            js.ExecuteScript("arguments[0].click();", element);
        }
    }

    private static string ToKeySequence(string expression)
    {
        var raw = (expression ?? "").Trim();
        if (string.IsNullOrWhiteSpace(raw)) return raw;
        var parts = raw.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 1) return MapKey(parts[0]);

        var mapped = parts.Select(MapKey).ToArray();
        // Selenium 4.50 no longer exposes Keys.Chord in the .NET API.
        // A chord is represented by concatenating the WebDriver key tokens and
        // terminating with Keys.Null so modifier keys are released afterwards.
        return string.Concat(mapped) + Keys.Null;
    }

    private static string MapKey(string token)
    {
        return token.Trim().ToUpperInvariant() switch
        {
            "ENTER" or "RETURN" => Keys.Enter,
            "ESC" or "ESCAPE" => Keys.Escape,
            "TAB" => Keys.Tab,
            "SPACE" => Keys.Space,
            "BACKSPACE" => Keys.Backspace,
            "DELETE" or "DEL" => Keys.Delete,
            "HOME" => Keys.Home,
            "END" => Keys.End,
            "PAGEUP" => Keys.PageUp,
            "PAGEDOWN" => Keys.PageDown,
            "UP" or "ARROWUP" => Keys.ArrowUp,
            "DOWN" or "ARROWDOWN" => Keys.ArrowDown,
            "LEFT" or "ARROWLEFT" => Keys.ArrowLeft,
            "RIGHT" or "ARROWRIGHT" => Keys.ArrowRight,
            "CTRL" or "CONTROL" => Keys.Control,
            "ALT" => Keys.Alt,
            "SHIFT" => Keys.Shift,
            "META" or "WIN" or "WINDOWS" => Keys.Meta,
            _ => token
        };
    }

    private static async Task ApplyBrowserContextAsync(IWebDriver driver, AutomationTestStep step, AutomationProject project, CancellationToken ct)
    {
        var handles = driver.WindowHandles.ToList();
        if (handles.Count == 0) throw new InvalidOperationException("The browser does not expose an active window.");

        string? targetHandle = null;
        switch (step.WindowTarget)
        {
            case AutomationWindowTarget.Newest:
            case AutomationWindowTarget.Last:
                targetHandle = handles[^1];
                break;
            case AutomationWindowTarget.First:
                targetHandle = handles[0];
                break;
            case AutomationWindowTarget.TitleContains:
                if (string.IsNullOrWhiteSpace(step.WindowMatch))
                    throw new InvalidOperationException("Window target TitleContains requires a title fragment.");
                var current = driver.CurrentWindowHandle;
                foreach (var handle in handles)
                {
                    driver.SwitchTo().Window(handle);
                    if ((driver.Title ?? "").Contains(step.WindowMatch, StringComparison.OrdinalIgnoreCase))
                    {
                        targetHandle = handle;
                        break;
                    }
                }
                if (targetHandle is null)
                {
                    try { driver.SwitchTo().Window(current); } catch { }
                    throw new InvalidOperationException($"No browser tab/window title contains '{step.WindowMatch}'.");
                }
                break;
            case AutomationWindowTarget.Current:
            default:
                targetHandle = driver.CurrentWindowHandle;
                break;
        }

        driver.SwitchTo().Window(targetHandle);
        EnsureInScope(SafeUrl(driver), project);
        driver.SwitchTo().DefaultContent();

        switch (step.FrameTarget)
        {
            case AutomationFrameTarget.DefaultContent:
                break;
            case AutomationFrameTarget.CssSelector:
                if (string.IsNullOrWhiteSpace(step.FrameValue)) throw new InvalidOperationException("Frame CSS selector is required.");
                driver.SwitchTo().Frame(driver.FindElement(By.CssSelector(step.FrameValue)));
                break;
            case AutomationFrameTarget.NameOrId:
                if (string.IsNullOrWhiteSpace(step.FrameValue)) throw new InvalidOperationException("Frame name or id is required.");
                driver.SwitchTo().Frame(step.FrameValue);
                break;
            case AutomationFrameTarget.Index:
                if (!int.TryParse(step.FrameValue, out var index) || index < 0) throw new InvalidOperationException("Frame index must be a zero-based integer.");
                driver.SwitchTo().Frame(index);
                break;
        }

        await Task.Delay(80, ct);
    }

    private static void CloseCurrentWindowAndReturn(IWebDriver driver, AutomationProject project)
    {
        var current = driver.CurrentWindowHandle;
        var handlesBefore = driver.WindowHandles.ToList();
        if (handlesBefore.Count <= 1)
            throw new InvalidOperationException("QAFlow will not close the last remaining browser window.");

        driver.Close();
        var remaining = driver.WindowHandles.Where(h => !string.Equals(h, current, StringComparison.Ordinal)).ToList();
        if (remaining.Count == 0) throw new InvalidOperationException("No browser window remains after closing the current tab/window.");
        driver.SwitchTo().Window(remaining[^1]);
        driver.SwitchTo().DefaultContent();
        EnsureInScope(SafeUrl(driver), project);
    }

    private static string ResolveUploadPath(string rawPath)
    {
        var expanded = Environment.ExpandEnvironmentVariables((rawPath ?? "").Trim().Trim('"'));
        if (expanded.StartsWith("~/", StringComparison.Ordinal) || expanded.StartsWith("~\\", StringComparison.Ordinal))
            expanded = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), expanded[2..]);
        if (!Path.IsPathRooted(expanded)) expanded = Path.GetFullPath(expanded);
        return expanded;
    }

    private static async Task<string> WaitForDownloadedFileAsync(string pattern, DateTimeOffset runStartedAt, int timeoutMs, int pollMs, CancellationToken ct)
    {
        var downloads = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
        if (!Directory.Exists(downloads)) throw new DirectoryNotFoundException($"Downloads folder was not found: {downloads}");
        var effectivePattern = string.IsNullOrWhiteSpace(pattern) ? "*" : Path.GetFileName(pattern.Trim().Trim('"'));
        if (string.IsNullOrWhiteSpace(effectivePattern)) effectivePattern = "*";
        var deadline = DateTimeOffset.UtcNow.AddMilliseconds(Math.Clamp(timeoutMs, 500, 120000));
        var delay = Math.Clamp(pollMs, 100, 5000);
        while (DateTimeOffset.UtcNow <= deadline)
        {
            ct.ThrowIfCancellationRequested();
            var candidates = Directory.EnumerateFiles(downloads, effectivePattern, SearchOption.TopDirectoryOnly)
                .Where(f => !f.EndsWith(".crdownload", StringComparison.OrdinalIgnoreCase)
                         && !f.EndsWith(".part", StringComparison.OrdinalIgnoreCase)
                         && !f.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase))
                .Select(f => new FileInfo(f))
                .Where(fi => fi.Exists && fi.Length > 0 && fi.LastWriteTimeUtc >= runStartedAt.UtcDateTime.AddSeconds(-2))
                .OrderByDescending(fi => fi.LastWriteTimeUtc)
                .ToList();
            foreach (var file in candidates)
            {
                var firstLength = file.Length;
                await Task.Delay(Math.Min(delay, 500), ct);
                file.Refresh();
                if (file.Exists && file.Length == firstLength && file.Length > 0) return file.FullName;
            }
            await Task.Delay(delay, ct);
        }
        throw new TimeoutException($"No completed download matching '{effectivePattern}' was found in Downloads within {Math.Clamp(timeoutMs, 500, 120000) / 1000.0:0.#} seconds.");
    }

    private static bool RequiresManualData(AutomationTestStep step) =>
        (step.Action.Equals("Input", StringComparison.OrdinalIgnoreCase) || step.Action.Equals("Select", StringComparison.OrdinalIgnoreCase) || step.Action.Equals("UploadFile", StringComparison.OrdinalIgnoreCase) || step.Action.Equals("ValidateDownload", StringComparison.OrdinalIgnoreCase))
        && string.IsNullOrWhiteSpace(step.ValueExpression);

    private static async Task EvaluateAssertionsAsync(IWebDriver driver, AutomationTestStep step, AutomationStepRun stepRun, AutomationDataSet? dataSet, string runFolder, CancellationToken ct)
    {
        foreach (var assertion in step.Assertions)
        {
            var result = new AutomationAssertionRun { Type = assertion.Type, Expected = assertion.Expected };
            try
            {
                var expected = AutomationDataResolver.Resolve(assertion.Expected, dataSet);
                switch (assertion.Type)
                {
                    case AutomationAssertionType.UrlContains:
                        if (!SafeUrl(driver).Contains(expected, StringComparison.OrdinalIgnoreCase))
                            throw new InvalidOperationException($"URL does not contain the expected value.");
                        break;
                    case AutomationAssertionType.TitleContains:
                        if (!(driver.Title ?? "").Contains(expected, StringComparison.OrdinalIgnoreCase))
                            throw new InvalidOperationException($"Page title does not contain the expected value.");
                        break;
                    case AutomationAssertionType.ElementTextContains:
                        var textContainsElement = (await ResolveElementAsync(driver, step, ct)).Element;
                        if (textContainsElement is null || !(textContainsElement.Text ?? "").Contains(expected, StringComparison.OrdinalIgnoreCase))
                            throw new InvalidOperationException("Target element text does not contain the expected value.");
                        break;
                    case AutomationAssertionType.ElementTextEquals:
                        var textEqualsElement = (await ResolveElementAsync(driver, step, ct)).Element;
                        if (textEqualsElement is null || !string.Equals((textEqualsElement.Text ?? "").Trim(), expected.Trim(), StringComparison.OrdinalIgnoreCase))
                            throw new InvalidOperationException("Target element text does not equal the expected value.");
                        break;
                    case AutomationAssertionType.ElementAttributeEquals:
                        var attributeElement = (await ResolveElementAsync(driver, step, ct)).Element;
                        if (attributeElement is null) throw new InvalidOperationException("Target element was not found.");
                        var separator = expected.IndexOf('=');
                        if (separator <= 0) throw new InvalidOperationException("ElementAttributeEquals expects 'attribute=value'.");
                        var attributeName = expected[..separator].Trim();
                        var attributeExpected = expected[(separator + 1)..].Trim();
                        var actualAttribute = attributeElement.GetAttribute(attributeName) ?? "";
                        if (!string.Equals(actualAttribute, attributeExpected, StringComparison.OrdinalIgnoreCase))
                            throw new InvalidOperationException($"Attribute '{attributeName}' does not equal the expected value.");
                        break;
                    case AutomationAssertionType.TextContains:
                        var bodyText = driver.FindElement(By.TagName("body")).Text ?? "";
                        if (!bodyText.Contains(expected, StringComparison.OrdinalIgnoreCase))
                            throw new InvalidOperationException($"Page text does not contain the expected value.");
                        break;
                    case AutomationAssertionType.ElementVisible:
                        var visible = (await ResolveElementAsync(driver, step, ct)).Element;
                        if (visible is null || !visible.Displayed) throw new InvalidOperationException("Target element is not visible.");
                        break;
                    case AutomationAssertionType.ElementEnabled:
                        var enabled = (await ResolveElementAsync(driver, step, ct)).Element;
                        if (enabled is null || !enabled.Enabled) throw new InvalidOperationException("Target element is not enabled.");
                        break;
                    case AutomationAssertionType.ElementNotVisible:
                        if (FindNow(driver, step)?.Displayed == true) throw new InvalidOperationException("Target element is still visible.");
                        break;
                    case AutomationAssertionType.NoConsoleErrors:
                        var consoleErrors = ReadTechnicalErrors(driver, "console");
                        if (consoleErrors.Count > 0)
                            throw new InvalidOperationException($"Browser console contains {consoleErrors.Count} error(s): {string.Join(" | ", consoleErrors.Take(3))}");
                        result.Message = "No browser console errors were detected for this step.";
                        break;
                    case AutomationAssertionType.NoNetworkErrors:
                        var networkErrors = ReadTechnicalErrors(driver, "network");
                        if (networkErrors.Count > 0)
                            throw new InvalidOperationException($"Browser/network diagnostics contain {networkErrors.Count} failed request(s): {string.Join(" | ", networkErrors.Take(3))}");
                        result.Message = "No failed network requests or resource-load errors were detected for this step.";
                        break;
                    case AutomationAssertionType.MaxPageLoadMs:
                        if (!double.TryParse(expected, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var maxLoadMs) || maxLoadMs <= 0)
                            throw new InvalidOperationException("MaxPageLoadMs expects a positive number of milliseconds, for example 3000.");
                        var loadMs = ReadNavigationDurationMs(driver);
                        if (loadMs < 0) throw new InvalidOperationException("Navigation timing is not available for the current page.");
                        if (loadMs > maxLoadMs)
                            throw new InvalidOperationException($"Page load duration {loadMs:0} ms exceeds the allowed {maxLoadMs:0} ms.");
                        result.Message = $"Page load duration {loadMs:0} ms is within the allowed {maxLoadMs:0} ms.";
                        break;
                    case AutomationAssertionType.MaxDomContentLoadedMs:
                        if (!double.TryParse(expected, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var maxDomMs) || maxDomMs <= 0)
                            throw new InvalidOperationException("MaxDomContentLoadedMs expects a positive number of milliseconds, for example 2000.");
                        var domMs = ReadDomContentLoadedMs(driver);
                        if (domMs < 0) throw new InvalidOperationException("DOM Content Loaded timing is not available for the current page.");
                        if (domMs > maxDomMs)
                            throw new InvalidOperationException($"DOM Content Loaded {domMs:0} ms exceeds the allowed {maxDomMs:0} ms.");
                        result.Message = $"DOM Content Loaded {domMs:0} ms is within the allowed {maxDomMs:0} ms.";
                        break;
                    case AutomationAssertionType.MaxFirstContentfulPaintMs:
                        if (!double.TryParse(expected, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var maxFcpMs) || maxFcpMs <= 0)
                            throw new InvalidOperationException("MaxFirstContentfulPaintMs expects a positive number of milliseconds, for example 1800.");
                        var fcpMs = ReadFirstContentfulPaintMs(driver);
                        if (fcpMs < 0) throw new InvalidOperationException("First Contentful Paint timing is not available for the current page.");
                        if (fcpMs > maxFcpMs)
                            throw new InvalidOperationException($"First Contentful Paint {fcpMs:0} ms exceeds the allowed {maxFcpMs:0} ms.");
                        result.Message = $"First Contentful Paint {fcpMs:0} ms is within the allowed {maxFcpMs:0} ms.";
                        break;
                    case AutomationAssertionType.MaxResourceDurationMs:
                        if (!double.TryParse(expected, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var maxResourceMs) || maxResourceMs <= 0)
                            throw new InvalidOperationException("MaxResourceDurationMs expects a positive number of milliseconds, for example 1500.");
                        var resource = ReadSlowestResource(driver);
                        if (resource.DurationMs < 0) throw new InvalidOperationException("Resource timing is not available for the current page.");
                        if (resource.DurationMs > maxResourceMs)
                            throw new InvalidOperationException($"Slowest resource took {resource.DurationMs:0} ms and exceeds the allowed {maxResourceMs:0} ms: {resource.Name}");
                        result.Message = resource.DurationMs <= 0
                            ? "No measurable resource duration exceeded the configured budget."
                            : $"Slowest resource took {resource.DurationMs:0} ms, within the allowed {maxResourceMs:0} ms: {resource.Name}";
                        break;
                    case AutomationAssertionType.VisualBaselineMatch:
                        var threshold = 95.0;
                        if (!string.IsNullOrWhiteSpace(expected) && (!double.TryParse(expected, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out threshold) || threshold < 0 || threshold > 100))
                            throw new InvalidOperationException("VisualBaselineMatch expects a similarity threshold from 0 to 100, for example 95.");
                        var currentVisual = Capture(driver, runFolder, stepRun.Order, "visual-assertion");
                        if (string.IsNullOrWhiteSpace(currentVisual)) throw new InvalidOperationException("QAFlow could not capture the current browser image for visual comparison.");
                        var similarity = AutomationVisualBaselineService.Compare(step.Id, currentVisual);
                        if (similarity + 0.0001 < threshold)
                            throw new InvalidOperationException($"Visual similarity {similarity:0.0}% is below the required {threshold:0.0}%.");
                        result.Message = $"Visual similarity {similarity:0.0}% meets the required {threshold:0.0}% baseline threshold.";
                        break;
                }
                result.Status = AutomationAssertionRunStatus.Passed;
                if (string.IsNullOrWhiteSpace(result.Message)) result.Message = "Assertion passed.";
            }
            catch (Exception ex)
            {
                result.Status = AutomationAssertionRunStatus.Failed;
                result.Message = ex.GetBaseException().Message;
            }
            stepRun.Assertions.Add(result);
        }
    }

    private static void EnsureTechnicalDiagnostics(IWebDriver driver)
    {
        if (driver is not IJavaScriptExecutor js) return;
        try
        {
            js.ExecuteScript(@"
                (function(){
                  if (window.__qaflowDiagnostics && window.__qaflowDiagnostics.version === 1) return;
                  const state = window.__qaflowDiagnostics = { version:1, console:[], network:[] };
                  const push = (bucket, value) => {
                    try {
                      const text = String(value == null ? '' : value).replace(/\s+/g,' ').trim().slice(0,800);
                      if (text && state[bucket].indexOf(text) < 0) state[bucket].push(text);
                    } catch (_) {}
                  };
                  try {
                    const originalError = console.error.bind(console);
                    console.error = function(){
                      try { push('console', Array.from(arguments).map(x => x && x.stack ? x.stack : String(x)).join(' ')); } catch (_) {}
                      return originalError.apply(console, arguments);
                    };
                  } catch (_) {}
                  window.addEventListener('error', function(e){
                    try {
                      const target = e.target;
                      if (target && target !== window && target.tagName) {
                        push('network', 'Resource failed: ' + target.tagName + ' ' + (target.src || target.href || ''));
                      } else {
                        push('console', (e.message || 'Window error') + (e.filename ? ' @ ' + e.filename + ':' + e.lineno : ''));
                      }
                    } catch (_) {}
                  }, true);
                  window.addEventListener('unhandledrejection', function(e){
                    try { push('console', 'Unhandled promise rejection: ' + (e.reason && e.reason.stack ? e.reason.stack : String(e.reason))); } catch (_) {}
                  });
                  try {
                    const originalFetch = window.fetch;
                    if (originalFetch) window.fetch = async function(){
                      try {
                        const response = await originalFetch.apply(this, arguments);
                        if (response && response.status >= 400) push('network', 'HTTP ' + response.status + ' ' + response.url);
                        return response;
                      } catch (err) {
                        push('network', 'Fetch failed: ' + (err && err.message ? err.message : String(err)));
                        throw err;
                      }
                    };
                  } catch (_) {}
                  try {
                    const open = XMLHttpRequest.prototype.open;
                    const send = XMLHttpRequest.prototype.send;
                    XMLHttpRequest.prototype.open = function(method, url){ this.__qaflowUrl = url; return open.apply(this, arguments); };
                    XMLHttpRequest.prototype.send = function(){
                      this.addEventListener('loadend', function(){
                        try { if (this.status >= 400 || this.status === 0) push('network', 'XHR ' + this.status + ' ' + (this.responseURL || this.__qaflowUrl || '')); } catch (_) {}
                      });
                      return send.apply(this, arguments);
                    };
                  } catch (_) {}
                  try {
                    const entries = performance.getEntriesByType('resource') || [];
                    for (const e of entries) {
                      if (typeof e.responseStatus === 'number' && e.responseStatus >= 400) push('network', 'HTTP ' + e.responseStatus + ' ' + e.name);
                    }
                  } catch (_) {}
                })();
            ");
        }
        catch { }
    }

    private static void ResetTechnicalDiagnostics(IWebDriver driver)
    {
        EnsureTechnicalDiagnostics(driver);
        if (driver is not IJavaScriptExecutor js) return;
        try
        {
            js.ExecuteScript("if(window.__qaflowDiagnostics){window.__qaflowDiagnostics.console=[];window.__qaflowDiagnostics.network=[];}");
        }
        catch { }
    }

    private static List<string> ReadTechnicalErrors(IWebDriver driver, string bucket)
    {
        EnsureTechnicalDiagnostics(driver);
        if (driver is not IJavaScriptExecutor js) return [];
        try
        {
            var raw = js.ExecuteScript(@"
                if(!window.__qaflowDiagnostics) return [];
                try {
                  const entries = performance.getEntriesByType('resource') || [];
                  for (const e of entries) {
                    if (typeof e.responseStatus === 'number' && e.responseStatus >= 400) {
                      const msg = 'HTTP ' + e.responseStatus + ' ' + e.name;
                      if (window.__qaflowDiagnostics.network.indexOf(msg) < 0) window.__qaflowDiagnostics.network.push(msg);
                    }
                  }
                } catch (_) {}
                return (window.__qaflowDiagnostics[arguments[0]] || []).slice(0,50);
            ", bucket);
            if (raw is System.Collections.IEnumerable items)
            {
                var list = new List<string>();
                foreach (var item in items)
                {
                    var text = item?.ToString();
                    if (!string.IsNullOrWhiteSpace(text)) list.Add(text);
                }
                return list;
            }
        }
        catch { }
        return [];
    }

    private static double ReadNavigationDurationMs(IWebDriver driver)
    {
        if (driver is not IJavaScriptExecutor js) return -1;
        try
        {
            var raw = js.ExecuteScript(@"
                const nav = performance.getEntriesByType('navigation');
                if (nav && nav.length) return Number(nav[0].duration || nav[0].loadEventEnd || 0);
                if (performance.timing) {
                  const t = performance.timing;
                  if (t.loadEventEnd > 0 && t.navigationStart > 0) return Number(t.loadEventEnd - t.navigationStart);
                }
                return -1;
            ");
            return Convert.ToDouble(raw, System.Globalization.CultureInfo.InvariantCulture);
        }
        catch { return -1; }
    }

    private static double ReadDomContentLoadedMs(IWebDriver driver)
    {
        if (driver is not IJavaScriptExecutor js) return -1;
        try
        {
            var raw = js.ExecuteScript(@"
                const nav = performance.getEntriesByType('navigation');
                if (nav && nav.length) return Number(nav[0].domContentLoadedEventEnd || -1);
                if (performance.timing) {
                  const t = performance.timing;
                  if (t.domContentLoadedEventEnd > 0 && t.navigationStart > 0) return Number(t.domContentLoadedEventEnd - t.navigationStart);
                }
                return -1;
            ");
            return Convert.ToDouble(raw, System.Globalization.CultureInfo.InvariantCulture);
        }
        catch { return -1; }
    }

    private static double ReadFirstContentfulPaintMs(IWebDriver driver)
    {
        if (driver is not IJavaScriptExecutor js) return -1;
        try
        {
            var raw = js.ExecuteScript(@"
                const paints = performance.getEntriesByType('paint') || [];
                const fcp = paints.find(x => x.name === 'first-contentful-paint');
                return fcp ? Number(fcp.startTime || -1) : -1;
            ");
            return Convert.ToDouble(raw, System.Globalization.CultureInfo.InvariantCulture);
        }
        catch { return -1; }
    }

    private static (double DurationMs, string Name) ReadSlowestResource(IWebDriver driver)
    {
        if (driver is not IJavaScriptExecutor js) return (-1, "");
        try
        {
            var raw = js.ExecuteScript(@"
                const entries = (performance.getEntriesByType('resource') || [])
                  .filter(x => Number.isFinite(Number(x.duration)))
                  .sort((a,b) => Number(b.duration) - Number(a.duration));
                if (!entries.length) return null;
                return [Number(entries[0].duration || 0), String(entries[0].name || '')];
            ");
            if (raw is System.Collections.IEnumerable items)
            {
                var values = new List<object?>();
                foreach (var item in items) values.Add(item);
                if (values.Count >= 2)
                {
                    var duration = Convert.ToDouble(values[0], System.Globalization.CultureInfo.InvariantCulture);
                    var name = values[1]?.ToString() ?? "";
                    if (name.Length > 180) name = name[..180] + "…";
                    return (duration, name);
                }
            }
        }
        catch { }
        return (-1, "");
    }

    private static IWebElement? FindNow(IWebDriver driver, AutomationTestStep step)
    {
        if (step.ShadowDomMode == AutomationShadowDomMode.OpenShadowPath)
            return FindShadowDomElement(driver, step.ShadowCssPath);
        return FindNow(driver, step.SelectorProfile);
    }

    private static IWebElement? FindShadowDomElement(IWebDriver driver, string cssPath)
    {
        if (driver is not IJavaScriptExecutor js || string.IsNullOrWhiteSpace(cssPath)) return null;
        try
        {
            const string script = @"
                const path = String(arguments[0] || '').split('>>>').map(x => x.trim()).filter(Boolean);
                if (!path.length) return null;
                let root = document;
                let element = null;
                for (let i = 0; i < path.length; i++) {
                    element = root.querySelector(path[i]);
                    if (!element) return null;
                    if (i < path.length - 1) {
                        if (!element.shadowRoot) return null;
                        root = element.shadowRoot;
                    }
                }
                return element;";
            return js.ExecuteScript(script, cssPath) as IWebElement;
        }
        catch (WebDriverException) { return null; }
        catch (InvalidOperationException) { return null; }
    }

    private static IWebElement? FindNow(IWebDriver driver, WebSelectorProfile profile)
    {
        var candidates = profile.Candidates.OrderByDescending(x => x.Confidence).ToList();
        if (candidates.Count == 0 && !string.IsNullOrWhiteSpace(profile.PrimaryValue))
            candidates.Add(new WebSelectorCandidate { Strategy = profile.PrimaryStrategy, Value = profile.PrimaryValue, Confidence = profile.Confidence });
        foreach (var candidate in candidates)
        {
            try
            {
                var by = ToBy(candidate);
                if (by is null) continue;
                var found = driver.FindElements(by).FirstOrDefault();
                if (found is not null) return found;
            }
            catch { }
        }
        return null;
    }

    private sealed record ElementResolution(IWebElement? Element, bool Healed, string Message, int HealingScore = 0, string HealingStrategy = "");

    private static async Task<ElementResolution> ResolveElementAsync(IWebDriver driver, AutomationTestStep step, CancellationToken ct)
    {
        if (step.ShadowDomMode == AutomationShadowDomMode.OpenShadowPath)
        {
            if (string.IsNullOrWhiteSpace(step.ShadowCssPath))
                throw new InvalidOperationException($"Shadow DOM step {step.Order} requires a CSS path using >>> between shadow hosts.");
            var shadowDeadline = DateTime.UtcNow.AddMilliseconds(Math.Clamp(step.WaitTimeoutMs, 500, 120000));
            while (DateTime.UtcNow < shadowDeadline)
            {
                ct.ThrowIfCancellationRequested();
                var shadowElement = FindShadowDomElement(driver, step.ShadowCssPath);
                if (shadowElement is not null) return new ElementResolution(shadowElement, false, "Shadow DOM target resolved.");
                await Task.Delay(Math.Clamp(step.WaitPollMs, 50, 5000), ct);
            }
            throw new TimeoutException($"Shadow DOM target was not found within the configured timeout: {step.ShadowCssPath}");
        }

        var profile = step.SelectorProfile;
        var candidates = profile.Candidates.OrderByDescending(x => x.Confidence).ToList();
        if (candidates.Count == 0 && !string.IsNullOrWhiteSpace(profile.PrimaryValue))
            candidates.Add(new WebSelectorCandidate { Strategy = profile.PrimaryStrategy, Value = profile.PrimaryValue, Confidence = profile.Confidence });

        var deadline = DateTime.UtcNow.AddMilliseconds(Math.Clamp(step.WaitTimeoutMs, 500, 120000));
        Exception? last = null;
        while (DateTime.UtcNow < deadline)
        {
            ct.ThrowIfCancellationRequested();
            foreach (var c in candidates)
            {
                try
                {
                    var by = ToBy(c);
                    if (by is null) continue;
                    var elements = driver.FindElements(by);
                    var el = elements.FirstOrDefault(x => x.Displayed && x.Enabled) ?? elements.FirstOrDefault(x => x.Displayed);
                    if (el is not null) return new ElementResolution(el, false, "");
                }
                catch (Exception ex) when (ex is NoSuchElementException or StaleElementReferenceException or InvalidSelectorException)
                {
                    last = ex;
                }
            }
            await Task.Delay(Math.Clamp(step.WaitPollMs, 50, 5000), ct);
        }

        var healed = TrySelfHeal(driver, step);
        if (healed.Element is not null)
        {
            step.HealingCount++;
            step.LastHealedAt = DateTimeOffset.Now;
            if (step.LearnHealedSelector && healed.Candidate is not null)
            {
                profile.Candidates.RemoveAll(c => c.Strategy.Equals(healed.Candidate.Strategy, StringComparison.OrdinalIgnoreCase) && c.Value == healed.Candidate.Value);
                profile.Candidates.Add(healed.Candidate);
                profile.PrimaryStrategy = healed.Candidate.Strategy;
                profile.PrimaryValue = healed.Candidate.Value;
                profile.Confidence = healed.Candidate.Confidence;
            }
            return new ElementResolution(healed.Element, true, healed.Message, healed.Score, healed.Strategy);
        }

        if (last is not null) throw new InvalidOperationException("No stable selector resolved the target element and self-healing could not find a safe replacement.", last);
        return new ElementResolution(null, false, "");
    }

    private sealed record HealingResult(IWebElement? Element, WebSelectorCandidate? Candidate, string Message, int Score = 0, string Strategy = "");

    private static HealingResult TrySelfHeal(IWebDriver driver, AutomationTestStep step)
    {
        if (step.HealingMode == AutomationHealingMode.Off)
            return new HealingResult(null, null, "Self-healing disabled for this step.");

        if (step.ShadowDomMode != AutomationShadowDomMode.None)
            return new HealingResult(null, null, "Self-healing does not escape an explicit Shadow DOM path.");

        if (step.HealingMode == AutomationHealingMode.Conservative)
            return TryConservativeSelfHeal(driver, step);

        return TryAdvancedSelfHeal(driver, step);
    }

    private static HealingResult TryConservativeSelfHeal(IWebDriver driver, AutomationTestStep step)
    {
        var profile = step.SelectorProfile;
        var names = new[] { profile.AccessibleName, step.Target }
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        foreach (var name in names)
        {
            try
            {
                var xpath = $"//*[@aria-label={Xpath(name)} or @title={Xpath(name)} or @placeholder={Xpath(name)} or normalize-space(.)={Xpath(name)}]";
                var matches = driver.FindElements(By.XPath(xpath)).Where(IsDisplayedSafe).Take(3).ToList();
                if (matches.Count != 1) continue;
                var element = matches[0];
                var candidate = new WebSelectorCandidate { Strategy = "xpath", Value = xpath, Confidence = 72, Preferred = true };
                return new HealingResult(element, candidate, $"Recovered target conservatively using exact semantic name '{name}'.", 72, "Exact semantic match");
            }
            catch { }
        }
        return new HealingResult(null, null, "");
    }

    private sealed record HealingCandidate(IWebElement Element, int Score, string Reason);

    private static HealingResult TryAdvancedSelfHeal(IWebDriver driver, AutomationTestStep step)
    {
        var profile = step.SelectorProfile;
        var expectedNames = new[] { profile.AccessibleName, step.Target }
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(NormalizeHealingText)
            .Where(x => x.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (expectedNames.Count == 0 && string.IsNullOrWhiteSpace(profile.TagName) && string.IsNullOrWhiteSpace(profile.Role))
            return new HealingResult(null, null, "Advanced self-healing has insufficient semantic context.");

        IReadOnlyCollection<IWebElement> raw;
        try
        {
            if (!string.IsNullOrWhiteSpace(profile.TagName) && profile.TagName.All(ch => char.IsLetterOrDigit(ch) || ch is '-' or '_'))
                raw = driver.FindElements(By.TagName(profile.TagName)).Take(250).ToList();
            else if (!string.IsNullOrWhiteSpace(profile.Role))
                raw = driver.FindElements(By.CssSelector($"[role=\"{Css(profile.Role)}\"]")).Take(250).ToList();
            else
                raw = driver.FindElements(By.CssSelector("button,a,input,select,textarea,[role],[aria-label],[title],[placeholder]")).Take(250).ToList();
        }
        catch
        {
            return new HealingResult(null, null, "Advanced self-healing could not enumerate safe candidates.");
        }

        var scored = new List<HealingCandidate>();
        foreach (var element in raw)
        {
            if (!IsDisplayedSafe(element)) continue;
            try
            {
                var score = 0;
                var reasons = new List<string>();
                var tag = (element.TagName ?? "").Trim();
                var role = SafeAttribute(element, "role");
                var aria = SafeAttribute(element, "aria-label");
                var title = SafeAttribute(element, "title");
                var placeholder = SafeAttribute(element, "placeholder");
                var text = NormalizeHealingText(SafeText(element));

                if (!string.IsNullOrWhiteSpace(profile.TagName) && tag.Equals(profile.TagName, StringComparison.OrdinalIgnoreCase)) { score += 15; reasons.Add("tag"); }
                if (!string.IsNullOrWhiteSpace(profile.Role) && role.Equals(profile.Role, StringComparison.OrdinalIgnoreCase)) { score += 22; reasons.Add("role"); }

                foreach (var expected in expectedNames)
                {
                    if (SemanticEquals(aria, expected) || SemanticEquals(title, expected) || SemanticEquals(placeholder, expected))
                    {
                        score += 38; reasons.Add("accessible name"); break;
                    }
                    if (text.Equals(expected, StringComparison.OrdinalIgnoreCase))
                    {
                        score += 34; reasons.Add("exact text"); break;
                    }
                    if (text.Length > 0 && (text.Contains(expected, StringComparison.OrdinalIgnoreCase) || expected.Contains(text, StringComparison.OrdinalIgnoreCase)))
                    {
                        score += 18; reasons.Add("text similarity"); break;
                    }
                }

                var id = SafeAttribute(element, "id");
                if (!string.IsNullOrWhiteSpace(id))
                {
                    try
                    {
                        var labels = driver.FindElements(By.CssSelector($"label[for=\"{Css(id)}\"]"));
                        var labelText = labels.Select(x => NormalizeHealingText(SafeText(x))).FirstOrDefault(x => x.Length > 0) ?? "";
                        if (expectedNames.Any(n => labelText.Equals(n, StringComparison.OrdinalIgnoreCase))) { score += 24; reasons.Add("label"); }
                        else if (expectedNames.Any(n => labelText.Contains(n, StringComparison.OrdinalIgnoreCase))) { score += 12; reasons.Add("label context"); }
                    }
                    catch { }
                }

                try
                {
                    var parentText = NormalizeHealingText(SafeText(element.FindElement(By.XPath(".."))));
                    if (parentText.Length > 0 && expectedNames.Any(n => parentText.Contains(n, StringComparison.OrdinalIgnoreCase)))
                    {
                        score += 8; reasons.Add("DOM context");
                    }
                }
                catch { }

                if (score > 0) scored.Add(new HealingCandidate(element, Math.Min(100, score), string.Join(", ", reasons.Distinct())));
            }
            catch { }
        }

        var ordered = scored.OrderByDescending(x => x.Score).Take(3).ToList();
        if (ordered.Count == 0) return new HealingResult(null, null, "Advanced self-healing found no plausible candidate.");

        var best = ordered[0];
        var minimum = Math.Clamp(step.HealingMinimumScore, 50, 100);
        if (best.Score < minimum)
            return new HealingResult(null, null, $"Best self-healing candidate scored {best.Score}/100, below the configured minimum of {minimum}.", best.Score, "Advanced scoring");

        if (ordered.Count > 1 && best.Score - ordered[1].Score < 10)
            return new HealingResult(null, null, $"Self-healing rejected an ambiguous match: top candidates scored {best.Score} and {ordered[1].Score}.", best.Score, "Ambiguity guard");

        var candidate = BuildHealedSelector(driver, best.Element, best.Score);
        if (candidate is null)
            return new HealingResult(null, null, "A likely element was found but no unique stable selector could be built.", best.Score, "Advanced scoring");

        return new HealingResult(best.Element, candidate, $"Recovered target with advanced scoring ({best.Score}/100) using {best.Reason}." + (step.LearnHealedSelector ? " The selector can be learned." : " Learning is disabled for this step."), best.Score, "Advanced semantic + structural scoring");
    }

    private static WebSelectorCandidate? BuildHealedSelector(IWebDriver driver, IWebElement element, int score)
    {
        var tests = new List<WebSelectorCandidate>();
        var dataTestId = SafeAttribute(element, "data-testid");
        var id = SafeAttribute(element, "id");
        var name = SafeAttribute(element, "name");
        var aria = SafeAttribute(element, "aria-label");
        var role = SafeAttribute(element, "role");
        var tag = element.TagName ?? "*";
        var text = NormalizeHealingText(SafeText(element));

        if (!string.IsNullOrWhiteSpace(dataTestId)) tests.Add(new WebSelectorCandidate { Strategy = "data-testid", Value = dataTestId, Confidence = Math.Max(88, score), Preferred = true });
        if (!string.IsNullOrWhiteSpace(id)) tests.Add(new WebSelectorCandidate { Strategy = "id", Value = id, Confidence = Math.Max(86, score), Preferred = true });
        if (!string.IsNullOrWhiteSpace(name)) tests.Add(new WebSelectorCandidate { Strategy = "name", Value = name, Confidence = Math.Max(82, score), Preferred = true });
        if (!string.IsNullOrWhiteSpace(aria)) tests.Add(new WebSelectorCandidate { Strategy = "aria-label", Value = aria, Confidence = Math.Max(80, score), Preferred = true });
        if (!string.IsNullOrWhiteSpace(role) && !string.IsNullOrWhiteSpace(aria)) tests.Add(new WebSelectorCandidate { Strategy = "role+name", Value = $"{role}|{aria}", Confidence = Math.Max(80, score), Preferred = true });
        if (!string.IsNullOrWhiteSpace(text) && text.Length <= 120)
            tests.Add(new WebSelectorCandidate { Strategy = "xpath", Value = $"//{tag}[normalize-space(.)={Xpath(text)}]", Confidence = Math.Max(75, score), Preferred = true });

        foreach (var c in tests)
        {
            try
            {
                var by = ToBy(c);
                if (by is not null && driver.FindElements(by).Count == 1) return c;
            }
            catch { }
        }
        return null;
    }

    private static bool IsDisplayedSafe(IWebElement element)
    {
        try { return element.Displayed; } catch { return false; }
    }

    private static string SafeAttribute(IWebElement element, string name)
    {
        try { return (element.GetAttribute(name) ?? "").Trim(); } catch { return ""; }
    }

    private static string SafeText(IWebElement element)
    {
        try { return element.Text ?? ""; } catch { return ""; }
    }

    private static string NormalizeHealingText(string? value)
        => string.Join(" ", (value ?? "").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)).Trim();

    private static bool SemanticEquals(string actual, string expected)
        => NormalizeHealingText(actual).Equals(NormalizeHealingText(expected), StringComparison.OrdinalIgnoreCase);

    private static By? ToBy(WebSelectorCandidate c)
    {
        var v = c.Value;
        return c.Strategy.ToLowerInvariant() switch
        {
            "data-testid" => By.CssSelector($"[data-testid=\"{Css(v)}\"]"),
            "id" => By.Id(v),
            "name" => By.Name(v),
            "aria-label" => By.CssSelector($"[aria-label=\"{Css(v)}\"]"),
            "role+name" => RoleName(v),
            "css" => By.CssSelector(v),
            "xpath" => By.XPath(v),
            _ => null
        };
    }

    private static By RoleName(string value)
    {
        var parts = value.Split('|', 2);
        var role = parts.ElementAtOrDefault(0) ?? "";
        var name = parts.ElementAtOrDefault(1) ?? "";
        return By.XPath($"//*[@role={Xpath(role)} and (normalize-space(.)={Xpath(name)} or @aria-label={Xpath(name)})]");
    }

    private static string Css(string value) => value.Replace("\\", "\\\\").Replace("\"", "\\\"");
    private static string Xpath(string value)
    {
        if (!value.Contains('\'')) return $"'{value}'";
        if (!value.Contains('\"')) return $"\"{value}\"";
        var parts = value.Split('\'');
        return "concat(" + string.Join(", \"'\", ", parts.Select(p => $"'{p}'")) + ")";
    }

    private static async Task WaitForConditionAsync(IWebDriver driver, AutomationTestStep step, CancellationToken ct)
    {
        var condition = step.WaitCondition;
        if (condition == AutomationWaitCondition.Auto)
        {
            var action = (step.Action ?? "").Trim().ToLowerInvariant();
            condition = action switch
            {
                "click" or "submit" or "input" or "select" => AutomationWaitCondition.ElementEnabled,
                _ => AutomationWaitCondition.ElementVisible
            };
        }
        if (condition == AutomationWaitCondition.None) return;
        if (condition == AutomationWaitCondition.DocumentReady)
        {
            await WaitReadyAsync(driver, ct, step.WaitTimeoutMs, step.WaitPollMs);
            return;
        }

        var timeout = Math.Clamp(step.WaitTimeoutMs, 500, 120000);
        var poll = Math.Clamp(step.WaitPollMs, 50, 5000);
        var deadline = DateTime.UtcNow.AddMilliseconds(timeout);
        Exception? last = null;
        while (DateTime.UtcNow < deadline)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var el = FindNow(driver, step);
                var ok = condition switch
                {
                    AutomationWaitCondition.ElementPresent => el is not null,
                    AutomationWaitCondition.ElementVisible => el?.Displayed == true,
                    AutomationWaitCondition.ElementEnabled => el?.Displayed == true && el.Enabled,
                    AutomationWaitCondition.ElementHidden => el is null || !el.Displayed,
                    _ => true
                };
                if (ok) return;
            }
            catch (Exception ex) when (ex is NoSuchElementException or StaleElementReferenceException or WebDriverException)
            {
                last = ex;
                if (condition == AutomationWaitCondition.ElementHidden) return;
            }
            await Task.Delay(poll, ct);
        }
        throw new TimeoutException($"Smart wait timed out after {timeout} ms waiting for {condition} on step {step.Order}.", last);
    }

    private static async Task WaitReadyAsync(IWebDriver driver, CancellationToken ct, int timeoutMs = 20000, int pollMs = 200)
    {
        if (driver is not IJavaScriptExecutor js) return;
        var deadline = DateTime.UtcNow.AddMilliseconds(Math.Clamp(timeoutMs, 500, 120000));
        var poll = Math.Clamp(pollMs, 50, 5000);
        while (DateTime.UtcNow < deadline)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var state = js.ExecuteScript("return document.readyState")?.ToString();
                if (string.Equals(state, "complete", StringComparison.OrdinalIgnoreCase) || string.Equals(state, "interactive", StringComparison.OrdinalIgnoreCase)) return;
            }
            catch { }
            await Task.Delay(poll, ct);
        }
        throw new TimeoutException("The page did not reach a ready state before the configured timeout.");
    }

    private static string Capture(IWebDriver? driver, string runFolder, int order, string state)
    {
        try
        {
            if (driver is not ITakesScreenshot ss) return "";
            var path = Path.Combine(runFolder, "evidence", $"{order:000}-{state}.png");
            ss.GetScreenshot().SaveAsFile(path);
            return path;
        }
        catch { return ""; }
    }

    private static string SafeUrl(IWebDriver? driver)
    {
        try { return driver?.Url ?? ""; } catch { return ""; }
    }

    public static bool IsInScope(string? candidate, AutomationProject project)
    {
        if (!Uri.TryCreate(candidate, UriKind.Absolute, out var url)) return false;
        if (!Uri.TryCreate(project.BaseUrl, UriKind.Absolute, out var baseUrl)) return false;
        if (string.Equals(url.Host, baseUrl.Host, StringComparison.OrdinalIgnoreCase)) return true;
        var allowed = (project.AllowedDomains ?? "").Split(new[] { ',', ';', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (var raw in allowed)
        {
            var domain = raw.Trim();
            if (Uri.TryCreate(domain, UriKind.Absolute, out var parsed)) domain = parsed.Host;
            domain = domain.TrimStart('*', '.').TrimEnd('/');
            if (string.Equals(url.Host, domain, StringComparison.OrdinalIgnoreCase) || url.Host.EndsWith("." + domain, StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
    }

    private static void EnsureInScope(string? url, AutomationProject project)
    {
        if (!IsInScope(url, project))
            throw new InvalidOperationException($"Automation scope blocked navigation outside the configured web application: {url}");
    }

    private static void ValidateProject(AutomationProject project)
    {
        if (!Uri.TryCreate(project.BaseUrl, UriKind.Absolute, out var u) || (u.Scheme != "http" && u.Scheme != "https"))
            throw new InvalidOperationException("The project requires a valid HTTP/HTTPS Base URL before automation can run.");
    }

    private static string FriendlyError(Exception ex)
    {
        var m = ex.GetBaseException().Message;
        if (m.Contains("driver", StringComparison.OrdinalIgnoreCase) || m.Contains("browser", StringComparison.OrdinalIgnoreCase))
            return "The browser automation driver could not start. Verify that Chrome, Edge or Firefox is installed and retry. " + m;
        return m;
    }
}
