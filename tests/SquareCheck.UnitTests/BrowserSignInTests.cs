using Microsoft.eShopWeb.SquareCheck.SignIn;
using Microsoft.eShopWeb.SquareCheck.UnitTests.TestDoubles;
using Xunit;

namespace Microsoft.eShopWeb.SquareCheck.UnitTests;

public sealed class BrowserSignInTests
{
    private const string State = "this-runs-state-0123456789abcdef";

    [Fact]
    public async Task OpensTheSignInPageOnlyOnce_EvenIfAskedAgain()
    {
        await using var listener = await OAuthCallbackListener.StartAsync(new Uri("http://127.0.0.1:0/cb"), State, default);
        using var browser = new FakeBrowser();
        browser.CallbackUri.SetResult(listener.CallbackUri);
        browser.OnOpen = (b, _) => b.VisitCallbackAsync(("code", "the-code"), ("state", State));
        var codeReceived = 0;
        var signIn = new BrowserSignIn(listener, browser, TextWriter.Null, TimeSpan.FromSeconds(20), () => codeReceived++);

        var code = await signIn.PromptAsync("https://connect.squareupsandbox.com/oauth2/authorize?state=" + State, default);
        listener.Complete(BrowserPage.Connected("Biz"));

        Assert.Equal("the-code", code);
        Assert.Equal(1, codeReceived);
        await Assert.ThrowsAsync<SignInAlreadyAttemptedException>(
            () => signIn.PromptAsync("https://connect.squareupsandbox.com/oauth2/authorize?state=" + State, default));
        Assert.Single(browser.OpenedUrls);
        Assert.True(signIn.Attempted);
    }

    [Fact]
    public async Task PrintsTheSignInAddress_InCaseTheBrowserDoesNotOpen()
    {
        await using var listener = await OAuthCallbackListener.StartAsync(new Uri("http://127.0.0.1:0/cb"), State, default);
        var output = new StringWriter();
        var signIn = new BrowserSignIn(listener, new NoBrowser(), output, TimeSpan.FromMilliseconds(100));

        await Assert.ThrowsAsync<SignInTimedOutException>(
            () => signIn.PromptAsync("https://connect.squareupsandbox.com/oauth2/authorize?x=1", default));

        Assert.Contains("Could not open a browser automatically.", output.ToString());
        Assert.Contains("https://connect.squareupsandbox.com/oauth2/authorize?x=1", output.ToString());
    }

    [Theory]
    [InlineData(300, "5 minutes")]
    [InlineData(60, "1 minute")]
    [InlineData(30, "30 seconds")]
    public void DescribesDurationsPlainly(int seconds, string expected)
    {
        Assert.Equal(expected, BrowserSignIn.Describe(TimeSpan.FromSeconds(seconds)));
    }

    private sealed class NoBrowser : IBrowserLauncher
    {
        public bool TryOpen(string url) => false;
    }
}
