using System.Net;
using System.Net.Http.Json;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PublicApiIntegrationTests.Payments;

[TestClass]
public class OrderPaymentEndpointsTest : PaymentTestBase
{
    [TestMethod]
    public async Task CreateOrder_ReturnsOrderId_AwaitingPayment()
    {
        var client = AuthedClient(DemoToken);

        var response = await client.PostAsJsonAsync("api/orders", new { items = new[] { new { catalogItemId = 2, quantity = 3 } } });

        Assert.AreEqual(HttpStatusCode.Created, response.StatusCode);
        var body = await ReadJson(response);
        Assert.IsTrue(body.GetProperty("orderId").GetInt32() > 0);
        Assert.AreEqual("AwaitingPayment", body.GetProperty("order").GetProperty("paymentStatus").GetString());
    }

    [TestMethod]
    public async Task CreateOrder_Unauthenticated_Returns401()
    {
        var response = await AnonymousClient().PostAsJsonAsync("api/orders", new { items = new[] { new { catalogItemId = 2, quantity = 1 } } });
        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [TestMethod]
    public async Task CreateOrder_EmptyItems_Returns400()
    {
        var client = AuthedClient(DemoToken);
        var response = await client.PostAsJsonAsync("api/orders", new { items = new object[0] });
        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [TestMethod]
    public async Task PayWithCard_MarksOrderPaid()
    {
        var client = AuthedClient(DemoToken);
        var orderId = await CreateOrderAsync(client);

        var response = await client.PostAsJsonAsync($"api/orders/{orderId}/pay", CardBody());

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        var order = (await ReadJson(response)).GetProperty("order");
        Assert.AreEqual("Paid", order.GetProperty("paymentStatus").GetString());
        Assert.IsFalse(string.IsNullOrEmpty(order.GetProperty("payPalCaptureId").GetString()));
        Assert.AreEqual(1, Factory.Gateway.ChargeCardCalls);
    }

    [TestMethod]
    public async Task PayTwice_IsIdempotent_ChargesOnce()
    {
        var client = AuthedClient(DemoToken);
        var orderId = await CreateOrderAsync(client);

        var first = await client.PostAsJsonAsync($"api/orders/{orderId}/pay", CardBody());
        var second = await client.PostAsJsonAsync($"api/orders/{orderId}/pay", CardBody());

        Assert.AreEqual(HttpStatusCode.OK, first.StatusCode);
        Assert.AreEqual(HttpStatusCode.OK, second.StatusCode);
        var cap1 = (await ReadJson(first)).GetProperty("order").GetProperty("payPalCaptureId").GetString();
        var cap2 = (await ReadJson(second)).GetProperty("order").GetProperty("payPalCaptureId").GetString();
        Assert.AreEqual(cap1, cap2, "double-click must not change the capture");
        Assert.AreEqual(1, Factory.Gateway.ChargeCardCalls, "double-click must not charge twice");
    }

    [TestMethod]
    public async Task Pay_Declined_Returns402()
    {
        Factory.Gateway.DeclineReason = "The card was declined.";
        var client = AuthedClient(DemoToken);
        var orderId = await CreateOrderAsync(client);

        var response = await client.PostAsJsonAsync($"api/orders/{orderId}/pay", CardBody());

        Assert.AreEqual(HttpStatusCode.PaymentRequired, response.StatusCode);
    }

    [TestMethod]
    public async Task Pay_MissingInstrument_Returns400()
    {
        var client = AuthedClient(DemoToken);
        var orderId = await CreateOrderAsync(client);

        var response = await client.PostAsJsonAsync($"api/orders/{orderId}/pay", new { });

        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [TestMethod]
    public async Task Refund_AfterPay_MarksRefunded_AndIsIdempotent()
    {
        var client = AuthedClient(DemoToken);
        var orderId = await CreateOrderAsync(client);
        await client.PostAsJsonAsync($"api/orders/{orderId}/pay", CardBody());

        var first = await client.PostAsync($"api/orders/{orderId}/refunds", null);
        var second = await client.PostAsync($"api/orders/{orderId}/refunds", null);

        Assert.AreEqual(HttpStatusCode.OK, first.StatusCode);
        Assert.AreEqual(HttpStatusCode.OK, second.StatusCode);
        Assert.AreEqual("Refunded", (await ReadJson(first)).GetProperty("order").GetProperty("paymentStatus").GetString());
        var ref1 = (await ReadJson(first)).GetProperty("order").GetProperty("payPalRefundId").GetString();
        var ref2 = (await ReadJson(second)).GetProperty("order").GetProperty("payPalRefundId").GetString();
        Assert.AreEqual(ref1, ref2, "double-click must not change the refund");
        Assert.AreEqual(1, Factory.Gateway.RefundCalls, "double-click must not refund twice");
    }

    [TestMethod]
    public async Task Refund_WithoutPay_Returns409()
    {
        var client = AuthedClient(DemoToken);
        var orderId = await CreateOrderAsync(client);

        var response = await client.PostAsync($"api/orders/{orderId}/refunds", null);

        Assert.AreEqual(HttpStatusCode.Conflict, response.StatusCode);
        Assert.AreEqual(0, Factory.Gateway.RefundCalls);
    }

    [TestMethod]
    public async Task Pay_OtherUsersOrder_Returns404()
    {
        var owner = AuthedClient(DemoToken);
        var orderId = await CreateOrderAsync(owner);

        var attacker = AuthedClient(OtherToken);
        var response = await attacker.PostAsJsonAsync($"api/orders/{orderId}/pay", CardBody());

        Assert.AreEqual(HttpStatusCode.NotFound, response.StatusCode);
        Assert.AreEqual(0, Factory.Gateway.ChargeCardCalls);
    }

    [TestMethod]
    public async Task MyOrders_ReturnsOnlyCallersOrders()
    {
        // Unique users so the shared in-memory store from other tests cannot skew the counts.
        var demo = AuthedClient(UniqueUserToken());
        await CreateOrderAsync(demo);
        await CreateOrderAsync(demo);

        var other = AuthedClient(UniqueUserToken());
        await CreateOrderAsync(other);

        var demoOrders = await ReadJson(await demo.GetAsync("api/my-orders"));
        var otherOrders = await ReadJson(await other.GetAsync("api/my-orders"));

        Assert.AreEqual(2, demoOrders.GetProperty("orders").GetArrayLength());
        Assert.AreEqual(1, otherOrders.GetProperty("orders").GetArrayLength());
    }
}
