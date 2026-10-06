using OpenQA.Selenium;
using OpenQA.Selenium.Chrome;
using OpenQA.Selenium.Edge;
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
        CancellationToken cancellationToken)
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
            driver = CreateDriver(project.DefaultBrowser);
            driver.Manage().Timeouts().ImplicitWait = TimeSpan.FromMilliseconds(250);
            driver.Manage().Window.Maximize();
            driver.Navigate().GoToUrl(project.BaseUrl);

            foreach (var step in testCase.Steps.OrderBy(s => s.Order))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var stepRun = new AutomationStepRun
                {
                    TestStepId = step.Id,
                    Order = step.Order,
                    Action = step.Action,
                    Target = step.Target,
                    Selector = step.SelectorProfile.PrimaryDisplay,
                    Status = AutomationStepRunStatus.Running,
                    StartedAt = DateTimeOffset.Now,
                    Url = SafeUrl(driver)
                };
                run.Steps.Add(stepRun);
                progress?.Invoke(run, stepRun);
                await _store.SaveAsync(run);

                try
                {
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
                        stepRun.ScreenshotPath = Capture(driver, run.RunFolder, step.Order, passed ? "manual-pass" : "manual-fail");
                        progress?.Invoke(run, stepRun);
                        await _store.SaveAsync(run);
                        if (!passed) break;
                        continue;
                    }

                    EnsureInScope(SafeUrl(driver), project);
                    Exception? executionError = null;
                    var attempts = Math.Max(1, step.MaxRetries + 1);
                    for (var attempt = 1; attempt <= attempts; attempt++)
                    {
                        try
                        {
                            await ExecuteStepAsync(driver, step, project, dataSet, stepRun, cancellationToken);
                            stepRun.Url = SafeUrl(driver);
                            EnsureInScope(stepRun.Url, project);
                            stepRun.Assertions.Clear();
                            await EvaluateAssertionsAsync(driver, step, stepRun, dataSet, cancellationToken);
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
                                stepRun.Message = $"Retry {stepRun.RetryCount}/{step.MaxRetries}: {FriendlyError(ex)}";
                                progress?.Invoke(run, stepRun);
                                await Task.Delay(Math.Max(100, step.RetryDelayMs), cancellationToken);
                            }
                        }
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
                            stepRun.ScreenshotPath = Capture(driver, run.RunFolder, step.Order, passed ? "recovered-manual" : "fail");
                            if (!passed) run.FailureMessage = stepRun.Message;
                            goto StepCompleted;
                        }
                        throw executionError;
                    }

                    stepRun.Status = AutomationStepRunStatus.Passed;
                    var healingSuffix = stepRun.SelectorHealed ? " Selector self-healing was applied." : "";
                    stepRun.Message = (stepRun.Assertions.Count == 0 ? "Step completed successfully." : $"Step completed. {stepRun.Assertions.Count} assertion(s) passed.") + healingSuffix;
                    stepRun.ScreenshotPath = AutomationDataResolver.UsesSecret(step.ValueExpression, dataSet) ? "" : Capture(driver, run.RunFolder, step.Order, "pass");
                    StepCompleted: ;
                }
                catch (Exception ex)
                {
                    stepRun.Status = AutomationStepRunStatus.Failed;
                    stepRun.Message = FriendlyError(ex);
                    stepRun.Url = SafeUrl(driver);
                    stepRun.ScreenshotPath = Capture(driver, run.RunFolder, step.Order, "fail");
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

                if (stepRun.Status is AutomationStepRunStatus.Failed or AutomationStepRunStatus.ManualFailed && step.OnFailure != AutomationFailureAction.ContinueTest) break;
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

    private static IWebDriver CreateDriver(string browser)
    {
        if (browser.Equals("Edge", StringComparison.OrdinalIgnoreCase))
        {
            var options = new EdgeOptions();
            options.AddArgument("--disable-notifications");
            return new EdgeDriver(options);
        }
        else
        {
            var options = new ChromeOptions();
            options.AddArgument("--disable-notifications");
            return new ChromeDriver(options);
        }
    }

    private static async Task ExecuteStepAsync(IWebDriver driver, AutomationTestStep step, AutomationProject project, AutomationDataSet? dataSet, AutomationStepRun stepRun, CancellationToken ct)
    {
        var action = step.Action.Trim().ToLowerInvariant();
        if (action == "navigate")
        {
            var targetUrl = string.IsNullOrWhiteSpace(step.SourceUrl) ? project.BaseUrl : step.SourceUrl;
            EnsureInScope(targetUrl, project);
            driver.Navigate().GoToUrl(targetUrl);
            await WaitReadyAsync(driver, ct);
            return;
        }

        if (action == "scroll")
        {
            if (driver is IJavaScriptExecutor js) js.ExecuteScript("window.scrollBy(0, Math.max(300, window.innerHeight * 0.65));");
            await Task.Delay(250, ct);
            return;
        }

        var resolution = await ResolveElementAsync(driver, step, ct);
        var element = resolution.Element;
        if (resolution.Healed)
        {
            stepRun.SelectorHealed = true;
            stepRun.HealingMessage = resolution.Message;
            stepRun.Selector = step.SelectorProfile.PrimaryDisplay;
        }
        if (element is null) throw new InvalidOperationException($"Target not found: {step.Target}");

        if (action is "click" or "submit")
        {
            element.Click();
            await Task.Delay(300, ct);
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

    private static bool RequiresManualData(AutomationTestStep step) =>
        (step.Action.Equals("Input", StringComparison.OrdinalIgnoreCase) || step.Action.Equals("Select", StringComparison.OrdinalIgnoreCase))
        && string.IsNullOrWhiteSpace(step.ValueExpression);

    private static async Task EvaluateAssertionsAsync(IWebDriver driver, AutomationTestStep step, AutomationStepRun stepRun, AutomationDataSet? dataSet, CancellationToken ct)
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
                        if (FindNow(driver, step.SelectorProfile)?.Displayed == true) throw new InvalidOperationException("Target element is still visible.");
                        break;
                }
                result.Status = AutomationAssertionRunStatus.Passed;
                result.Message = "Assertion passed.";
            }
            catch (Exception ex)
            {
                result.Status = AutomationAssertionRunStatus.Failed;
                result.Message = ex.GetBaseException().Message;
            }
            stepRun.Assertions.Add(result);
        }
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

    private sealed record ElementResolution(IWebElement? Element, bool Healed, string Message);

    private static async Task<ElementResolution> ResolveElementAsync(IWebDriver driver, AutomationTestStep step, CancellationToken ct)
    {
        var profile = step.SelectorProfile;
        var candidates = profile.Candidates.OrderByDescending(x => x.Confidence).ToList();
        if (candidates.Count == 0 && !string.IsNullOrWhiteSpace(profile.PrimaryValue))
            candidates.Add(new WebSelectorCandidate { Strategy = profile.PrimaryStrategy, Value = profile.PrimaryValue, Confidence = profile.Confidence });

        var deadline = DateTime.UtcNow.AddSeconds(10);
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
            await Task.Delay(250, ct);
        }

        var healed = TrySelfHeal(driver, step);
        if (healed.Element is not null)
        {
            step.HealingCount++;
            step.LastHealedAt = DateTimeOffset.Now;
            if (healed.Candidate is not null)
            {
                profile.Candidates.RemoveAll(c => c.Strategy.Equals(healed.Candidate.Strategy, StringComparison.OrdinalIgnoreCase) && c.Value == healed.Candidate.Value);
                profile.Candidates.Add(healed.Candidate);
                profile.PrimaryStrategy = healed.Candidate.Strategy;
                profile.PrimaryValue = healed.Candidate.Value;
                profile.Confidence = healed.Candidate.Confidence;
            }
            return new ElementResolution(healed.Element, true, healed.Message);
        }

        if (last is not null) throw new InvalidOperationException("No stable selector resolved the target element and self-healing could not find a safe replacement.", last);
        return new ElementResolution(null, false, "");
    }

    private sealed record HealingResult(IWebElement? Element, WebSelectorCandidate? Candidate, string Message);

    private static HealingResult TrySelfHeal(IWebDriver driver, AutomationTestStep step)
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
                var matches = driver.FindElements(By.XPath(xpath)).Where(x => x.Displayed).Take(3).ToList();
                if (matches.Count != 1) continue;
                var element = matches[0];
                var candidate = new WebSelectorCandidate { Strategy = "xpath", Value = xpath, Confidence = 72, Preferred = true };
                return new HealingResult(element, candidate, $"Recovered target semantically using '{name}'.");
            }
            catch { }
        }
        return new HealingResult(null, null, "");
    }

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

    private static async Task WaitReadyAsync(IWebDriver driver, CancellationToken ct)
    {
        if (driver is not IJavaScriptExecutor js) return;
        var deadline = DateTime.UtcNow.AddSeconds(20);
        while (DateTime.UtcNow < deadline)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                if (string.Equals(js.ExecuteScript("return document.readyState")?.ToString(), "complete", StringComparison.OrdinalIgnoreCase)) return;
            }
            catch { }
            await Task.Delay(200, ct);
        }
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
            return "The browser automation driver could not start. Verify that Chrome/Edge is installed and retry. " + m;
        return m;
    }
}
