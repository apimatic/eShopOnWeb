using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.eShopWeb;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.PublicApi.OrderEndpoints;
using Microsoft.eShopWeb.PublicApi.PaymentMethodEndpoints;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PublicApiIntegrationTests.PaymentEndpoints;

[TestClass]
public class PaymentFlowTests
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    private static WebApplicationFactory<Program> CreateFactory(FakePayPalPaymentGateway fake) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IPayPalPaymentGateway>();
                services.AddSingleton<IPayPalPaymentGateway>(fake);
            }));

    private static HttpClient AuthedClient(WebApplicationFactory<Program> factory, string token)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private static StringContent Json(object body) =>
        new(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");

    private static async Task<T> ReadAsync<T>(HttpResponseMessage response)
    {
        var json = await response.Content.ReadAsStringAsync();
        return JsonSerializer.Deserialize<T>(json, JsonOptions)!;
    }

    private static async Task<int> PlaceOrderAsync(HttpClient client, int catalogItemId = 1, int quantity = 1)
    {
        var response = await client.PostAsync("api/orders",
            Json(new { items = new[] { new { catalogItemId, quantity } } }));
        response.EnsureSuccessStatusCode();
        Assert.AreEqual(HttpStatusCode.Created, response.StatusCode);
        var body = await ReadAsync<CreateOrderResponse>(response);
        Assert.IsTrue(body.OrderId > 0);
        return body.OrderId;
    }

    private static object CardBody() => new
    {
        card = new { number = "4111111111111111", expiry = "2030-01", securityCode = "123", cardholderName = "Test" }
    };

    [TestMethod]
    public async Task Place_Pay_MyOrders_Refund_HappyPath()
    {
        var fake = new FakePayPalPaymentGateway();
        using var factory = CreateFactory(fake);
        var client = AuthedClient(factory, ApiTokenHelper.GetNormalUserToken());

        var orderId = await PlaceOrderAsync(client);

        var payResponse = await client.PostAsync($"api/orders/{orderId}/pay", Json(CardBody()));
        payResponse.EnsureSuccessStatusCode();
        var paid = await ReadAsync<PayOrderResponse>(payResponse);
        Assert.AreEqual("Paid", paid.Order.PaymentStatus);
        Assert.AreEqual(1, fake.ChargeCount);

        var myOrders = await ReadAsync<MyOrdersResponse>(await client.GetAsync("api/my-orders"));
        Assert.IsTrue(myOrders.Orders.Exists(o => o.OrderId == orderId && o.PaymentStatus == "Paid"));

        var refundResponse = await client.PostAsync($"api/orders/{orderId}/refunds", content: null);
        refundResponse.EnsureSuccessStatusCode();
        var refunded = await ReadAsync<RefundOrderResponse>(refundResponse);
        Assert.AreEqual("Refunded", refunded.Order.PaymentStatus);
        Assert.AreEqual(1, fake.RefundCount);
    }

    [TestMethod]
    public async Task DoublePay_ChargesOnlyOnce()
    {
        var fake = new FakePayPalPaymentGateway();
        using var factory = CreateFactory(fake);
        var client = AuthedClient(factory, ApiTokenHelper.GetNormalUserToken());

        var orderId = await PlaceOrderAsync(client);

        var first = await client.PostAsync($"api/orders/{orderId}/pay", Json(CardBody()));
        var second = await client.PostAsync($"api/orders/{orderId}/pay", Json(CardBody()));
        first.EnsureSuccessStatusCode();
        second.EnsureSuccessStatusCode();

        Assert.AreEqual(1, fake.ChargeCount); // double-click never double-charges
    }

    [TestMethod]
    public async Task DoubleRefund_RefundsOnlyOnce()
    {
        var fake = new FakePayPalPaymentGateway();
        using var factory = CreateFactory(fake);
        var client = AuthedClient(factory, ApiTokenHelper.GetNormalUserToken());

        var orderId = await PlaceOrderAsync(client);
        (await client.PostAsync($"api/orders/{orderId}/pay", Json(CardBody()))).EnsureSuccessStatusCode();

        (await client.PostAsync($"api/orders/{orderId}/refunds", null)).EnsureSuccessStatusCode();
        (await client.PostAsync($"api/orders/{orderId}/refunds", null)).EnsureSuccessStatusCode();

        Assert.AreEqual(1, fake.RefundCount);
    }

    [TestMethod]
    public async Task SaveCard_List_PayWithIt_Delete_ThenUnusable()
    {
        var fake = new FakePayPalPaymentGateway();
        using var factory = CreateFactory(fake);
        var client = AuthedClient(factory, ApiTokenHelper.GetNormalUserToken());

        // Save a card
        var saveResponse = await client.PostAsync("api/payment-methods",
            Json(new { card = new { number = "4111111111111111", expiry = "2030-01", securityCode = "123" }, label = "My Visa" }));
        Assert.AreEqual(HttpStatusCode.Created, saveResponse.StatusCode);
        var saved = await ReadAsync<CreatePaymentMethodResponse>(saveResponse);
        Assert.IsTrue(saved.PaymentMethodId > 0);
        Assert.AreEqual("VISA", saved.PaymentMethod.CardBrand);
        Assert.AreEqual("1111", saved.PaymentMethod.Last4);
        Assert.AreEqual(1, fake.VaultCount);

        // List shows it
        var list = await ReadAsync<ListPaymentMethodsResponse>(await client.GetAsync("api/payment-methods"));
        Assert.IsTrue(list.PaymentMethods.Exists(p => p.PaymentMethodId == saved.PaymentMethodId));

        // Pay an order with the saved card
        var orderId = await PlaceOrderAsync(client);
        var payResponse = await client.PostAsync($"api/orders/{orderId}/pay",
            Json(new { savedPaymentMethodId = saved.PaymentMethodId }));
        payResponse.EnsureSuccessStatusCode();
        Assert.AreEqual("Paid", (await ReadAsync<PayOrderResponse>(payResponse)).Order.PaymentStatus);

        // Delete it
        (await client.DeleteAsync($"api/payment-methods/{saved.PaymentMethodId}")).EnsureSuccessStatusCode();
        var listAfter = await ReadAsync<ListPaymentMethodsResponse>(await client.GetAsync("api/payment-methods"));
        Assert.IsFalse(listAfter.PaymentMethods.Exists(p => p.PaymentMethodId == saved.PaymentMethodId));

        // No longer usable to pay
        var order2 = await PlaceOrderAsync(client);
        var payWithDeleted = await client.PostAsync($"api/orders/{order2}/pay",
            Json(new { savedPaymentMethodId = saved.PaymentMethodId }));
        Assert.AreEqual(HttpStatusCode.NotFound, payWithDeleted.StatusCode);
    }

    [TestMethod]
    public async Task OneShopperCannotPayOrSeeAnothersOrder()
    {
        var fake = new FakePayPalPaymentGateway();
        using var factory = CreateFactory(fake);

        // demouser places and owns an order
        var demoClient = AuthedClient(factory, ApiTokenHelper.GetNormalUserToken());
        var orderId = await PlaceOrderAsync(demoClient);

        // a different shopper (admin) must not be able to pay it, refund it, or see it
        var otherClient = AuthedClient(factory, ApiTokenHelper.GetAdminUserToken());

        var pay = await otherClient.PostAsync($"api/orders/{orderId}/pay", Json(CardBody()));
        Assert.AreEqual(HttpStatusCode.NotFound, pay.StatusCode);

        var refund = await otherClient.PostAsync($"api/orders/{orderId}/refunds", null);
        Assert.AreEqual(HttpStatusCode.NotFound, refund.StatusCode);

        var otherOrders = await ReadAsync<MyOrdersResponse>(await otherClient.GetAsync("api/my-orders"));
        Assert.IsFalse(otherOrders.Orders.Exists(o => o.OrderId == orderId));

        Assert.AreEqual(0, fake.ChargeCount);
    }

    [TestMethod]
    public async Task PaymentEndpointsRequireAuthentication()
    {
        var fake = new FakePayPalPaymentGateway();
        using var factory = CreateFactory(fake);
        var anon = factory.CreateClient();

        var response = await anon.GetAsync("api/my-orders");
        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
