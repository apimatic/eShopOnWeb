using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PublicApiIntegrationTests.PaymentEndpoints;

[TestClass]
public class PaymentEndpointsTest
{
    private static PaymentApiFactory _factory = null!;

    [ClassInitialize]
    public static void Init(TestContext _) => _factory = new PaymentApiFactory();

    [ClassCleanup]
    public static void Cleanup() => _factory.Dispose();

    private static HttpClient Client(string? token)
    {
        var client = _factory.CreateClient();
        if (token is not null)
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private static object Card => new { number = "4111111111111111", expiry = "2030-01", securityCode = "123", name = "Test Buyer" };

    [TestMethod]
    public async Task Diagnostic_CreateOrderBody()
    {
        var shopper = Client(ApiTokenHelper.GetNormalUserToken());
        var create = await shopper.PostAsJsonAsync("api/orders", new { items = new[] { new { catalogItemId = 1, quantity = 2 } } });
        var body = await create.Content.ReadAsStringAsync();
        Assert.AreEqual(HttpStatusCode.Created, create.StatusCode, "BODY=" + body);
    }

    [TestMethod]
    public async Task Places_Authorizes_Fulfils_And_Refunds_An_Order()
    {
        var shopper = Client(ApiTokenHelper.GetNormalUserToken());
        var admin = Client(ApiTokenHelper.GetAdminUserToken());

        var create = await shopper.PostAsJsonAsync("api/orders", new { items = new[] { new { catalogItemId = 1, quantity = 2 } } });
        Assert.AreEqual(HttpStatusCode.Created, create.StatusCode);
        var orderId = (await create.Content.ReadFromJsonAsync<OrderCreatedDto>())!.OrderId;

        var pay = await shopper.PostAsJsonAsync($"api/orders/{orderId}/pay", new { card = Card });
        pay.EnsureSuccessStatusCode();
        var paid = await pay.Content.ReadFromJsonAsync<PaymentDto>();
        Assert.AreEqual("Authorized", paid!.PaymentStatus);
        Assert.IsNotNull(paid.AuthorizationId);

        var fulfil = await admin.PostAsJsonAsync($"api/orders/{orderId}/fulfil", new { });
        fulfil.EnsureSuccessStatusCode();
        var captured = await fulfil.Content.ReadFromJsonAsync<PaymentDto>();
        Assert.AreEqual("Captured", captured!.PaymentStatus);
        Assert.IsNotNull(captured.CaptureId);
        Assert.IsNotNull(captured.PaypalFee);
        Assert.IsNotNull(captured.NetAmount);

        var key = System.Guid.NewGuid().ToString();
        var refund1 = await shopper.PostAsJsonAsync($"api/orders/{orderId}/refunds", new { amount = 1.00m, idempotencyKey = key });
        refund1.EnsureSuccessStatusCode();
        var r1 = await refund1.Content.ReadFromJsonAsync<RefundDto>();
        Assert.AreEqual("PartiallyRefunded", r1!.PaymentStatus);

        // Same idempotency key -> same refund, no second gateway refund.
        var before = _factory.Gateway.RefundCalls;
        var refund2 = await shopper.PostAsJsonAsync($"api/orders/{orderId}/refunds", new { amount = 1.00m, idempotencyKey = key });
        refund2.EnsureSuccessStatusCode();
        var r2 = await refund2.Content.ReadFromJsonAsync<RefundDto>();
        Assert.AreEqual(r1.RefundId, r2!.RefundId);
        Assert.AreEqual(before, _factory.Gateway.RefundCalls, "a repeated idempotency key must not refund again");
    }

    [TestMethod]
    public async Task Refund_Beyond_Captured_Is_Rejected()
    {
        var shopper = Client(ApiTokenHelper.GetNormalUserToken());
        var admin = Client(ApiTokenHelper.GetAdminUserToken());
        var orderId = await PlaceAuthorizeFulfil(shopper, admin);

        var resp = await shopper.PostAsJsonAsync($"api/orders/{orderId}/refunds",
            new { amount = 999999m, idempotencyKey = System.Guid.NewGuid().ToString() });
        Assert.AreEqual(HttpStatusCode.Conflict, resp.StatusCode);
    }

    [TestMethod]
    public async Task Cancel_Voids_An_Authorized_Order()
    {
        var shopper = Client(ApiTokenHelper.GetNormalUserToken());
        var admin = Client(ApiTokenHelper.GetAdminUserToken());

        var create = await shopper.PostAsJsonAsync("api/orders", new { items = new[] { new { catalogItemId = 2, quantity = 1 } } });
        var orderId = (await create.Content.ReadFromJsonAsync<OrderCreatedDto>())!.OrderId;
        await shopper.PostAsJsonAsync($"api/orders/{orderId}/pay", new { card = Card });

        var cancel = await admin.PostAsJsonAsync($"api/orders/{orderId}/cancel", new { });
        cancel.EnsureSuccessStatusCode();
        var voided = await cancel.Content.ReadFromJsonAsync<PaymentDto>();
        Assert.AreEqual("Voided", voided!.PaymentStatus);
    }

    [TestMethod]
    public async Task Saved_Card_Is_Reusable_And_Deletable()
    {
        var shopper = Client(ApiTokenHelper.GetNormalUserToken());
        var admin = Client(ApiTokenHelper.GetAdminUserToken());

        var save = await shopper.PostAsJsonAsync("api/payment-methods", new { card = Card });
        Assert.AreEqual(HttpStatusCode.Created, save.StatusCode);
        var saved = await save.Content.ReadFromJsonAsync<SavedCardDto>();
        Assert.IsTrue(saved!.PaymentMethodId > 0);
        Assert.AreEqual("1111", saved.LastFourDigits);

        // Pay a new order with the saved card.
        var create = await shopper.PostAsJsonAsync("api/orders", new { items = new[] { new { catalogItemId = 1, quantity = 1 } } });
        var orderId = (await create.Content.ReadFromJsonAsync<OrderCreatedDto>())!.OrderId;
        var pay = await shopper.PostAsJsonAsync($"api/orders/{orderId}/pay", new { savedPaymentMethodId = saved.PaymentMethodId });
        pay.EnsureSuccessStatusCode();

        // Delete it; afterwards it is gone and cannot be used to pay.
        var del = await shopper.DeleteAsync($"api/payment-methods/{saved.PaymentMethodId}");
        Assert.AreEqual(HttpStatusCode.NoContent, del.StatusCode);

        var list = await shopper.GetFromJsonAsync<List<SavedCardDto>>("api/payment-methods");
        Assert.IsFalse(list!.Exists(c => c.PaymentMethodId == saved.PaymentMethodId));

        var create2 = await shopper.PostAsJsonAsync("api/orders", new { items = new[] { new { catalogItemId = 1, quantity = 1 } } });
        var orderId2 = (await create2.Content.ReadFromJsonAsync<OrderCreatedDto>())!.OrderId;
        var payDeleted = await shopper.PostAsJsonAsync($"api/orders/{orderId2}/pay", new { savedPaymentMethodId = saved.PaymentMethodId });
        Assert.AreEqual(HttpStatusCode.NotFound, payDeleted.StatusCode);
    }

    [TestMethod]
    public async Task One_Shopper_Cannot_Pay_Another_Shoppers_Order()
    {
        var shopper = Client(ApiTokenHelper.GetNormalUserToken());
        var create = await shopper.PostAsJsonAsync("api/orders", new { items = new[] { new { catalogItemId = 1, quantity = 1 } } });
        var orderId = (await create.Content.ReadFromJsonAsync<OrderCreatedDto>())!.OrderId;

        // Admin is authenticated but is a different buyer -> the order is not theirs -> 404 (not leaked).
        var admin = Client(ApiTokenHelper.GetAdminUserToken());
        var pay = await admin.PostAsJsonAsync($"api/orders/{orderId}/pay", new { card = Card });
        Assert.AreEqual(HttpStatusCode.NotFound, pay.StatusCode);
    }

    [TestMethod]
    public async Task Operator_Endpoints_Require_Administrator_Role()
    {
        var normal = Client(ApiTokenHelper.GetNormalUserToken());
        Assert.AreEqual(HttpStatusCode.Forbidden, (await normal.PostAsJsonAsync("api/orders/1/fulfil", new { })).StatusCode);
        Assert.AreEqual(HttpStatusCode.Forbidden, (await normal.PostAsJsonAsync("api/orders/1/cancel", new { })).StatusCode);
        Assert.AreEqual(HttpStatusCode.Forbidden,
            (await normal.GetAsync("api/reconciliation?from=2026-01-01T00:00:00Z&to=2026-01-02T00:00:00Z")).StatusCode);
    }

    [TestMethod]
    public async Task Shopper_Endpoints_Require_Authentication()
    {
        var anon = Client(null);
        Assert.AreEqual(HttpStatusCode.Unauthorized, (await anon.GetAsync("api/my-orders")).StatusCode);
        Assert.AreEqual(HttpStatusCode.Unauthorized, (await anon.GetAsync("api/payment-methods")).StatusCode);
    }

    private async Task<int> PlaceAuthorizeFulfil(HttpClient shopper, HttpClient admin)
    {
        var create = await shopper.PostAsJsonAsync("api/orders", new { items = new[] { new { catalogItemId = 1, quantity = 2 } } });
        var orderId = (await create.Content.ReadFromJsonAsync<OrderCreatedDto>())!.OrderId;
        await shopper.PostAsJsonAsync($"api/orders/{orderId}/pay", new { card = Card });
        await admin.PostAsJsonAsync($"api/orders/{orderId}/fulfil", new { });
        return orderId;
    }

    private record OrderCreatedDto(int OrderId, string PaymentStatus);
    private record PaymentDto(int OrderId, string PaymentStatus, decimal Amount, decimal? CapturedAmount,
        decimal? PaypalFee, decimal? NetAmount, string? AuthorizationId, string? CaptureId);
    private record RefundDto(string RefundId, decimal Amount, decimal RefundedTotal, string PaymentStatus);
    private record SavedCardDto(int PaymentMethodId, string? Brand, string? LastFourDigits, string? Expiry);
}
