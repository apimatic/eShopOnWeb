using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.Infrastructure.SquareIntegration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PublicApiIntegrationTests.SquareIntegration;

[TestClass]
public class SquareOAuthEndpointTests
{
    private static async Task<string> StartConnectAsync(SquareApiFactory app)
    {
        var response = await app.AdminClient().GetAsync("api/square/connect");
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(SquareApiFactory.Json);
        var url = new Uri(body.Str("signInUrl")!);
        return QueryHelpers.ParseQuery(url.Query)["state"].ToString();
    }

    private static async Task<JsonElement> ConnectionAsync(SquareApiFactory app)
    {
        var response = await app.AdminClient().GetAsync("api/square/connection");
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<JsonElement>(SquareApiFactory.Json);
    }

    [TestMethod]
    public async Task ConnectReturnsSandboxSignInUrlWithScopesRedirectAndState()
    {
        await using var app = new SquareApiFactory();
        var response = await app.AdminClient().GetAsync("api/square/connect");
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(SquareApiFactory.Json);

        var url = new Uri(body.Str("signInUrl")!);
        var query = QueryHelpers.ParseQuery(url.Query);
        Assert.AreEqual("connect.squareupsandbox.com", url.Host);
        Assert.AreEqual("/oauth2/authorize", url.AbsolutePath);
        Assert.AreEqual(SquareApiFactory.ApplicationId, query["client_id"].ToString());
        Assert.AreEqual("code", query["response_type"].ToString());
        Assert.AreEqual("MERCHANT_PROFILE_READ ITEMS_READ ITEMS_WRITE ORDERS_READ ORDERS_WRITE", query["scope"].ToString());
        Assert.AreEqual(SquareApiFactory.RedirectUri, query["redirect_uri"].ToString());
        Assert.IsTrue(query["state"].ToString().Length >= 32);
        Assert.IsFalse(url.Query.Contains(SquareApiFactory.ApplicationSecret), "the secret must never be in the sign-in URL");
        Assert.AreEqual(0, app.Square.Requests.Count, "starting sign-in does not call Square");
    }

    [TestMethod]
    public async Task OperatorEndpointsRequireTheAdministratorRole()
    {
        await using var app = new SquareApiFactory();
        var shopper = app.ShopperClient();
        var anonymous = app.CreateClient();

        Assert.AreEqual(HttpStatusCode.Forbidden, (await shopper.GetAsync("api/square/connect")).StatusCode);
        Assert.AreEqual(HttpStatusCode.Forbidden, (await shopper.GetAsync("api/square/connection")).StatusCode);
        Assert.AreEqual(HttpStatusCode.Forbidden, (await shopper.PostAsync("api/square/catalog/sync", null)).StatusCode);
        Assert.AreEqual(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("api/square/connect")).StatusCode);
        Assert.AreEqual(HttpStatusCode.Unauthorized, (await anonymous.PostAsync("api/square/catalog/sync", null)).StatusCode);
    }

    [TestMethod]
    public async Task UsesTheConfiguredAccessTokenWhileNoMerchantHasConnected()
    {
        await using var app = new SquareApiFactory();
        var connection = await ConnectionAsync(app);

        Assert.IsTrue(connection.GetProperty("connected").GetBoolean());
        Assert.AreEqual("configured-access-token", connection.Str("connectedVia"));
        Assert.AreEqual(FakeSquare.MerchantId, connection.Str("merchantId"));
        Assert.AreEqual(FakeSquare.BusinessName, connection.Str("businessName"));
        Assert.AreEqual("Bearer " + FakeSquare.ConfiguredAccessToken, app.Square.RequestsTo("GET", "/v2/merchants/me").Single().Authorization);
        Assert.AreEqual("connect.squareupsandbox.com", app.Square.Requests.First().Host);
    }

    [TestMethod]
    public async Task ReportsNotConnectedWithoutConnectionOrAccessToken()
    {
        await using var app = new SquareApiFactory(withAccessToken: false);
        var connection = await ConnectionAsync(app);

        Assert.IsFalse(connection.GetProperty("connected").GetBoolean());
        Assert.IsNull(connection.Str("merchantId"));
        Assert.AreEqual(0, app.Square.Requests.Count);
    }

    [TestMethod]
    public async Task CallbackWithValidStateConnectsTheMerchantAndActsForItFromThenOn()
    {
        await using var app = new SquareApiFactory();
        var state = await StartConnectAsync(app);

        var callback = await app.CreateClient().GetAsync($"api/square/callback?code=code-abc&state={Uri.EscapeDataString(state)}");
        Assert.AreEqual(HttpStatusCode.OK, callback.StatusCode, await callback.Content.ReadAsStringAsync());
        var connected = await callback.Content.ReadFromJsonAsync<JsonElement>(SquareApiFactory.Json);
        Assert.AreEqual("sign-in", connected.Str("connectedVia"));
        Assert.AreEqual(FakeSquare.OAuthMerchantId, connected.Str("merchantId"));
        Assert.AreEqual(FakeSquare.OAuthBusinessName, connected.Str("businessName"));

        var exchange = app.Square.RequestsTo("POST", "/oauth2/token").Single();
        Assert.AreEqual("authorization_code", (string?)exchange.Json!["grant_type"]);
        Assert.AreEqual("code-abc", (string?)exchange.Json["code"]);
        Assert.AreEqual(SquareApiFactory.RedirectUri, (string?)exchange.Json["redirect_uri"]);

        // From then on the app acts for the connected merchant (its token, not the configured one).
        var connection = await ConnectionAsync(app);
        Assert.AreEqual(FakeSquare.OAuthMerchantId, connection.Str("merchantId"));
        Assert.IsTrue(app.Square.RequestsTo("GET", "/v2/merchants/me").Last().Authorization!.StartsWith("Bearer oauth-access-"));

        // Tokens are stored encrypted, never in clear text.
        var stored = await app.WithDbAsync(db => db.SquareMerchantConnections.SingleAsync());
        Assert.IsFalse(stored.ProtectedAccessToken.StartsWith("oauth-access-"));
        Assert.IsFalse(stored.ProtectedRefreshToken!.StartsWith("refresh-"));
    }

    [TestMethod]
    public async Task CallbackTheShopDidNotStartIsRefusedAndDoesNotChangeTheConnection()
    {
        await using var app = new SquareApiFactory();
        await StartConnectAsync(app);

        var forged = await app.CreateClient().GetAsync("api/square/callback?code=code-evil&state=not-a-state-we-issued");
        var missing = await app.CreateClient().GetAsync("api/square/callback?code=code-evil");

        Assert.AreEqual(HttpStatusCode.BadRequest, forged.StatusCode);
        Assert.AreEqual(HttpStatusCode.BadRequest, missing.StatusCode);
        Assert.AreEqual(0, app.Square.RequestsTo("POST", "/oauth2/token").Count, "no code exchange for a forged callback");
        Assert.AreEqual(0, await app.WithDbAsync(db => db.SquareMerchantConnections.CountAsync()));
        Assert.AreEqual(FakeSquare.MerchantId, (await ConnectionAsync(app)).Str("merchantId"));
    }

    [TestMethod]
    public async Task StateCanOnlyBeUsedOnce()
    {
        await using var app = new SquareApiFactory();
        var state = await StartConnectAsync(app);
        var first = await app.CreateClient().GetAsync($"api/square/callback?code=code-1&state={Uri.EscapeDataString(state)}");
        var replay = await app.CreateClient().GetAsync($"api/square/callback?code=code-2&state={Uri.EscapeDataString(state)}");

        Assert.AreEqual(HttpStatusCode.OK, first.StatusCode);
        Assert.AreEqual(HttpStatusCode.BadRequest, replay.StatusCode);
        Assert.AreEqual(1, app.Square.RequestsTo("POST", "/oauth2/token").Count);
    }

    [TestMethod]
    public async Task DeniedApprovalIsRefusedAndLeavesConnectionUnchanged()
    {
        await using var app = new SquareApiFactory();
        var state = await StartConnectAsync(app);
        var response = await app.CreateClient().GetAsync($"api/square/callback?error=access_denied&error_description=user_denied&state={Uri.EscapeDataString(state)}");

        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.AreEqual(0, app.Square.RequestsTo("POST", "/oauth2/token").Count);
        Assert.AreEqual(0, await app.WithDbAsync(db => db.SquareMerchantConnections.CountAsync()));
    }

    [TestMethod]
    public async Task CallbackExchangeConnectionFailureLeavesConnectionUnchanged()
    {
        await using var app = new SquareApiFactory();
        var state = await StartConnectAsync(app);
        app.Square.Fail("POST", "/oauth2/token", FakeSquare.FaultKind.DropAfterProcessing);

        var response = await app.CreateClient().GetAsync($"api/square/callback?code=code-abc&state={Uri.EscapeDataString(state)}");

        Assert.AreEqual(HttpStatusCode.GatewayTimeout, response.StatusCode);
        Assert.AreEqual(1, app.Square.RequestsTo("POST", "/oauth2/token").Count, "single-use code is not re-sent");
        Assert.AreEqual(0, await app.WithDbAsync(db => db.SquareMerchantConnections.CountAsync()));
        Assert.AreEqual(FakeSquare.MerchantId, (await ConnectionAsync(app)).Str("merchantId"));
    }

    [TestMethod]
    public async Task AccessThatIsRunningOutIsRefreshedWithoutSigningInAgain()
    {
        await using var app = new SquareApiFactory();
        app.Square.IssuedAccessTokenLifetime = TimeSpan.FromHours(2); // inside the refresh window
        var state = await StartConnectAsync(app);
        (await app.CreateClient().GetAsync($"api/square/callback?code=code-abc&state={Uri.EscapeDataString(state)}")).EnsureSuccessStatusCode();
        var firstAccess = app.Square.RequestsTo("GET", "/v2/merchants/me").Last().Authorization;

        var connection = await ConnectionAsync(app);

        Assert.AreEqual(FakeSquare.OAuthMerchantId, connection.Str("merchantId"));
        var refresh = app.Square.RequestsTo("POST", "/oauth2/token").Last();
        Assert.AreEqual("refresh_token", (string?)refresh.Json!["grant_type"]);
        Assert.IsTrue(((string?)refresh.Json["refresh_token"])!.StartsWith("refresh-"));
        var stored = await app.WithDbAsync(db => db.SquareMerchantConnections.SingleAsync());
        Assert.IsTrue(stored.AccessTokenExpiresAt > DateTimeOffset.UtcNow.AddDays(20), "refreshed expiry is persisted");
        Assert.IsNotNull(firstAccess);
    }

    /// <summary>What a restart does to the SDK's in-memory token cache.</summary>
    private static void DropCachedSquareClient(SquareApiFactory app) =>
        app.Services.GetRequiredService<SquareClientHolder>().Reset();

    private static async Task ConnectThroughSignInAsync(SquareApiFactory app)
    {
        var state = await StartConnectAsync(app);
        (await app.CreateClient().GetAsync($"api/square/callback?code=code-abc&state={Uri.EscapeDataString(state)}")).EnsureSuccessStatusCode();
    }

    [TestMethod]
    public async Task ExpiredAccessIsRefreshedBeforeTheNextCall()
    {
        await using var app = new SquareApiFactory();
        await ConnectThroughSignInAsync(app);
        await app.WithDbAsync(async db =>
        {
            var connection = await db.SquareMerchantConnections.SingleAsync();
            connection.AccessTokenExpiresAt = DateTimeOffset.UtcNow.AddMinutes(-5);
            await db.SaveChangesAsync();
        });
        DropCachedSquareClient(app);
        var tokenCallsBefore = app.Square.RequestsTo("POST", "/oauth2/token").Count;

        var connection = await ConnectionAsync(app);

        Assert.IsTrue(connection.GetProperty("connected").GetBoolean());
        Assert.AreEqual(FakeSquare.OAuthMerchantId, connection.Str("merchantId"));
        var tokenCalls = app.Square.RequestsTo("POST", "/oauth2/token");
        Assert.AreEqual(tokenCallsBefore + 1, tokenCalls.Count);
        Assert.AreEqual("refresh_token", (string?)tokenCalls.Last().Json!["grant_type"]);
        var stored = await app.WithDbAsync(db => db.SquareMerchantConnections.AsNoTracking().SingleAsync());
        Assert.IsTrue(stored.AccessTokenExpiresAt > DateTimeOffset.UtcNow.AddDays(20));
    }

    [TestMethod]
    public async Task RefreshConnectionFailureKeepsStoredToken()
    {
        await using var app = new SquareApiFactory();
        app.Square.IssuedAccessTokenLifetime = TimeSpan.FromHours(2);
        app.Square.RefreshedAccessTokenLifetime = TimeSpan.FromHours(2); // stays inside the refresh window
        await ConnectThroughSignInAsync(app);
        var before = await app.WithDbAsync(db => db.SquareMerchantConnections.AsNoTracking().SingleAsync());

        app.Square.Fail("POST", "/oauth2/token", FakeSquare.FaultKind.DropBeforeProcessing, times: 10);
        DropCachedSquareClient(app);
        var connection = await ConnectionAsync(app);

        Assert.IsTrue(connection.GetProperty("connected").GetBoolean(), "the still-valid stored token keeps working");
        Assert.AreEqual(FakeSquare.OAuthMerchantId, connection.Str("merchantId"));
        var after = await app.WithDbAsync(db => db.SquareMerchantConnections.AsNoTracking().SingleAsync());
        Assert.AreEqual(before.ProtectedAccessToken, after.ProtectedAccessToken);
        Assert.AreEqual(before.ProtectedRefreshToken, after.ProtectedRefreshToken);
        Assert.AreEqual(before.AccessTokenExpiresAt, after.AccessTokenExpiresAt);
    }

    [TestMethod]
    public async Task RevokedAuthorizationAfterExpiryReportsNotConnected()
    {
        await using var app = new SquareApiFactory();
        await ConnectThroughSignInAsync(app);
        await app.WithDbAsync(async db =>
        {
            var connection = await db.SquareMerchantConnections.SingleAsync();
            connection.AccessTokenExpiresAt = DateTimeOffset.UtcNow.AddMinutes(-5);
            await db.SaveChangesAsync();
        });
        app.Square.RevokeRefreshTokens();
        DropCachedSquareClient(app);

        var connection = await ConnectionAsync(app);

        Assert.IsFalse(connection.GetProperty("connected").GetBoolean());
        Assert.AreEqual("sign-in", connection.Str("connectedVia"));
        StringAssert.Contains(connection.Str("problem"), "reconnect");
    }
}
