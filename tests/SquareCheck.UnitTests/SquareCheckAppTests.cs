using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.eShopWeb.SquareCheck.Configuration;
using Microsoft.eShopWeb.SquareCheck.UnitTests.TestDoubles;
using Square.Servers;
using Xunit;

namespace Microsoft.eShopWeb.SquareCheck.UnitTests;

/// <summary>
/// The whole run, end to end, with the browser and Square played by test doubles. The redirect listener is
/// real (Kestrel on a dynamic loopback port), so the browser's part goes over HTTP exactly as it would live.
/// </summary>
public sealed class SquareCheckAppTests : IDisposable
{
    private const string ApplicationId = "sandbox-sq0idb-TESTAPPLICATIONID";
    private const string ApplicationSecret = "sandbox-sq0csb-TEST-SECRET-NOT-REAL";
    private const string Code = "sandbox-sq0cgb-TESTCODE";

    private readonly SquareStub _square = new();
    private readonly FakeBrowser _browser = new();
    private readonly StringWriter _output = new();
    private readonly StringWriter _error = new();

    public void Dispose()
    {
        _browser.Dispose();
    }

    private static SquareSettings Settings(Uri? redirectUri = null) => new(
        ServerEnvironment.Sandbox,
        ApplicationId,
        ApplicationSecret,
        redirectUri ?? new Uri("http://127.0.0.1:0/callback"));

    private Task<int> RunAsync(
        CancellationToken cancellationToken = default,
        SquareCheckTimeouts? timeouts = null,
        SquareSettings? settings = null)
    {
        var app = new SquareCheckApp(
            settings ?? Settings(),
            _browser,
            _output,
            _error,
            timeouts ?? new SquareCheckTimeouts(TimeSpan.FromSeconds(20), TimeSpan.FromSeconds(10)),
            () => _square)
        {
            OnListening = uri => _browser.CallbackUri.TrySetResult(uri),
        };
        return app.RunAsync(cancellationToken);
    }

    /// <summary>The operator approves: Square sends the browser back with the code and this run's state.</summary>
    private static Func<FakeBrowser, Uri, Task> Approve(string code = Code) =>
        (browser, authUrl) => browser.VisitCallbackAsync(("code", code), ("state", FakeBrowser.QueryOf(authUrl)["state"]));

    [Fact]
    public async Task ApprovedSignIn_ShowsMerchantAndLocations_AndExitsZero()
    {
        _browser.OnOpen = Approve();

        var exitCode = await RunAsync();
        await _browser.Activity;

        Assert.Equal(ExitCodes.Success, exitCode);
        Assert.Equal(string.Empty, _error.ToString());

        var output = _output.ToString();
        Assert.Contains("Business:    Test <Shop> & Co", output);
        Assert.Contains("Merchant id: MLTEST123", output);
        Assert.Contains("Locations (2):", output);
        Assert.Contains("  - Main Street", output);
        Assert.Contains("Status:  ACTIVE", output);
        Assert.Contains("Address: 1 Main St, Springfield IL 62701, US", output);
        Assert.Contains("  - Old Kiosk", output);
        Assert.Contains("Status:  INACTIVE", output);
        Assert.Contains("Address: (no address)", output);
    }

    [Fact]
    public async Task ApprovedSignIn_BrowserPageNamesTheBusiness_AndSaysTheTabCanBeClosed()
    {
        _browser.OnOpen = Approve();

        await RunAsync();
        await _browser.Activity;

        var page = Assert.Single(_browser.Visits);
        Assert.Equal(HttpStatusCode.OK, page.Status);
        Assert.Contains("Test &lt;Shop&gt; &amp; Co", page.Body); // named, and HTML-encoded
        Assert.Contains("You can close this tab", page.Body);
    }

    [Fact]
    public async Task OpensSquaresSandboxSignInPage_Once_AskingForMerchantProfileRead()
    {
        _browser.OnOpen = Approve();

        await RunAsync();
        await _browser.Activity;

        var opened = Assert.Single(_browser.OpenedUrls);
        var url = new Uri(opened);
        Assert.Equal("https://connect.squareupsandbox.com/oauth2/authorize", url.GetLeftPart(UriPartial.Path));

        var query = FakeBrowser.QueryOf(url);
        Assert.Equal(ApplicationId, query["client_id"]);
        Assert.Equal("MERCHANT_PROFILE_READ", query["scope"]);
        Assert.Equal("code", query["response_type"]);
        Assert.Equal("http://127.0.0.1:0/callback", query["redirect_uri"]);
        Assert.True(query["state"].Length >= 32);
        Assert.False(query.ContainsKey("code_challenge"));
        Assert.DoesNotContain(ApplicationSecret, opened);
    }

    [Fact]
    public async Task ExchangesTheCodeWithTheAppSecret_ThenCallsSquareWithTheIssuedToken()
    {
        _browser.OnOpen = Approve();

        await RunAsync();

        var exchange = Assert.Single(_square.RequestsTo("POST /oauth2/token"));
        Assert.Equal("connect.squareupsandbox.com", exchange.Uri.Host);
        var form = QueryHelpers.ParseQuery(exchange.Body);
        Assert.Equal("authorization_code", form["grant_type"]);
        Assert.Equal(Code, form["code"]);
        Assert.Equal(ApplicationId, form["client_id"]);
        Assert.Equal(ApplicationSecret, form["client_secret"]);
        Assert.Equal("http://127.0.0.1:0/callback", form["redirect_uri"]);
        Assert.False(form.ContainsKey("code_verifier"));

        foreach (var route in new[] { "GET /v2/merchants/me", "GET /v2/locations" })
        {
            var call = Assert.Single(_square.RequestsTo(route));
            Assert.Equal($"Bearer {SquareStub.AccessToken}", call.Authorization);
        }

        Assert.DoesNotContain(SquareStub.AccessToken, _output.ToString());
    }

    [Fact]
    public async Task VisitsThatDoNotBelongToThisRun_DoNotCompleteTheSignIn()
    {
        _browser.OnOpen = async (browser, authUrl) =>
        {
            var state = FakeBrowser.QueryOf(authUrl)["state"];
            // A stale tab from an earlier run, a link without state, a forged duplicate state.
            await browser.VisitCallbackAsync(("code", "stale-code"), ("state", "state-from-an-earlier-run"));
            await browser.VisitCallbackAsync(("code", "no-state-code"));
            await browser.VisitCallbackAsync(("code", "dup-code"), ("state", "x"), ("state", state));
            await browser.VisitCallbackAsync(("error", "access_denied"), ("state", "state-from-an-earlier-run"));

            // Then the operator really approves.
            await browser.VisitCallbackAsync(("code", Code), ("state", state));
        };

        var exitCode = await RunAsync();
        await _browser.Activity;

        Assert.Equal(ExitCodes.Success, exitCode);
        var visits = _browser.Visits;
        Assert.Equal(5, visits.Count);
        Assert.All(visits.Take(4), v =>
        {
            Assert.Equal(HttpStatusCode.BadRequest, v.Status);
            Assert.Contains("does not belong to the sign-in", v.Body);
        });
        Assert.Equal(HttpStatusCode.OK, visits[4].Status);

        var exchange = Assert.Single(_square.RequestsTo("POST /oauth2/token"));
        Assert.Equal(Code, QueryHelpers.ParseQuery(exchange.Body)["code"]);
    }

    [Fact]
    public async Task StaleVisitAlone_NeverCompletes_AndTheRunEndsAsNotSignedIn()
    {
        _browser.OnOpen = (browser, _) =>
            browser.VisitCallbackAsync(("code", "stale-code"), ("state", "state-from-an-earlier-run"));

        var exitCode = await RunAsync(timeouts: new SquareCheckTimeouts(TimeSpan.FromMilliseconds(800), TimeSpan.FromSeconds(10)));

        Assert.Equal(ExitCodes.SignInNotCompleted, exitCode);
        Assert.Empty(_square.Requests);
    }

    [Fact]
    public async Task ASecondVisitForThisRun_IsNotTreatedAsAnotherSignIn()
    {
        var replayed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var exchanged = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _square.On("POST /oauth2/token", async (_, ct) =>
        {
            exchanged.TrySetResult();
            await replayed.Task.WaitAsync(ct); // keep the run (and its listener) alive until the replay arrived
            return SquareStub.Json(HttpStatusCode.OK, SquareStub.TokenJson);
        });
        _browser.OnOpen = async (browser, authUrl) =>
        {
            var state = FakeBrowser.QueryOf(authUrl)["state"];
            var first = browser.VisitCallbackAsync(("code", Code), ("state", state));
            await exchanged.Task;
            await browser.VisitCallbackAsync(("code", "replayed-code"), ("state", state));
            replayed.SetResult();
            await first;
        };

        var exitCode = await RunAsync();
        await _browser.Activity;

        Assert.Equal(ExitCodes.Success, exitCode);
        Assert.Contains(_browser.Visits, v => v.Status == HttpStatusCode.Conflict && v.Body.Contains("already handled"));
        Assert.Single(_square.RequestsTo("POST /oauth2/token"));
    }

    [Fact]
    public async Task OperatorDeclines_SaysSo_ExitsTwo_AndNeverCallsSquare()
    {
        _browser.OnOpen = (browser, authUrl) => browser.VisitCallbackAsync(
            ("error", "access_denied"),
            ("error_description", "user denied access"),
            ("state", FakeBrowser.QueryOf(authUrl)["state"]));

        var exitCode = await RunAsync();
        await _browser.Activity;

        Assert.Equal(ExitCodes.SignInNotCompleted, exitCode);
        var line = Assert.Single(Lines(_error));
        Assert.StartsWith("Sign-in declined: access to the Square account was not approved", line);
        Assert.Contains("access_denied", line);
        Assert.Empty(_square.Requests);

        var page = Assert.Single(_browser.Visits);
        Assert.Contains("was not approved", page.Body);
    }

    [Fact]
    public async Task OperatorDoesNotFinishInTime_SaysSo_ExitsTwo_AndLateVisitsDoNotComplete()
    {
        var exitCode = await RunAsync(timeouts: new SquareCheckTimeouts(TimeSpan.FromMilliseconds(500), TimeSpan.FromSeconds(10)));

        Assert.Equal(ExitCodes.SignInNotCompleted, exitCode);
        var line = Assert.Single(Lines(_error));
        Assert.StartsWith("Sign-in not completed: nobody finished signing in within", line);
        Assert.Single(_browser.OpenedUrls);
        Assert.Empty(_square.Requests);
    }

    [Fact]
    public void SignInTimeoutDefaultsToFiveMinutes()
    {
        Assert.Equal(TimeSpan.FromMinutes(5), SquareCheckTimeouts.Default.SignIn);
    }

    [Fact]
    public async Task CtrlCWhileWaitingForTheOperator_StopsAtOnce_WithExitCode130()
    {
        using var cancellation = new CancellationTokenSource();
        _browser.OnOpen = (_, _) =>
        {
            cancellation.CancelAfter(TimeSpan.FromMilliseconds(200));
            return Task.CompletedTask;
        };

        var stopwatch = Stopwatch.StartNew();
        var exitCode = await RunAsync(cancellation.Token, new SquareCheckTimeouts(TimeSpan.FromMinutes(5), TimeSpan.FromSeconds(30)));

        Assert.Equal(ExitCodes.Cancelled, exitCode);
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(5), $"took {stopwatch.Elapsed}");
        Assert.Equal("Cancelled.", Assert.Single(Lines(_error)));
        Assert.Empty(_square.Requests);
    }

    [Fact]
    public async Task CtrlCWhileTalkingToSquare_StopsAtOnce_WithExitCode130()
    {
        using var cancellation = new CancellationTokenSource();
        _square.On("GET /v2/merchants/me", async (_, ct) =>
        {
            cancellation.Cancel();
            await Task.Delay(Timeout.Infinite, ct);
            throw new InvalidOperationException("unreachable");
        });
        _browser.OnOpen = Approve();

        var exitCode = await RunAsync(cancellation.Token);

        Assert.Equal(ExitCodes.Cancelled, exitCode);
        Assert.Equal("Cancelled.", Assert.Single(Lines(_error)));
    }

    [Fact]
    public async Task SquareRefusesTheCodeExchange_SaysSo_ExitsOne()
    {
        _square.On("POST /oauth2/token", HttpStatusCode.Unauthorized,
            """{"errors":[{"category":"AUTHENTICATION_ERROR","code":"UNAUTHORIZED","detail":"Authorization code not found"}]}""");
        _browser.OnOpen = Approve();

        var exitCode = await RunAsync();
        await _browser.Activity;

        Assert.Equal(ExitCodes.SquareFailure, exitCode);
        var line = Assert.Single(Lines(_error));
        Assert.Equal(
            "Square refused the sign-in (POST /oauth2/token): HTTP 401 Unauthorized – UNAUTHORIZED: Authorization code not found.",
            line);
        Assert.DoesNotContain(ApplicationSecret, line);
        Assert.Empty(_square.RequestsTo("GET /v2/merchants/me"));

        var page = Assert.Single(_browser.Visits);
        Assert.Contains("could not finish connecting", page.Body);
    }

    [Fact]
    public async Task SquareUnreachableDuringTheCodeExchange_SaysSo_ExitsOne_WithoutResendingTheCode()
    {
        _square.On("POST /oauth2/token", (_, _) => throw new HttpRequestException("connection reset"));
        _browser.OnOpen = Approve();

        var exitCode = await RunAsync();

        Assert.Equal(ExitCodes.SquareFailure, exitCode);
        Assert.Equal("Could not reach Square (POST /oauth2/token): connection reset", Assert.Single(Lines(_error)));
        Assert.Single(_square.RequestsTo("POST /oauth2/token"));
        Assert.Single(_browser.OpenedUrls);
    }

    [Fact]
    public async Task SquareRefusesTheMerchantRequest_SaysSo_ExitsOne()
    {
        _square.On("GET /v2/merchants/me", HttpStatusCode.Forbidden,
            """{"errors":[{"category":"AUTHENTICATION_ERROR","code":"INSUFFICIENT_SCOPES","detail":"The merchant has not given your application sufficient permissions."}]}""");
        _browser.OnOpen = Approve();

        var exitCode = await RunAsync();

        Assert.Equal(ExitCodes.SquareFailure, exitCode);
        Assert.Equal(
            "Square refused the request (GET /v2/merchants/me): HTTP 403 Forbidden – INSUFFICIENT_SCOPES: The merchant has not given your application sufficient permissions.",
            Assert.Single(Lines(_error)));
    }

    [Fact]
    public async Task SquareRejectsTheTokenOnLocations_ReportsIt_AndDoesNotOpenTheSignInPageAgain()
    {
        _square.On("GET /v2/locations", HttpStatusCode.Unauthorized,
            """{"errors":[{"category":"AUTHENTICATION_ERROR","code":"UNAUTHORIZED","detail":"This request could not be authorized."}]}""");
        _browser.OnOpen = Approve();

        var exitCode = await RunAsync();
        await _browser.Activity;

        Assert.Equal(ExitCodes.SquareFailure, exitCode);
        Assert.StartsWith("Square refused the request (GET /v2/locations): HTTP 401 Unauthorized", Assert.Single(Lines(_error)));
        Assert.Single(_browser.OpenedUrls);
        // The browser was already told which business it connected to.
        Assert.Contains("Test &lt;Shop&gt; &amp; Co", Assert.Single(_browser.Visits).Body);
    }

    [Fact]
    public async Task SquareSendsAnUnreadableMerchant_SaysSo_ExitsOne()
    {
        _square.On("GET /v2/merchants/me", HttpStatusCode.OK, """{"merchant":{"id":"MLTEST123","business_name":"No country"}}""");
        _browser.OnOpen = Approve();

        var exitCode = await RunAsync();

        Assert.Equal(ExitCodes.SquareFailure, exitCode);
        Assert.Equal(
            "Square sent a response SquareCheck could not read (GET /v2/merchants/me, HTTP 200 OK).",
            Assert.Single(Lines(_error)));
    }

    [Fact]
    public async Task SquareSendsAProxyErrorPage_StillOneLine_ExitsOne()
    {
        _square.On("GET /v2/locations", (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.BadRequest)
        {
            Content = new StringContent("<html>\n<body>Bad gateway\r\n</body></html>", System.Text.Encoding.UTF8, "text/html"),
        }));
        _browser.OnOpen = Approve();

        var exitCode = await RunAsync();

        Assert.Equal(ExitCodes.SquareFailure, exitCode);
        Assert.Equal("Square refused the request (GET /v2/locations): HTTP 400 BadRequest.", Assert.Single(Lines(_error)));
    }

    [Fact]
    public async Task SquareDoesNotAnswerWithinTheRequestBudget_SaysSo_ExitsOne()
    {
        _square.On("GET /v2/merchants/me", async (_, ct) =>
        {
            await Task.Delay(Timeout.Infinite, ct);
            throw new InvalidOperationException("unreachable");
        });
        _browser.OnOpen = Approve();

        var stopwatch = Stopwatch.StartNew();
        var exitCode = await RunAsync(timeouts: new SquareCheckTimeouts(TimeSpan.FromSeconds(20), TimeSpan.FromMilliseconds(500)));

        Assert.Equal(ExitCodes.SquareFailure, exitCode);
        Assert.StartsWith("Square did not respond within", Assert.Single(Lines(_error)));
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(10), $"took {stopwatch.Elapsed}");
    }

    [Fact]
    public async Task RedirectPortAlreadyInUse_SaysSo_ExitsThree_WithoutOpeningTheBrowser()
    {
        using var occupant = new TcpListener(IPAddress.Loopback, 0);
        occupant.Start();
        var port = ((IPEndPoint)occupant.LocalEndpoint).Port;

        var exitCode = await RunAsync(settings: Settings(new Uri($"http://127.0.0.1:{port}/callback")));

        Assert.Equal(ExitCodes.CannotStart, exitCode);
        var line = Assert.Single(Lines(_error));
        Assert.Contains($"port {port} is unavailable", line);
        Assert.Empty(_browser.OpenedUrls);
    }

    [Fact]
    public async Task TheListenerOnlyAnswersAtTheRedirectPath()
    {
        _browser.OnOpen = async (browser, authUrl) =>
        {
            var callback = await browser.CallbackUri.Task;
            var state = FakeBrowser.QueryOf(authUrl)["state"];
            await browser.VisitAsync(new Uri(callback, $"/favicon.ico"));
            await browser.VisitAsync(new Uri(callback, $"/other?code=x&state={state}"));
            await browser.VisitAsync(new Uri($"{callback}?code=x&state={state}"), HttpMethod.Post);
            await browser.VisitCallbackAsync(("code", Code), ("state", state));
        };

        var exitCode = await RunAsync();
        await _browser.Activity;

        Assert.Equal(ExitCodes.Success, exitCode);
        Assert.Equal(
            [HttpStatusCode.NotFound, HttpStatusCode.NotFound, HttpStatusCode.NotFound, HttpStatusCode.OK],
            _browser.Visits.Select(v => v.Status));
    }

    private static string[] Lines(StringWriter writer) =>
        writer.ToString().Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
}
