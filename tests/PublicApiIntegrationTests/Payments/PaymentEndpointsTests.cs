using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.eShopWeb;
using Microsoft.eShopWeb.ApplicationCore.Interfaces.Payments;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PublicApiIntegrationTests.Payments;

[TestClass]
public class PaymentEndpointsTests
{
    private const int SeededCatalogItemId = 1;   // ".NET Bot Black Sweatshirt", $19.50
    private const decimal UnitPrice = 19.50m;

    private static readonly FakePayPalGateway Fake = new();
    private static WebApplicationFactory<Program> _factory = null!;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [ClassInitialize]
    public static void ClassInitialize(TestContext _)
    {
        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IPayPalGateway>();
                services.AddSingleton<IPayPalGateway>(Fake);
            });
        });
    }

    [ClassCleanup]
    public static void ClassCleanup() => _factory?.Dispose();

    private static HttpClient ClientFor(string token)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private static HttpClient ShopperClient() => ClientFor(ApiTokenHelper.GetNormalUserToken());
    private static HttpClient AdminClient() => ClientFor(ApiTokenHelper.GetAdminUserToken());

    private static StringContent Json(object payload) =>
        new(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

    private static object CardPayload() => new
    {
        card = new
        {
            number = "4111111111111111",
            expiry = "2030-01",
            securityCode = "123",
            cardholderName = "Test Shopper",
            billingAddress = new { countryCode = "US" }
        }
    };

    private static async Task<JsonElement> ReadRoot(HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsStringAsync();
        return JsonDocument.Parse(body).RootElement.Clone();
    }

    private static async Task<int> PlaceOrderAsync(HttpClient client, int quantity = 2)
    {
        var response = await client.PostAsync("api/orders",
            Json(new { items = new[] { new { catalogItemId = SeededCatalogItemId, quantity } } }));
        Assert.AreEqual(HttpStatusCode.Created, response.StatusCode);
        var root = await ReadRoot(response);
        return root.GetProperty("orderId").GetInt32();
    }

    [TestMethod]
    public async Task AdminEndpoints_RejectNormalUser()
    {
        var client = ShopperClient();

        var fulfil = await client.PostAsync("api/orders/999/fulfil", Json(new { }));
        var cancel = await client.PostAsync("api/orders/999/cancel", Json(new { }));
        var reconcile = await client.GetAsync("api/reconciliation?from=2020-01-01T00:00:00Z&to=2020-01-02T00:00:00Z");

        Assert.AreEqual(HttpStatusCode.Forbidden, fulfil.StatusCode);
        Assert.AreEqual(HttpStatusCode.Forbidden, cancel.StatusCode);
        Assert.AreEqual(HttpStatusCode.Forbidden, reconcile.StatusCode);
    }

    [TestMethod]
    public async Task Unauthenticated_IsRejected()
    {
        var client = _factory.CreateClient(); // no token
        var response = await client.GetAsync("api/my-orders");
        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [TestMethod]
    public async Task FullFlow_Place_Pay_Fulfil_Refund()
    {
        var shopper = ShopperClient();
        var admin = AdminClient();

        var orderId = await PlaceOrderAsync(shopper, quantity: 2);
        var expectedTotal = UnitPrice * 2;

        // Authorize (hold), and a second click must be idempotent.
        var pay1 = await shopper.PostAsync($"api/orders/{orderId}/pay", Json(CardPayload()));
        var pay2 = await shopper.PostAsync($"api/orders/{orderId}/pay", Json(CardPayload()));
        Assert.AreEqual(HttpStatusCode.OK, pay1.StatusCode);
        Assert.AreEqual(HttpStatusCode.OK, pay2.StatusCode);

        // Operator fulfils → money captured.
        var fulfil = await admin.PostAsync($"api/orders/{orderId}/fulfil", Json(new { }));
        Assert.AreEqual(HttpStatusCode.OK, fulfil.StatusCode);

        // Payment state reflects the capture with fee and net.
        var payment = await FindOrderPaymentAsync(shopper, orderId);
        Assert.AreEqual("Fulfilled", payment.GetProperty("paymentStatus").GetString());
        var state = payment.GetProperty("payment");
        Assert.AreEqual(expectedTotal, state.GetProperty("capturedGrossAmount").GetDecimal());
        Assert.IsTrue(state.GetProperty("payPalFee").GetDecimal() > 0m);
        Assert.IsTrue(state.GetProperty("netAmount").GetDecimal() < expectedTotal);

        // Partial refund, then the same idempotency key must not refund twice.
        var refund1 = await shopper.PostAsync($"api/orders/{orderId}/refunds", Json(new { amount = 10.00m, idempotencyKey = "k1" }));
        var refund1Again = await shopper.PostAsync($"api/orders/{orderId}/refunds", Json(new { amount = 10.00m, idempotencyKey = "k1" }));
        Assert.AreEqual(HttpStatusCode.Created, refund1.StatusCode);
        Assert.AreEqual(HttpStatusCode.Created, refund1Again.StatusCode);
        var refundId1 = (await ReadRoot(refund1)).GetProperty("refundId").GetInt32();
        var refundId1Again = (await ReadRoot(refund1Again)).GetProperty("refundId").GetInt32();
        Assert.AreEqual(refundId1, refundId1Again, "Repeating a refund under the same key must return the same refund.");

        // A distinct partial refund under a new key is allowed.
        var refund2 = await shopper.PostAsync($"api/orders/{orderId}/refunds", Json(new { amount = 5.00m, idempotencyKey = "k2" }));
        Assert.AreEqual(HttpStatusCode.Created, refund2.StatusCode);
        Assert.AreNotEqual(refundId1, (await ReadRoot(refund2)).GetProperty("refundId").GetInt32());

        // Refunding beyond the captured amount is rejected.
        var overRefund = await shopper.PostAsync($"api/orders/{orderId}/refunds", Json(new { amount = 1000m, idempotencyKey = "k3" }));
        Assert.AreEqual(HttpStatusCode.BadRequest, overRefund.StatusCode);
    }

    [TestMethod]
    public async Task Cancel_ReleasesHoldBeforeFulfilment()
    {
        var shopper = ShopperClient();
        var admin = AdminClient();

        var orderId = await PlaceOrderAsync(shopper);
        await shopper.PostAsync($"api/orders/{orderId}/pay", Json(CardPayload()));

        var cancel = await admin.PostAsync($"api/orders/{orderId}/cancel", Json(new { }));
        Assert.AreEqual(HttpStatusCode.OK, cancel.StatusCode);

        var payment = await FindOrderPaymentAsync(shopper, orderId);
        Assert.AreEqual("Cancelled", payment.GetProperty("paymentStatus").GetString());
    }

    [TestMethod]
    public async Task StaleHold_IsRenewedAtFulfilment()
    {
        var shopper = ShopperClient();
        var admin = AdminClient();
        Fake.NextAuthorizationIsStale = true;
        try
        {
            var orderId = await PlaceOrderAsync(shopper);
            await shopper.PostAsync($"api/orders/{orderId}/pay", Json(CardPayload()));

            var fulfil = await admin.PostAsync($"api/orders/{orderId}/fulfil", Json(new { }));
            Assert.AreEqual(HttpStatusCode.OK, fulfil.StatusCode);

            var payment = await FindOrderPaymentAsync(shopper, orderId);
            Assert.AreEqual("Fulfilled", payment.GetProperty("paymentStatus").GetString());
        }
        finally
        {
            Fake.NextAuthorizationIsStale = false;
        }
    }

    [TestMethod]
    public async Task SavedCard_CanBeSavedListedReusedAndDeleted()
    {
        var shopper = ShopperClient();

        // Save a card — response describes it safely, never full details.
        var save = await shopper.PostAsync("api/payment-methods", Json(CardPayload()));
        Assert.AreEqual(HttpStatusCode.Created, save.StatusCode);
        var saved = await ReadRoot(save);
        var paymentMethodId = saved.GetProperty("paymentMethodId").GetInt32();
        Assert.AreEqual("Visa", saved.GetProperty("cardBrand").GetString());
        Assert.AreEqual("1111", saved.GetProperty("lastFourDigits").GetString());

        // It appears in the caller's saved cards (listed as "id").
        var list = await shopper.GetAsync("api/payment-methods");
        Assert.IsTrue((await list.Content.ReadAsStringAsync()).Contains($"\"id\":{paymentMethodId}"));

        // Reuse it to pay a new order.
        var orderId = await PlaceOrderAsync(shopper);
        var pay = await shopper.PostAsync($"api/orders/{orderId}/pay", Json(new { savedPaymentMethodId = paymentMethodId }));
        Assert.AreEqual(HttpStatusCode.OK, pay.StatusCode);

        // Delete it — afterwards it is neither listed nor usable.
        var delete = await shopper.DeleteAsync($"api/payment-methods/{paymentMethodId}");
        Assert.AreEqual(HttpStatusCode.NoContent, delete.StatusCode);

        var listAfter = await shopper.GetAsync("api/payment-methods");
        Assert.IsFalse((await listAfter.Content.ReadAsStringAsync()).Contains($"\"id\":{paymentMethodId}"));

        var orderId2 = await PlaceOrderAsync(shopper);
        var payDeleted = await shopper.PostAsync($"api/orders/{orderId2}/pay", Json(new { savedPaymentMethodId = paymentMethodId }));
        Assert.AreEqual(HttpStatusCode.NotFound, payDeleted.StatusCode);
    }

    [TestMethod]
    public async Task Order_IsIsolatedToItsOwner()
    {
        var owner = ShopperClient();
        var otherShopper = ClientFor(ApiTokenHelper.GetNormalUserToken("other-shopper@microsoft.com"));

        var orderId = await PlaceOrderAsync(owner);
        await owner.PostAsync($"api/orders/{orderId}/pay", Json(CardPayload()));

        // Another shopper cannot see it in their orders...
        var otherOrders = await otherShopper.GetAsync("api/my-orders");
        Assert.IsFalse((await otherOrders.Content.ReadAsStringAsync()).Contains($"\"orderId\":{orderId}"));

        // ...and cannot act on it (same response as not-found — ownership is not leaked).
        var otherRefund = await otherShopper.PostAsync($"api/orders/{orderId}/refunds", Json(new { amount = 1m, idempotencyKey = "x" }));
        Assert.AreEqual(HttpStatusCode.NotFound, otherRefund.StatusCode);
    }

    [TestMethod]
    public async Task SavedCard_IsIsolatedToItsOwner()
    {
        var owner = ShopperClient();
        var otherShopper = ClientFor(ApiTokenHelper.GetNormalUserToken("stranger@microsoft.com"));

        var save = await owner.PostAsync("api/payment-methods", Json(CardPayload()));
        var paymentMethodId = (await ReadRoot(save)).GetProperty("paymentMethodId").GetInt32();

        // The stranger cannot see or delete it.
        var strangerList = await otherShopper.GetAsync("api/payment-methods");
        Assert.IsFalse((await strangerList.Content.ReadAsStringAsync()).Contains($"\"id\":{paymentMethodId}"));

        var strangerDelete = await otherShopper.DeleteAsync($"api/payment-methods/{paymentMethodId}");
        Assert.AreEqual(HttpStatusCode.NotFound, strangerDelete.StatusCode);
    }

    [TestMethod]
    public async Task Reconciliation_ListsCapturedOrdersAsMatched()
    {
        var shopper = ShopperClient();
        var admin = AdminClient();

        var orderId = await PlaceOrderAsync(shopper);
        await shopper.PostAsync($"api/orders/{orderId}/pay", Json(CardPayload()));
        await admin.PostAsync($"api/orders/{orderId}/fulfil", Json(new { }));

        var payment = await FindOrderPaymentAsync(shopper, orderId);
        var captureId = payment.GetProperty("payment").GetProperty("captureId").GetString();

        var response = await admin.GetAsync("api/reconciliation?from=2000-01-01T00:00:00Z&to=2100-01-01T00:00:00Z");
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);

        var report = (await ReadRoot(response)).GetProperty("report");
        var matched = false;
        foreach (var m in report.GetProperty("matched").EnumerateArray())
        {
            if (m.GetProperty("captureId").GetString() == captureId)
            {
                matched = true;
                Assert.AreEqual(orderId, m.GetProperty("orderId").GetInt32());
            }
        }
        Assert.IsTrue(matched, "The captured order should line up against PayPal's transaction record.");
    }

    private static async Task<JsonElement> FindOrderPaymentAsync(HttpClient client, int orderId)
    {
        var response = await client.GetAsync("api/my-orders");
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        var root = await ReadRoot(response);
        foreach (var order in root.GetProperty("orders").EnumerateArray())
        {
            if (order.GetProperty("orderId").GetInt32() == orderId)
            {
                return order;
            }
        }

        Assert.Fail($"Order {orderId} was not found in the caller's orders.");
        return default;
    }
}
