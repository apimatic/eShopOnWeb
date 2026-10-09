using SquareCheck;

namespace SquareCheckTests;

public class OAuthFlowCoordinatorTests
{
    [Fact]
    public async Task Returns_code_when_valid_redirect_arrives()
    {
        const string state = "abc123";
        var listener = new FakeRedirectListener(new OAuthCallback("auth-code", null, state));
        var coordinator = new OAuthFlowCoordinator(listener, new FakeProcessLauncher(), state);

        var code = await coordinator.CreatePromptCallback(CancellationToken.None)(
            "https://example.com/auth", CancellationToken.None);

        Assert.Equal("auth-code", code);
    }

    [Fact]
    public async Task Opens_browser_only_once_when_callback_invoked_multiple_times()
    {
        const string state = "abc123";
        var listener = new FakeRedirectListener(
            new OAuthCallback("code1", null, state),
            new OAuthCallback("code2", null, state));
        var launcher = new FakeProcessLauncher();
        var coordinator = new OAuthFlowCoordinator(listener, launcher, state);

        var cb = coordinator.CreatePromptCallback(CancellationToken.None);
        await cb("https://example.com/auth?v=1", CancellationToken.None);
        await cb("https://example.com/auth?v=2", CancellationToken.None);

        Assert.Single(launcher.OpenedUrls);
    }

    [Fact]
    public async Task Rejects_stale_tab_with_400_then_accepts_valid_redirect()
    {
        const string state = "abc123";
        var listener = new FakeRedirectListener(
            new OAuthCallback("old-code", null, "stale-state"), // stale tab
            new OAuthCallback("real-code", null, state));        // real redirect
        var coordinator = new OAuthFlowCoordinator(listener, new FakeProcessLauncher(), state);

        var code = await coordinator.CreatePromptCallback(CancellationToken.None)(
            "https://example.com/auth", CancellationToken.None);

        Assert.Equal("real-code", code);
        // Stale tab received an error response before looping.
        Assert.Single(listener.SentResponses);
        Assert.Equal(400, listener.SentResponses[0].StatusCode);
    }

    [Fact]
    public async Task Sets_IsDeclined_and_throws_when_Square_returns_access_denied()
    {
        const string state = "abc123";
        var listener = new FakeRedirectListener(new OAuthCallback(null, "access_denied", state));
        var coordinator = new OAuthFlowCoordinator(listener, new FakeProcessLauncher(), state);

        await Assert.ThrowsAnyAsync<Exception>(() =>
            coordinator.CreatePromptCallback(CancellationToken.None)(
                "https://example.com/auth", CancellationToken.None));

        Assert.True(coordinator.IsDeclined);
        Assert.Single(listener.SentResponses);
        Assert.Equal(400, listener.SentResponses[0].StatusCode);
    }

    [Fact]
    public async Task Sets_IsTimedOut_when_internal_timeout_fires()
    {
        const string state = "abc123";
        var listener = new FakeRedirectListener(); // no responses — blocks until cancelled
        var coordinator = new OAuthFlowCoordinator(listener, new FakeProcessLauncher(), state,
            signInTimeout: TimeSpan.FromMilliseconds(30));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            coordinator.CreatePromptCallback(CancellationToken.None)(
                "https://example.com/auth", CancellationToken.None));

        Assert.True(coordinator.IsTimedOut);
        Assert.False(coordinator.IsDeclined);
    }

    [Fact]
    public async Task Does_not_set_IsTimedOut_when_app_token_is_cancelled()
    {
        const string state = "abc123";
        var listener = new FakeRedirectListener(); // no responses
        var coordinator = new OAuthFlowCoordinator(listener, new FakeProcessLauncher(), state,
            signInTimeout: TimeSpan.FromMinutes(5)); // long timeout — won't fire

        using var appCts = new CancellationTokenSource(TimeSpan.FromMilliseconds(30));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            coordinator.CreatePromptCallback(appCts.Token)(
                "https://example.com/auth", CancellationToken.None));

        Assert.False(coordinator.IsTimedOut);
    }

    [Fact]
    public async Task ServeSuccessAsync_sends_200_containing_business_name()
    {
        const string state = "abc123";
        var listener = new FakeRedirectListener(new OAuthCallback("code", null, state));
        var coordinator = new OAuthFlowCoordinator(listener, new FakeProcessLauncher(), state);

        await coordinator.CreatePromptCallback(CancellationToken.None)(
            "https://example.com/auth", CancellationToken.None);

        await coordinator.ServeSuccessAsync("Acme Bakery", CancellationToken.None);

        Assert.Single(listener.SentResponses);
        Assert.Equal(200, listener.SentResponses[0].StatusCode);
        Assert.Contains("Acme Bakery", listener.SentResponses[0].Body);
    }
}
