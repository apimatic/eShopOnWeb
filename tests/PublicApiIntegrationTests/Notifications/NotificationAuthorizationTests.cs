using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PublicApiIntegrationTests.Notifications;

[TestClass]
public class NotificationAuthorizationTests
{
    [TestMethod]
    public async Task ShopperRoutesRequireJwt()
    {
        var response = await ProgramTest.NewClient.GetAsync("api/contact-numbers");
        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [TestMethod]
    public async Task OperatorRoutesRejectShopperRole()
    {
        var client = ProgramTest.NewClient;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ApiTokenHelper.GetNormalUserToken());

        var dispatch = await client.PostAsync("api/orders/1/dispatch", new StringContent(string.Empty));
        var reconciliation = await client.GetAsync("api/notifications/reconciliation?from=2026-01-01T00:00:00Z&to=2026-01-02T00:00:00Z");

        Assert.AreEqual(HttpStatusCode.Forbidden, dispatch.StatusCode);
        Assert.AreEqual(HttpStatusCode.Forbidden, reconciliation.StatusCode);
    }
}
