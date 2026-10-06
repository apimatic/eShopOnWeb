using System;
using System.Linq;
using System.Net.Http;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using System.Web;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.Infrastructure.SquareIntegration;
using Microsoft.eShopWeb.Infrastructure.SquareIntegration.Entities;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PublicApiIntegrationTests.Square;

[TestClass]
public class SquareOAuthServiceTests
{
    private static string StateOf(string signInUrl) => HttpUtility.ParseQueryString(new Uri(signInUrl).Query)["state"]!;

    [TestMethod]
    public async Task SignInUrlTargetsTheConfiguredEnvironmentWithScopesAndState()
    {
        await using var h = new SquareHarness();
        var signIn = await h.Run<SquareOAuthService, SquareSignIn>(s => s.BeginAsync("admin@microsoft.com", default));

        var url = new Uri(signIn.SignInUrl);
        var query = HttpUtility.ParseQueryString(url.Query);
        Assert.AreEqual("https://connect.squareupsandbox.com/oauth2/authorize", url.GetLeftPart(UriPartial.Path));
        Assert.AreEqual("code", query["response_type"]);
        Assert.AreEqual(SquareHarness.ApplicationId, query["client_id"]);
        Assert.AreEqual(SquareHarness.RedirectUri, query["redirect_uri"]);
        Assert.AreEqual("MERCHANT_PROFILE_READ ITEMS_READ ITEMS_WRITE ORDERS_READ ORDERS_WRITE", query["scope"]);
        Assert.IsTrue(query["state"]!.Length >= 32);
        Assert.IsFalse(signIn.SignInUrl.Contains(SquareHarness.ApplicationSecret));

        // Only a hash of the state is stored.
        var stored = await h.Db(db => db.SquareOAuthStates.SingleAsync());
        Assert.AreNotEqual(query["state"], stored.StateHash);
    }

    [TestMethod]
    public async Task ProductionEnvironmentUsesTheProductionSignInPage()
    {
        await using var h = new SquareHarness(s => s["Square:Environment"] = "production");
        var signIn = await h.Run<SquareOAuthService, SquareSignIn>(s => s.BeginAsync("admin", default));
        StringAssert.StartsWith(signIn.SignInUrl, "https://connect.squareup.com/oauth2/authorize?");
    }

    [TestMethod]
    public async Task CallbackStoresEncryptedTokensAndTheAppThenActsForTheConnectedMerchant()
    {
        await using var h = new SquareHarness();
        var signIn = await h.Run<SquareOAuthService, SquareSignIn>(s => s.BeginAsync("admin", default));

        var result = await h.Run<SquareOAuthService, SquareCallbackResult>(s => s.CompleteAsync(StateOf(signIn.SignInUrl), "auth-code-1", null, default));

        Assert.AreEqual(SquareCallbackOutcome.Connected, result.Outcome);
        Assert.AreEqual(h.Square.MerchantId, result.MerchantId);
        Assert.AreEqual(h.Square.BusinessName, result.BusinessName);

        var exchange = h.Square.RequestsTo(HttpMethod.Post, "/oauth2/token").Single().Json!;
        Assert.AreEqual("authorization_code", (string?)exchange["grant_type"]);
        Assert.AreEqual("auth-code-1", (string?)exchange["code"]);
        Assert.AreEqual(SquareHarness.ApplicationId, (string?)exchange["client_id"]);
        Assert.AreEqual(SquareHarness.ApplicationSecret, (string?)exchange["client_secret"]);
        Assert.AreEqual(SquareHarness.RedirectUri, (string?)exchange["redirect_uri"]);

        var connection = await h.Db(db => db.SquareConnections.SingleAsync());
        Assert.AreEqual(h.Square.MerchantId, connection.MerchantId);
        Assert.IsFalse(connection.ProtectedAccessToken.Contains("oauth-access"), "tokens must be stored encrypted");
        Assert.IsNotNull(connection.ProtectedRefreshToken);
        Assert.IsFalse(connection.ProtectedRefreshToken!.Contains("oauth-refresh"));

        // From now on calls go out with the merchant's OAuth token, not the configured one.
        var merchant = await h.Services.GetRequiredService<SquareMerchantContextProvider>().GetAsync(default, refresh: true);
        Assert.AreEqual(SquareCredentialSource.OAuthConnection, merchant.Source);
        StringAssert.StartsWith(h.Square.Snapshot().Last().Authorization, "Bearer oauth-access-");
    }

    [TestMethod]
    public async Task CallbackWithAStateTheShopDidNotIssueIsRefusedAndChangesNothing()
    {
        await using var h = new SquareHarness();
        await h.Run<SquareOAuthService, SquareSignIn>(s => s.BeginAsync("admin", default));

        foreach (var forged in new[] { "forged-state", "", null })
        {
            var result = await h.Run<SquareOAuthService, SquareCallbackResult>(s => s.CompleteAsync(forged, "attacker-code", null, default));
            Assert.AreEqual(SquareCallbackOutcome.Refused, result.Outcome);
        }

        Assert.AreEqual(0, h.Square.RequestsTo(HttpMethod.Post, "/oauth2/token").Count(), "no code may be exchanged");
        Assert.AreEqual(0, await h.Db(db => db.SquareConnections.CountAsync()));
    }

    [TestMethod]
    public async Task AStateWorksOnlyOnce()
    {
        await using var h = new SquareHarness();
        var signIn = await h.Run<SquareOAuthService, SquareSignIn>(s => s.BeginAsync("admin", default));
        var state = StateOf(signIn.SignInUrl);

        var first = await h.Run<SquareOAuthService, SquareCallbackResult>(s => s.CompleteAsync(state, "code-1", null, default));
        var connectedWith = (await h.Db(db => db.SquareConnections.SingleAsync())).ProtectedAccessToken;
        h.Square.MerchantId = "OTHER_MERCHANT";
        var replay = await h.Run<SquareOAuthService, SquareCallbackResult>(s => s.CompleteAsync(state, "code-2", null, default));

        Assert.AreEqual(SquareCallbackOutcome.Connected, first.Outcome);
        Assert.AreEqual(SquareCallbackOutcome.Refused, replay.Outcome);
        Assert.AreEqual(1, h.Square.RequestsTo(HttpMethod.Post, "/oauth2/token").Count());
        var connection = await h.Db(db => db.SquareConnections.SingleAsync());
        Assert.AreEqual(connectedWith, connection.ProtectedAccessToken);
        Assert.AreEqual("MERCHANT_1", connection.MerchantId);
    }

    [TestMethod]
    public async Task AnExpiredStateIsRefused()
    {
        await using var h = new SquareHarness();
        var signIn = await h.Run<SquareOAuthService, SquareSignIn>(s => s.BeginAsync("admin", default));
        h.Clock.Advance(SquareOAuthService.StateLifetime + TimeSpan.FromSeconds(1));

        var result = await h.Run<SquareOAuthService, SquareCallbackResult>(s => s.CompleteAsync(StateOf(signIn.SignInUrl), "code", null, default));

        Assert.AreEqual(SquareCallbackOutcome.Refused, result.Outcome);
        Assert.AreEqual(0, await h.Db(db => db.SquareConnections.CountAsync()));
    }

    [TestMethod]
    public async Task AMerchantWhoDeclinesLeavesTheConnectionUnchanged()
    {
        await using var h = new SquareHarness();
        var signIn = await h.Run<SquareOAuthService, SquareSignIn>(s => s.BeginAsync("admin", default));

        var result = await h.Run<SquareOAuthService, SquareCallbackResult>(s => s.CompleteAsync(StateOf(signIn.SignInUrl), null, "access_denied", default));

        Assert.AreEqual(SquareCallbackOutcome.Denied, result.Outcome);
        Assert.AreEqual(0, h.Square.RequestsTo(HttpMethod.Post, "/oauth2/token").Count());
        Assert.AreEqual(0, await h.Db(db => db.SquareConnections.CountAsync()));
    }

    [TestMethod]
    public async Task TokenExchangeConnectionFailureLeavesConnectionUnchanged()
    {
        await using var h = new SquareHarness();
        var signIn = await h.Run<SquareOAuthService, SquareSignIn>(s => s.BeginAsync("admin", default));
        h.Square.Intercept = r => r.Path == "/oauth2/token" ? throw new HttpRequestException("connection reset") : null;

        var result = await h.Run<SquareOAuthService, SquareCallbackResult>(s => s.CompleteAsync(StateOf(signIn.SignInUrl), "code", null, default));

        Assert.AreEqual(SquareCallbackOutcome.Failed, result.Outcome);
        Assert.AreEqual(1, h.Square.RequestsTo(HttpMethod.Post, "/oauth2/token").Count(), "a POST is never resent by the SDK");
        Assert.AreEqual(0, await h.Db(db => db.SquareConnections.CountAsync()));
    }

    [TestMethod]
    public async Task TheAppKeepsActingForTheMerchantAfterTheGrantedAccessRunsOut()
    {
        await using var h = new SquareHarness(s => s["Square:AccessToken"] = null);
        var signIn = await h.Run<SquareOAuthService, SquareSignIn>(s => s.BeginAsync("admin", default));
        await h.Run<SquareOAuthService, SquareCallbackResult>(s => s.CompleteAsync(StateOf(signIn.SignInUrl), "code", null, default));
        var tokens = h.Services.GetRequiredService<SquareAccessTokenProvider>();
        var first = await tokens.GetAsync(default);

        // 31 days later the 30-day access token has expired.
        h.Clock.Advance(TimeSpan.FromDays(31));
        var merchant = await h.Services.GetRequiredService<SquareMerchantContextProvider>().GetAsync(default, refresh: true);

        var refresh = h.Square.RequestsTo(HttpMethod.Post, "/oauth2/token").Last().Json!;
        Assert.AreEqual("refresh_token", (string?)refresh["grant_type"]);
        Assert.AreEqual("oauth-refresh-1", (string?)refresh["refresh_token"]);
        Assert.AreEqual(SquareHarness.ApplicationSecret, (string?)refresh["client_secret"]);
        Assert.AreEqual(h.Square.MerchantId, merchant.MerchantId);
        var used = h.Square.Snapshot().Last().Authorization;
        Assert.AreNotEqual("Bearer " + first.AccessToken, used);
        StringAssert.StartsWith(used, "Bearer oauth-access-");
    }

    [TestMethod]
    public async Task TokensAreRefreshedAWeekBeforeTheyExpire()
    {
        await using var h = new SquareHarness();
        var signIn = await h.Run<SquareOAuthService, SquareSignIn>(s => s.BeginAsync("admin", default));
        await h.Run<SquareOAuthService, SquareCallbackResult>(s => s.CompleteAsync(StateOf(signIn.SignInUrl), "code", null, default));
        var tokens = h.Services.GetRequiredService<SquareAccessTokenProvider>();

        h.Clock.Advance(TimeSpan.FromDays(10));
        await tokens.GetAsync(default);
        Assert.AreEqual(1, h.Square.RequestsTo(HttpMethod.Post, "/oauth2/token").Count(), "no refresh while far from expiry");

        h.Clock.Advance(TimeSpan.FromDays(14)); // 6 days left
        await tokens.GetAsync(default);
        Assert.AreEqual(2, h.Square.RequestsTo(HttpMethod.Post, "/oauth2/token").Count());
    }

    [TestMethod]
    public async Task WithoutSignInTheConfiguredAccessTokenIsUsed()
    {
        await using var h = new SquareHarness();
        var merchant = await h.Services.GetRequiredService<SquareMerchantContextProvider>().GetAsync(default);

        Assert.AreEqual(SquareCredentialSource.ConfiguredAccessToken, merchant.Source);
        Assert.AreEqual("LOCATION_1", merchant.LocationId, "the active main location, never the inactive one");
        Assert.IsTrue(h.Square.Snapshot().All(r => r.Authorization == "Bearer " + SquareHarness.ConfiguredToken));
    }

    [TestMethod]
    public async Task WithNeitherSignInNorTokenTheShopIsNotConnected()
    {
        await using var h = new SquareHarness(s => s["Square:AccessToken"] = null);
        var ex = await Assert.ThrowsExceptionAsync<SquareIntegrationException>(
            () => h.Services.GetRequiredService<SquareMerchantContextProvider>().GetAsync(default));
        Assert.AreEqual(SquareFailureKind.NotConnected, ex.Kind);
        Assert.AreEqual(0, h.Square.Snapshot().Count, "nothing is sent unauthenticated");
    }

    [TestMethod]
    public async Task ARejectedTokenIsReportedAsAnAuthorizationFailure()
    {
        await using var h = new SquareHarness();
        h.Square.Intercept = _ => FakeSquare.Error(System.Net.HttpStatusCode.Unauthorized, "UNAUTHORIZED", "AUTHENTICATION_ERROR");
        var ex = await Assert.ThrowsExceptionAsync<SquareIntegrationException>(
            () => h.Services.GetRequiredService<SquareMerchantContextProvider>().GetAsync(default));
        Assert.AreEqual(SquareFailureKind.AuthorizationFailed, ex.Kind);
        Assert.IsFalse(ex.Message.Contains(SquareHarness.ConfiguredToken));
    }
}
