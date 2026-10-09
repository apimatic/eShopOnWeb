using System.Diagnostics;
using System.Net;
using System.Text.Json;
using Microsoft.eShopWeb.SquareCheck.Configuration;
using Microsoft.eShopWeb.SquareCheck.SignIn;

namespace Microsoft.eShopWeb.SquareCheck.UnitTests;

/// <summary>
/// End-to-end runs of the tool with no network: a real listener on a free loopback port, a scripted
/// browser playing the operator, and a stub standing in for Square.
/// </summary>
public class SignInFlowTests
{
    private static readonly SquareCheckTimings Fast = new(TimeSpan.FromSeconds(20), TimeSpan.FromSeconds(20));

    private sealed record Run(int ExitCode, string Output, string Status, ScriptedBrowser Browser, StubSquare Square);

    private static async Task<Run> RunAsync(
        Func<ScriptedBrowser, Uri, Task> operatorScript,
        StubSquare? square = null,
        SquareCheckTimings? timings = null,
        SquareConnectionSettings? settings = null,
        CancellationToken cancellationToken = default,
        bool browserOpens = true)
    {
        square ??= StubSquare.ConnectedMerchant();
        var browser = new ScriptedBrowser(operatorScript, browserOpens);
        var output = new StringWriter();
        var status = new StringWriter();
        using var http = new HttpClient(square);

        var app = new SquareCheckApp(settings ?? TestSettings.OnFreePort(), http, browser, output, status, timings ?? Fast);
        var exitCode = await app.RunAsync(cancellationToken);
        await browser.Script.WaitAsync(TimeSpan.FromSeconds(30));
        return new Run(exitCode, output.ToString(), status.ToString(), browser, square);
    }

    private static Task Approve(ScriptedBrowser browser, Uri signInPage) =>
        browser.LandOnRedirectAsync(signInPage, ("code", "auth-code-1"), ("state", ScriptedBrowser.StateOf(signInPage)));

    [Fact]
    public async Task ApprovedSignIn_NamesTheBusinessInTheBrowser_AndPrintsTheAccount()
    {
        var run = await RunAsync(Approve);

        Assert.Equal(ExitCodes.Success, run.ExitCode);

        var page = Assert.Single(run.Browser.Visits);
        Assert.Equal(HttpStatusCode.OK, page.Status);
        Assert.Contains("Sunrise Coffee &amp; Co", page.Html);
        Assert.Contains("You can close this tab", page.Html);

        Assert.Contains("Business: Sunrise Coffee & Co", run.Output);
        Assert.Contains("Merchant ID: MLR123", run.Output);
        Assert.Contains("2 locations:", run.Output);
        Assert.Contains("Main Street", run.Output);
        Assert.Contains("Status:  ACTIVE", run.Output);
        Assert.Contains("Address: 1 Main St, Springfield, IL 62701, US", run.Output);
        Assert.Contains("Warehouse", run.Output);
        Assert.Contains("Status:  INACTIVE", run.Output);
        Assert.Contains("Address: (no address on file)", run.Output);
    }

    [Fact]
    public async Task ApprovedSignIn_OpensTheSignInPageOnce_WithThisRunsParameters()
    {
        var settings = TestSettings.OnFreePort();
        var run = await RunAsync(Approve, settings: settings);

        var signInPage = Assert.Single(run.Browser.Opened);
        Assert.Equal("https://connect.squareupsandbox.com/oauth2/authorize", signInPage.GetLeftPart(UriPartial.Path));
        var query = ScriptedBrowser.Query(signInPage);
        Assert.Equal(TestSettings.ApplicationId, query["client_id"]);
        Assert.Equal("code", query["response_type"]);
        Assert.Equal("MERCHANT_PROFILE_READ", query["scope"]);
        Assert.Equal(settings.RedirectUri, query["redirect_uri"]);
        Assert.True(query["state"]!.Length >= 43, "state must be unguessable");
        Assert.DoesNotContain(TestSettings.ApplicationSecret, signInPage.AbsoluteUri);
    }

    [Fact]
    public async Task ApprovedSignIn_ExchangesTheCodeOnce_ThenCallsSquareWithTheIssuedToken()
    {
        var settings = TestSettings.OnFreePort();
        var run = await RunAsync(Approve, settings: settings);

        var exchange = Assert.Single(run.Square.To("POST", "/oauth2/token"));
        Assert.Null(exchange.Authorization);
        Assert.Equal("application/json", exchange.ContentType);
        using var body = JsonDocument.Parse(exchange.Body!);
        Assert.Equal(TestSettings.ApplicationId, body.RootElement.GetProperty("client_id").GetString());
        Assert.Equal(TestSettings.ApplicationSecret, body.RootElement.GetProperty("client_secret").GetString());
        Assert.Equal("authorization_code", body.RootElement.GetProperty("grant_type").GetString());
        Assert.Equal("auth-code-1", body.RootElement.GetProperty("code").GetString());
        Assert.Equal(settings.RedirectUri, body.RootElement.GetProperty("redirect_uri").GetString());

        var merchant = Assert.Single(run.Square.To("GET", "/v2/merchants/me"));
        Assert.Equal("Bearer access-token-from-exchange", merchant.Authorization);
        Assert.StartsWith("https://connect.squareupsandbox.com/", merchant.Uri.AbsoluteUri);
        var locations = Assert.Single(run.Square.To("GET", "/v2/locations"));
        Assert.Equal("Bearer access-token-from-exchange", locations.Authorization);
    }

    [Fact]
    public async Task VisitsThatDoNotBelongToThisRun_AreTurnedAway_AndDoNotCompleteTheSignIn()
    {
        var run = await RunAsync(async (browser, signInPage) =>
        {
            // A stale tab from an earlier run, a forwarded link, and a bare visit - none carry this run's state.
            await browser.LandOnRedirectAsync(signInPage, ("code", "stale-code"), ("state", "state-from-an-earlier-run"));
            await browser.LandOnRedirectAsync(signInPage, ("code", "forwarded-code"));
            await browser.LandOnRedirectAsync(signInPage, ("error", "access_denied"), ("state", "someone-elses-state"));
            await browser.LandOnRedirectAsync(signInPage);
            await Approve(browser, signInPage);
        });

        Assert.Equal(ExitCodes.Success, run.ExitCode);
        Assert.Equal(5, run.Browser.Visits.Count);
        Assert.All(run.Browser.Visits.Take(4), visit =>
        {
            Assert.Equal(HttpStatusCode.BadRequest, visit.Status);
            Assert.Contains("does not belong to the sign-in SquareCheck is waiting for", visit.Html);
        });
        Assert.Contains("Sunrise Coffee &amp; Co", run.Browser.Visits[4].Html);

        var exchange = Assert.Single(run.Square.To("POST", "/oauth2/token"));
        Assert.Contains("\"code\":\"auth-code-1\"", exchange.Body);
        Assert.Single(run.Browser.Opened);
    }

    [Fact]
    public async Task OnlyStaleVisits_NeverCompleteTheSignIn()
    {
        var run = await RunAsync(
            (browser, signInPage) => browser.LandOnRedirectAsync(signInPage, ("code", "stale-code"), ("state", "old")),
            timings: Fast with { SignInWindow = TimeSpan.FromSeconds(2) });

        Assert.Equal(ExitCodes.SignInNotCompleted, run.ExitCode);
        Assert.Empty(run.Square.Requests);
        Assert.Contains("not completed within", run.Status);
    }

    [Fact]
    public async Task OtherPathsOnTheRedirectPort_AreNotTreatedAsTheRedirect()
    {
        var run = await RunAsync(async (browser, signInPage) =>
        {
            var state = ScriptedBrowser.StateOf(signInPage);
            var redirect = new Uri(ScriptedBrowser.Query(signInPage)["redirect_uri"]!);
            using var http = new HttpClient();
            var other = await http.GetAsync(new Uri(redirect, $"/elsewhere?code=x&state={Uri.EscapeDataString(state)}"));
            Assert.Equal(HttpStatusCode.NotFound, other.StatusCode);
            await Approve(browser, signInPage);
        });

        Assert.Equal(ExitCodes.Success, run.ExitCode);
        Assert.Single(run.Square.To("POST", "/oauth2/token"));
    }

    [Fact]
    public async Task OperatorDeclines_SaysSo_AndExitsWith2()
    {
        var run = await RunAsync((browser, signInPage) => browser.LandOnRedirectAsync(signInPage,
            ("error", "access_denied"), ("error_description", "user_denied"), ("state", ScriptedBrowser.StateOf(signInPage))));

        Assert.Equal(ExitCodes.SignInNotCompleted, run.ExitCode);
        Assert.Equal("Access was declined in Square; nothing was connected.", run.Status.TrimEnd().Split(Environment.NewLine)[^1]);
        Assert.Contains("You declined access", Assert.Single(run.Browser.Visits).Html);
        Assert.Empty(run.Square.Requests);
        Assert.Empty(run.Output);
    }

    [Fact]
    public async Task SquareRedirectsWithAnotherError_IsReportedAsSquareRefusing()
    {
        var run = await RunAsync((browser, signInPage) => browser.LandOnRedirectAsync(signInPage,
            ("error", "invalid_scope"), ("state", ScriptedBrowser.StateOf(signInPage))));

        Assert.Equal(ExitCodes.SquareFailed, run.ExitCode);
        Assert.Contains("Square refused the sign-in (invalid_scope)", run.Status);
        Assert.Empty(run.Square.Requests);
    }

    [Fact]
    public async Task OperatorDoesNotFinishInTime_SaysSo_AndExitsWith2()
    {
        var run = await RunAsync((_, _) => Task.CompletedTask, timings: Fast with { SignInWindow = TimeSpan.FromMilliseconds(500) });

        Assert.Equal(ExitCodes.SignInNotCompleted, run.ExitCode);
        Assert.Contains("Sign-in was not completed within 500 ms; nothing was connected.", run.Status);
        Assert.Single(run.Browser.Opened);
        Assert.Empty(run.Square.Requests);
    }

    [Fact]
    public async Task CtrlCWhileWaiting_StopsAtOnce_WithExitCode130()
    {
        using var ctrlC = new CancellationTokenSource();
        var stopwatch = Stopwatch.StartNew();

        var run = await RunAsync((_, _) => { ctrlC.CancelAfter(200); return Task.CompletedTask; },
            timings: SquareCheckTimings.Default, cancellationToken: ctrlC.Token);

        Assert.Equal(ExitCodes.Cancelled, run.ExitCode);
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(10), $"took {stopwatch.Elapsed}");
        Assert.Equal("Cancelled.", run.Status.TrimEnd().Split(Environment.NewLine)[^1]);
    }

    [Fact]
    public async Task CtrlCWhileTalkingToSquare_StopsAtOnce_WithExitCode130()
    {
        using var ctrlC = new CancellationTokenSource();
        var square = StubSquare.ConnectedMerchant().On("GET", "/v2/merchants/me", async (_, ct) =>
        {
            ctrlC.Cancel();
            await Task.Delay(Timeout.Infinite, ct);
            throw new UnreachableException();
        });

        var run = await RunAsync(Approve, square, timings: SquareCheckTimings.Default, cancellationToken: ctrlC.Token);

        Assert.Equal(ExitCodes.Cancelled, run.ExitCode);
        Assert.Contains("cancelled in the terminal", Assert.Single(run.Browser.Visits).Html);
    }

    [Fact]
    public async Task SquareRefusesTheCode_SaysSo_AndExitsWith1()
    {
        var square = StubSquare.ConnectedMerchant().On("POST", "/oauth2/token", HttpStatusCode.Unauthorized,
            """{"message":"Authorization code is expired","type":"service.not_authorized"}""");

        var run = await RunAsync(Approve, square);

        Assert.Equal(ExitCodes.SquareFailed, run.ExitCode);
        var line = run.Status.TrimEnd().Split(Environment.NewLine)[^1];
        Assert.Equal("Square refused to complete the sign-in: HTTP 401 Unauthorized - Authorization code is expired.", line);
        Assert.Contains("could not finish connecting", Assert.Single(run.Browser.Visits).Html);
        Assert.Single(run.Square.Requests);
    }

    [Fact]
    public async Task SquareErrorBodies_AreSummarisedOnOneLine()
    {
        var square = StubSquare.ConnectedMerchant().On("GET", "/v2/locations", HttpStatusCode.Forbidden,
            """{"errors":[{"category":"AUTHENTICATION_ERROR","code":"INSUFFICIENT_SCOPES","detail":"The merchant has not given your application\nsufficient permissions."}]}""");

        var run = await RunAsync(Approve, square);

        Assert.Equal(ExitCodes.SquareFailed, run.ExitCode);
        Assert.Equal(
            "Square refused to list the locations: HTTP 403 Forbidden - INSUFFICIENT_SCOPES: The merchant has not given your application sufficient permissions.",
            run.Status.TrimEnd().Split(Environment.NewLine)[^1]);
        Assert.DoesNotContain("   at ", run.Status);
    }

    [Fact]
    public async Task TokenExchangeConnectionFailure_IsReported_AndTheCodeIsNotResent()
    {
        var square = StubSquare.ConnectedMerchant().On("POST", "/oauth2/token",
            (_, _) => throw new HttpRequestException("connection reset"));

        var run = await RunAsync(Approve, square);

        Assert.Equal(ExitCodes.SquareFailed, run.ExitCode);
        Assert.Contains("Could not reach Square to complete the sign-in.", run.Status);
        Assert.Single(run.Square.To("POST", "/oauth2/token"));
    }

    [Fact]
    public async Task UnreadableMerchantResponse_IsReportedAsASquareFailure()
    {
        var square = StubSquare.ConnectedMerchant().On("GET", "/v2/merchants/me", HttpStatusCode.OK,
            """{"merchant":{"id":"MLR123","business_name":"No Country Ltd"}}""");

        var run = await RunAsync(Approve, square);

        Assert.Equal(ExitCodes.SquareFailed, run.ExitCode);
        Assert.Contains("Square answered the request to read the merchant profile (HTTP 200) with a response SquareCheck could not read.", run.Status);
    }

    [Fact]
    public async Task MissingAccessToken_IsReportedAsASquareFailure()
    {
        var square = StubSquare.ConnectedMerchant().On("POST", "/oauth2/token", HttpStatusCode.OK, """{"token_type":"bearer"}""");

        var run = await RunAsync(Approve, square);

        Assert.Equal(ExitCodes.SquareFailed, run.ExitCode);
        Assert.Contains("Square did not issue an access token", run.Status);
        Assert.Empty(run.Square.To("GET", "/v2/merchants/me"));
    }

    [Fact]
    public async Task SquareTooSlow_IsReportedWithinTheBudget()
    {
        var square = StubSquare.ConnectedMerchant().On("GET", "/v2/merchants/me", async (_, ct) =>
        {
            await Task.Delay(Timeout.Infinite, ct);
            throw new UnreachableException();
        });

        var run = await RunAsync(Approve, square, timings: Fast with { SquareBudget = TimeSpan.FromMilliseconds(500) });

        Assert.Equal(ExitCodes.SquareFailed, run.ExitCode);
        Assert.Contains("Square did not answer within 500 ms.", run.Status);
    }

    [Fact]
    public async Task BrowserCannotBeOpened_TheAddressIsPrinted_AndTheSignInStillWorks()
    {
        var run = await RunAsync(Approve, browserOpens: false);

        Assert.Equal(ExitCodes.Success, run.ExitCode);
        Assert.Contains("Could not open a browser automatically.", run.Status);
        Assert.Contains($"visit: {Assert.Single(run.Browser.Opened).AbsoluteUri}", run.Status);
    }

    [Fact]
    public async Task RedirectPortAlreadyInUse_IsASetupProblem()
    {
        var settings = TestSettings.OnFreePort();
        using var squatter = new HttpListener();
        squatter.Prefixes.Add($"http://{settings.RedirectAddress.Authority}/");
        squatter.Start();

        var run = await RunAsync((_, _) => Task.CompletedTask, settings: settings);

        Assert.Equal(ExitCodes.SetupProblem, run.ExitCode);
        Assert.Contains("Cannot listen for Square's redirect", run.Status);
        Assert.Empty(run.Browser.Opened);
    }

    [Fact]
    public async Task EachRunUsesItsOwnState_SoAnEarlierRunsRedirectCannotCompleteALaterRun()
    {
        string? firstState = null;
        await RunAsync((browser, signInPage) =>
        {
            firstState = ScriptedBrowser.StateOf(signInPage);
            return Approve(browser, signInPage);
        });

        var second = await RunAsync(
            (browser, signInPage) => browser.LandOnRedirectAsync(signInPage, ("code", "replayed"), ("state", firstState!)),
            timings: Fast with { SignInWindow = TimeSpan.FromSeconds(2) });

        Assert.Equal(ExitCodes.SignInNotCompleted, second.ExitCode);
        Assert.Equal(HttpStatusCode.BadRequest, Assert.Single(second.Browser.Visits).Status);
        Assert.Empty(second.Square.Requests);
    }
}
