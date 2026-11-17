using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.eShopWeb;
using Microsoft.eShopWeb.PublicApi.Payments;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PublicApiIntegrationTests.PaymentEndpoints;

[TestClass]
public class PaymentEndpointTests
{
    [TestMethod]
    public async Task CreateOrderRequiresAuthentication()
    {
        var client = ProgramTest.NewClient;
        var response = await client.PostAsync("api/orders", OrderContent(1, 1));
        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [TestMethod]
    public async Task CreateOrderReturnsOrderIdAndCatalogTotal()
    {
        var client = ProgramTest.NewClient;
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", ApiTokenHelper.GetNormalUserToken());

        var response = await client.PostAsync("api/orders", OrderContent(1, 2));
        response.EnsureSuccessStatusCode();

        var result = (await response.Content.ReadAsStringAsync()).FromJson<CreateOrderResult>();
        Assert.IsNotNull(result);
        Assert.IsTrue(result!.OrderId > 0);
        Assert.AreEqual(3900, result.TotalCents);
        Assert.AreEqual("AwaitingPayment", result.PaymentStatus);
    }

    [TestMethod]
    public async Task MyOrdersDoesNotExposeAnotherShoppersOrder()
    {
        var shopperClient = ProgramTest.NewClient;
        shopperClient.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", ApiTokenHelper.GetNormalUserToken());
        var created = await shopperClient.PostAsync("api/orders", OrderContent(1, 1));
        created.EnsureSuccessStatusCode();
        var order = (await created.Content.ReadAsStringAsync()).FromJson<CreateOrderResult>();

        var adminClient = ProgramTest.NewClient;
        adminClient.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", ApiTokenHelper.GetAdminUserToken());
        var response = await adminClient.GetAsync("api/my-orders");
        response.EnsureSuccessStatusCode();

        var orders = (await response.Content.ReadAsStringAsync()).FromJson<System.Collections.Generic.List<PaymentView>>();
        Assert.IsNotNull(orders);
        Assert.IsFalse(orders!.Exists(o => o.OrderId == order!.OrderId));
    }

    [TestMethod]
    public async Task PayWithoutCardOrSavedMethodIsRejected()
    {
        var client = ProgramTest.NewClient;
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", ApiTokenHelper.GetNormalUserToken());
        var created = await client.PostAsync("api/orders", OrderContent(1, 1));
        created.EnsureSuccessStatusCode();
        var order = (await created.Content.ReadAsStringAsync()).FromJson<CreateOrderResult>();

        var response = await client.PostAsync(
            $"api/orders/{order!.OrderId}/pay",
            new StringContent("{}", Encoding.UTF8, "application/json"));

        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        StringAssert.Contains(body, "PAYMENT_SOURCE_REQUIRED");
    }

    [TestMethod]
    public async Task OperatorEndpointsAreRestrictedToAdministrators()
    {
        var client = ProgramTest.NewClient;
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", ApiTokenHelper.GetNormalUserToken());

        var fulfil = await client.PostAsync("api/orders/1/fulfil", new StringContent("{}", Encoding.UTF8, "application/json"));
        var cancel = await client.PostAsync("api/orders/1/cancel", new StringContent("{}", Encoding.UTF8, "application/json"));
        var reconciliation = await client.GetAsync("api/reconciliation?from=2020-01-01T00:00:00Z&to=2030-01-01T00:00:00Z");

        Assert.AreEqual(HttpStatusCode.Forbidden, fulfil.StatusCode);
        Assert.AreEqual(HttpStatusCode.Forbidden, cancel.StatusCode);
        Assert.AreEqual(HttpStatusCode.Forbidden, reconciliation.StatusCode);
    }

    private static StringContent OrderContent(int catalogItemId, int quantity) =>
        new(
            JsonSerializer.Serialize(new { items = new[] { new { catalogItemId, quantity } } }),
            Encoding.UTF8,
            "application/json");
}
