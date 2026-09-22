using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PublicApiIntegrationTests.NotificationEndpoints;

/// <summary>
/// Operator (admin-only) endpoints must reject a normal shopper token. These make no live Twilio
/// call — authorization is checked before any handler work.
/// </summary>
[TestClass]
public class OperatorEndpointsAuthTest
{
    private static HttpClient NormalUserClient()
    {
        var client = ProgramTest.NewClient;
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", ApiTokenHelper.GetNormalUserToken());
        return client;
    }

    [TestMethod]
    public async Task Dispatch_ForbiddenForNormalUser()
    {
        var response = await NormalUserClient().PostAsync("api/orders/1/dispatch", null);
        Assert.AreEqual(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [TestMethod]
    public async Task Cancel_ForbiddenForNormalUser()
    {
        var response = await NormalUserClient().PostAsync("api/orders/1/cancel", null);
        Assert.AreEqual(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [TestMethod]
    public async Task Resend_ForbiddenForNormalUser()
    {
        var content = new StringContent("{\"idempotencyKey\":\"k\"}", Encoding.UTF8, "application/json");
        var response = await NormalUserClient().PostAsync("api/notifications/1/resend", content);
        Assert.AreEqual(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [TestMethod]
    public async Task DisposeContent_ForbiddenForNormalUser()
    {
        var response = await NormalUserClient().DeleteAsync("api/notifications/1/content");
        Assert.AreEqual(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [TestMethod]
    public async Task Reconciliation_ForbiddenForNormalUser()
    {
        var response = await NormalUserClient().GetAsync(
            "api/notifications/reconciliation?from=2020-01-01T00:00:00Z&to=2020-01-02T00:00:00Z");
        Assert.AreEqual(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [TestMethod]
    public async Task ContactNumbers_RequireAuthentication()
    {
        var response = await ProgramTest.NewClient.GetAsync("api/contact-numbers");
        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
