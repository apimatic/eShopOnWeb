using System.Net;
using Square;
using Square.Core.Authentication.OAuth2.AuthorizationCode;
using Square.Core.Configuration;
using Square.Core.ErrorResponse;
using Square.Core.Exceptions;
using Square.Servers;
using SquareCheck.SignIn;
using SquareCheck.Tests.Helpers;
using Xunit;

namespace SquareCheck.Tests.SignIn;

public sealed class SignInFlowTests
{
    private static SquareSettings MakeSettings() => new()
    {
        ApplicationId = "test_app_id",
        ApplicationSecret = "test_app_secret",
        RedirectUri = "http://localhost:8888/callback",
        SquareEnvironment = ServerEnvironment.Sandbox,
    };

    private static SquareClient MakeClient(StubHandler handler, string? bearerToken = null)
    {
        var opts = new SquareClientOptions
        {
            Retry = RetryOptions.Disabled(),
            Logging = new LoggingOptions { LoggerFactory = null },
        };

        if (bearerToken is not null)
        {
            opts.Oauth2 = new OAuth2AuthorizationCodeCredentials
            {
                ClientId = "test_app_id",
                RedirectUri = "http://localhost:8888/callback",
                PromptForAuthorizationCode = (_, _) => Task.FromResult(string.Empty),
            };
            opts.Oauth2TokenStrategy = new PreObtainedTokenStrategy(bearerToken);
        }

        return new SquareClient(new HttpClient(handler), opts);
    }

    // ─── ParseCallback ────────────────────────────────────────────────────────

    [Fact]
    public void ParseCallback_ReturnsSuccess_WhenCodeAndStateMatch()
    {
        const string state = "abc123";
        var uri = new Uri($"http://localhost:8888/callback?code=AUTH_CODE&state={state}");

        var result = SignInFlow.ParseCallback(uri, state);

        var success = Assert.IsType<CallbackResult.Success>(result);
        Assert.Equal("AUTH_CODE", success.Code);
    }

    [Fact]
    public void ParseCallback_ReturnsDeclined_WhenErrorPresent()
    {
        var uri = new Uri("http://localhost:8888/callback?error=access_denied&state=abc123");

        var result = SignInFlow.ParseCallback(uri, "abc123");

        var declined = Assert.IsType<CallbackResult.Declined>(result);
        Assert.Equal("access_denied", declined.ErrorCode);
    }

    [Fact]
    public void ParseCallback_ReturnsDeclined_EvenWithCode_WhenErrorPresent()
    {
        var uri = new Uri("http://localhost:8888/callback?error=access_denied&code=SOME_CODE&state=abc");

        var result = SignInFlow.ParseCallback(uri, "abc");

        Assert.IsType<CallbackResult.Declined>(result);
    }

    [Fact]
    public void ParseCallback_ReturnsStale_WhenStateMismatch()
    {
        var uri = new Uri("http://localhost:8888/callback?code=SOME_CODE&state=WRONG_STATE");

        var result = SignInFlow.ParseCallback(uri, "EXPECTED_STATE");

        Assert.IsType<CallbackResult.Stale>(result);
    }

    [Fact]
    public void ParseCallback_ReturnsStale_WhenNoQueryParams()
    {
        var uri = new Uri("http://localhost:8888/callback");

        var result = SignInFlow.ParseCallback(uri, "anything");

        Assert.IsType<CallbackResult.Stale>(result);
    }

    [Fact]
    public void ParseCallback_ReturnsStale_WhenStateMissing()
    {
        var uri = new Uri("http://localhost:8888/callback?code=SOME_CODE");

        var result = SignInFlow.ParseCallback(uri, "expected");

        Assert.IsType<CallbackResult.Stale>(result);
    }

    // ─── ExchangeCodeAsync ────────────────────────────────────────────────────

    [Fact]
    public async Task ExchangeCode_ReturnsAccessToken_OnSuccess()
    {
        const string json = """{"access_token":"OBTAINED_TOKEN","token_type":"bearer","merchant_id":"M123"}""";
        using var handler = StubHandler.Returning(HttpStatusCode.OK, json);
        var client = MakeClient(handler);

        var token = await SignInFlow.ExchangeCodeAsync(client, MakeSettings(), "AUTH_CODE", default);

        Assert.Equal("OBTAINED_TOKEN", token);
    }

    [Fact]
    public async Task ExchangeCode_SendsCorrectGrantType()
    {
        const string json = """{"access_token":"TOKEN","token_type":"bearer"}""";
        using var handler = StubHandler.Returning(HttpStatusCode.OK, json);
        var client = MakeClient(handler);

        await SignInFlow.ExchangeCodeAsync(client, MakeSettings(), "MY_CODE", default);

        Assert.Contains("\"authorization_code\"", handler.LastBody);
    }

    [Fact]
    public async Task ExchangeCode_ThrowsApiException_OnSquareError()
    {
        const string json = """{"errors":[{"category":"AUTHENTICATION_ERROR","code":"UNAUTHORIZED"}]}""";
        using var handler = StubHandler.Returning(HttpStatusCode.Unauthorized, json);
        var client = MakeClient(handler);

        await Assert.ThrowsAsync<ApiException<RawError>>(
            () => SignInFlow.ExchangeCodeAsync(client, MakeSettings(), "BAD_CODE", default));
    }

    [Fact]
    public async Task ExchangeCode_ThrowsSquareApiException_WhenNoTokenInResponse()
    {
        const string json = """{"merchant_id":"M123"}""";
        using var handler = StubHandler.Returning(HttpStatusCode.OK, json);
        var client = MakeClient(handler);

        await Assert.ThrowsAsync<SquareApiException>(
            () => SignInFlow.ExchangeCodeAsync(client, MakeSettings(), "CODE", default));
    }

    // ─── GetMerchantAsync ─────────────────────────────────────────────────────

    [Fact]
    public async Task GetMerchant_ReturnsMerchant_OnSuccess()
    {
        const string json = """
            {"merchant":[{"id":"M123","business_name":"Test Shop","country":"US"}]}
            """;
        using var handler = StubHandler.Returning(HttpStatusCode.OK, json);
        var client = MakeClient(handler, "my-access-token");

        var merchant = await SignInFlow.GetMerchantAsync(client, default);

        Assert.Equal("M123", merchant.Id);
        Assert.Equal("Test Shop", merchant.BusinessName);
    }

    [Fact]
    public async Task GetMerchant_ThrowsSquareApiException_WhenEmptyMerchantList()
    {
        const string json = """{"merchant":[]}""";
        using var handler = StubHandler.Returning(HttpStatusCode.OK, json);
        var client = MakeClient(handler, "token");

        await Assert.ThrowsAsync<SquareApiException>(
            () => SignInFlow.GetMerchantAsync(client, default));
    }

    [Fact]
    public async Task GetMerchant_ThrowsSquareApiException_WhenNullMerchantList()
    {
        const string json = """{}""";
        using var handler = StubHandler.Returning(HttpStatusCode.OK, json);
        var client = MakeClient(handler, "token");

        await Assert.ThrowsAsync<SquareApiException>(
            () => SignInFlow.GetMerchantAsync(client, default));
    }

    [Fact]
    public async Task GetMerchant_ThrowsApiException_OnSquareError()
    {
        const string json = """{"errors":[{"category":"AUTHENTICATION_ERROR","code":"UNAUTHORIZED"}]}""";
        using var handler = StubHandler.Returning(HttpStatusCode.Unauthorized, json);
        var client = MakeClient(handler, "bad-token");

        await Assert.ThrowsAsync<ApiException<RawError>>(
            () => SignInFlow.GetMerchantAsync(client, default));
    }

    // ─── Ctrl+C propagation ───────────────────────────────────────────────────

    [Fact]
    public async Task SignInAsync_ThrowsOperationCanceled_OnCtrlC()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var flow = new SignInFlow(timeout: TimeSpan.FromMilliseconds(50));

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => flow.SignInAsync(MakeSettings(), cts.Token));
    }
}
