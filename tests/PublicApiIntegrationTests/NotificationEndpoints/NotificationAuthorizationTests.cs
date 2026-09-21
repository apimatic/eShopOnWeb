using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;

namespace PublicApiIntegrationTests.NotificationEndpoints;

/// <summary>
/// Confirms the operator endpoints are restricted to the administrator role and that shopper endpoints
/// require authentication. These paths reject before any provider call, so they make no network request.
/// </summary>
[TestClass]
public class NotificationAuthorizationTests
{
    private static StringContent EmptyJson() => new("{}", Encoding.UTF8, "application/json");

    private static HttpClient AnonymousClient() => ProgramTest.NewClient;

    private static HttpClient ClientWithToken(string token)
    {
        var client = ProgramTest.NewClient;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    [TestMethod]
    public async Task DispatchReturnsUnauthorizedWithoutToken()
    {
        var response = await AnonymousClient().PostAsync("api/orders/1/dispatch", EmptyJson());
        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [TestMethod]
    public async Task DispatchReturnsForbiddenForNormalUser()
    {
        var client = ClientWithToken(ApiTokenHelper.GetNormalUserToken());
        var response = await client.PostAsync("api/orders/1/dispatch", EmptyJson());
        Assert.AreEqual(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [TestMethod]
    public async Task CancelReturnsForbiddenForNormalUser()
    {
        var client = ClientWithToken(ApiTokenHelper.GetNormalUserToken());
        var response = await client.PostAsync("api/orders/1/cancel", EmptyJson());
        Assert.AreEqual(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [TestMethod]
    public async Task ResendReturnsForbiddenForNormalUser()
    {
        var client = ClientWithToken(ApiTokenHelper.GetNormalUserToken());
        var response = await client.PostAsync("api/notifications/1/resend",
            new StringContent("{\"idempotencyKey\":\"k\"}", Encoding.UTF8, "application/json"));
        Assert.AreEqual(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [TestMethod]
    public async Task DisposeContentReturnsForbiddenForNormalUser()
    {
        var client = ClientWithToken(ApiTokenHelper.GetNormalUserToken());
        var response = await client.DeleteAsync("api/notifications/1/content");
        Assert.AreEqual(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [TestMethod]
    public async Task ReconciliationReturnsForbiddenForNormalUser()
    {
        var client = ClientWithToken(ApiTokenHelper.GetNormalUserToken());
        var response = await client.GetAsync("api/notifications/reconciliation?from=2026-01-01T00:00:00Z&to=2026-02-01T00:00:00Z");
        Assert.AreEqual(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [TestMethod]
    public async Task MyOrdersRequiresAuthentication()
    {
        var response = await AnonymousClient().GetAsync("api/my-orders");
        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [TestMethod]
    public async Task ContactNumbersRequireAuthentication()
    {
        var response = await AnonymousClient().GetAsync("api/contact-numbers");
        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
