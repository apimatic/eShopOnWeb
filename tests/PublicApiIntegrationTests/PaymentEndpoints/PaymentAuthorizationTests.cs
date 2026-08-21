using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PublicApiIntegrationTests.PaymentEndpoints;

[TestClass]
public class PaymentAuthorizationTests
{
    [TestMethod]
    public async Task Operator_endpoints_reject_a_non_admin()
    {
        using var factory = new PaymentApiFactory();
        var client = factory.CreateClient();
        client.UseToken(ApiTokenHelper.GetNormalUserToken());

        var (itemId, _) = await PaymentApi.GetFirstCatalogItemAsync(client);
        var orderId = await PaymentApi.CreateOrderAsync(client, itemId, 1);

        var fulfil = await client.PostAsync($"api/orders/{orderId}/fulfil", null);
        var cancel = await client.PostAsync($"api/orders/{orderId}/cancel", null);
        var reconcile = await client.GetAsync("api/reconciliation?from=2026-01-01T00:00:00Z&to=2026-02-01T00:00:00Z");

        Assert.AreEqual(HttpStatusCode.Forbidden, fulfil.StatusCode);
        Assert.AreEqual(HttpStatusCode.Forbidden, cancel.StatusCode);
        Assert.AreEqual(HttpStatusCode.Forbidden, reconcile.StatusCode);
    }

    [TestMethod]
    public async Task Endpoints_require_authentication()
    {
        using var factory = new PaymentApiFactory();
        var client = factory.CreateClient(); // no token

        Assert.AreEqual(HttpStatusCode.Unauthorized, (await client.GetAsync("api/my-orders")).StatusCode);
        Assert.AreEqual(HttpStatusCode.Unauthorized, (await client.GetAsync("api/payment-methods")).StatusCode);
    }

    [TestMethod]
    public async Task A_shopper_cannot_act_on_another_shoppers_order()
    {
        using var factory = new PaymentApiFactory();
        var owner = factory.CreateClient();
        owner.UseToken(ApiTokenHelper.GetNormalUserToken());
        var intruder = factory.CreateClient();
        intruder.UseToken(ApiTokenHelper.GetTokenFor("intruder@example.com"));

        var (itemId, _) = await PaymentApi.GetFirstCatalogItemAsync(owner);
        var orderId = await PaymentApi.CreateOrderAsync(owner, itemId, 1);

        // The intruder must not see or act on it.
        var pay = await PaymentApi.PayWithCardAsync(intruder, orderId);
        var refund = await intruder.PostAsJsonAsync($"api/orders/{orderId}/refunds", new { idempotencyKey = "x" });

        Assert.AreEqual(HttpStatusCode.NotFound, pay.StatusCode);
        Assert.AreEqual(HttpStatusCode.NotFound, refund.StatusCode);
        Assert.AreEqual(0, factory.Gateway.AuthorizeCalls);
    }
}
