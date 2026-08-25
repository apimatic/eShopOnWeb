using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.eShopWeb;
using Microsoft.eShopWeb.PublicApi.OrderEndpoints;
using Microsoft.eShopWeb.PublicApi.ReconciliationEndpoints;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PublicApiIntegrationTests.OrderEndpoints;

[TestClass]
public class OrderEndpointsTest
{
    // Matches the catalog seed used elsewhere in this test project (see CatalogItemListPagedEndpoint's
    // "catalog-items/1" case) — a stable, always-present item id to build orders from.
    private const int CatalogItemId = 1;

    private static StringContent OrderBody() => new StringContent(
        JsonSerializer.Serialize(new
        {
            items = new[] { new { catalogItemId = CatalogItemId, quantity = 1 } },
            shipToAddress = new { street = "1 Test St", city = "Testville", state = "WA", country = "US", zipCode = "98000" }
        }), Encoding.UTF8, "application/json");

    private static HttpClient AuthenticatedClient(string token)
    {
        var client = ProgramTest.NewClient;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    [TestMethod]
    public async Task CreateOrder_RequiresAuthentication()
    {
        var client = ProgramTest.NewClient;
        var response = await client.PostAsync("api/orders", OrderBody());
        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [TestMethod]
    public async Task CreateOrder_SucceedsForAuthenticatedBuyer_AwaitingPayment()
    {
        var client = AuthenticatedClient(ApiTokenHelper.GetNormalUserToken());

        var response = await client.PostAsync("api/orders", OrderBody());
        response.EnsureSuccessStatusCode();
        var result = (await response.Content.ReadAsStringAsync()).FromJson<CreateOrderResponse>();

        Assert.IsTrue(result!.OrderId > 0);
        Assert.AreEqual("AwaitingPayment", result.Order.Status);
        Assert.IsNull(result.Order.Payment);
    }

    [TestMethod]
    public async Task MyOrders_OnlyReturnsCallersOwnOrders()
    {
        var owner = AuthenticatedClient(ApiTokenHelper.GetNormalUserToken());
        var createResponse = await owner.PostAsync("api/orders", OrderBody());
        var created = (await createResponse.Content.ReadAsStringAsync()).FromJson<CreateOrderResponse>();

        var ownerOrders = (await (await owner.GetAsync("api/my-orders")).Content.ReadAsStringAsync()).FromJson<MyOrdersResponse>();
        Assert.IsTrue(ownerOrders!.Orders.Any(o => o.OrderId == created!.OrderId));

        var otherBuyer = AuthenticatedClient(ApiTokenHelper.GetOtherUserToken());
        var otherOrders = (await (await otherBuyer.GetAsync("api/my-orders")).Content.ReadAsStringAsync()).FromJson<MyOrdersResponse>();
        Assert.IsFalse(otherOrders!.Orders.Any(o => o.OrderId == created!.OrderId));
    }

    [TestMethod]
    public async Task Pay_ReturnsNotFound_ForAnotherBuyersOrder()
    {
        var owner = AuthenticatedClient(ApiTokenHelper.GetNormalUserToken());
        var createResponse = await owner.PostAsync("api/orders", OrderBody());
        var created = (await createResponse.Content.ReadAsStringAsync()).FromJson<CreateOrderResponse>();

        // Use a syntactically valid card (not a nonexistent paymentMethodId) so the only thing that
        // can reject this request is order ownership, not saved-card validation.
        var otherBuyer = AuthenticatedClient(ApiTokenHelper.GetOtherUserToken());
        var payBody = new StringContent(JsonSerializer.Serialize(new
        {
            card = new
            {
                name = "John Doe",
                number = "4111111111111111",
                expiry = "2030-12",
                securityCode = "123",
                addressLine1 = "1 Test St",
                city = "Testville",
                postalCode = "98000",
                countryCode = "US"
            }
        }), Encoding.UTF8, "application/json");
        var payResponse = await otherBuyer.PostAsync($"api/orders/{created!.OrderId}/pay", payBody);

        Assert.AreEqual(HttpStatusCode.NotFound, payResponse.StatusCode);
    }

    [TestMethod]
    public async Task Refunds_ReturnsNotFound_ForAnotherBuyersOrder()
    {
        var owner = AuthenticatedClient(ApiTokenHelper.GetNormalUserToken());
        var createResponse = await owner.PostAsync("api/orders", OrderBody());
        var created = (await createResponse.Content.ReadAsStringAsync()).FromJson<CreateOrderResponse>();

        var otherBuyer = AuthenticatedClient(ApiTokenHelper.GetOtherUserToken());
        var refundBody = new StringContent(JsonSerializer.Serialize(new { idempotencyKey = "test-key" }), Encoding.UTF8, "application/json");
        var refundResponse = await otherBuyer.PostAsync($"api/orders/{created!.OrderId}/refunds", refundBody);

        Assert.AreEqual(HttpStatusCode.NotFound, refundResponse.StatusCode);
    }

    [TestMethod]
    public async Task Fulfil_ForbiddenForNonAdmin()
    {
        var client = AuthenticatedClient(ApiTokenHelper.GetNormalUserToken());
        var response = await client.PostAsync("api/orders/1/fulfil", null);
        Assert.AreEqual(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [TestMethod]
    public async Task Cancel_ForbiddenForNonAdmin()
    {
        var client = AuthenticatedClient(ApiTokenHelper.GetNormalUserToken());
        var response = await client.PostAsync("api/orders/1/cancel", null);
        Assert.AreEqual(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [TestMethod]
    public async Task Reconciliation_ForbiddenForNonAdmin()
    {
        var client = AuthenticatedClient(ApiTokenHelper.GetNormalUserToken());
        var response = await client.GetAsync("api/reconciliation?from=2020-01-01T00:00:00Z&to=2020-01-02T00:00:00Z");
        Assert.AreEqual(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [TestMethod]
    public async Task Reconciliation_SucceedsForAdmin()
    {
        // PayPal's transaction search only looks back ~3 years, so the range must be recent — an
        // empty result here is still a valid, successful response (see the task's note on reporting
        // lag); this only asserts the call succeeds and returns a well-formed report.
        var client = AuthenticatedClient(ApiTokenHelper.GetAdminUserToken());
        var to = System.DateTimeOffset.UtcNow;
        var from = to.AddDays(-7);
        var query = $"from={System.Uri.EscapeDataString(from.ToString("O"))}&to={System.Uri.EscapeDataString(to.ToString("O"))}";
        var response = await client.GetAsync($"api/reconciliation?{query}");
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, await response.Content.ReadAsStringAsync());
        var result = (await response.Content.ReadAsStringAsync()).FromJson<ReconciliationResponse>();
        Assert.IsNotNull(result);
    }

    /// <summary>
    /// Exercises the full money-moving lifecycle for real against the PayPal sandbox (same
    /// credentials configured for this app via user-secrets/environment) using PayPal's published
    /// test card. Mirrors the manual self-verification: authorize, capture at fulfilment (with a
    /// real fee/net breakdown reported back), then a real, idempotent refund.
    /// </summary>
    [TestMethod]
    public async Task PayFulfilRefund_FullLifecycle_AgainstRealSandbox()
    {
        var buyer = AuthenticatedClient(ApiTokenHelper.GetNormalUserToken());
        var admin = AuthenticatedClient(ApiTokenHelper.GetAdminUserToken());

        var createResponse = await buyer.PostAsync("api/orders", OrderBody());
        createResponse.EnsureSuccessStatusCode();
        var created = (await createResponse.Content.ReadAsStringAsync()).FromJson<CreateOrderResponse>();

        var payBody = new StringContent(JsonSerializer.Serialize(new
        {
            card = new
            {
                name = "John Doe",
                number = "4111111111111111",
                expiry = "2030-12",
                securityCode = "123",
                addressLine1 = "1 Test St",
                city = "Testville",
                postalCode = "98000",
                countryCode = "US"
            }
        }), Encoding.UTF8, "application/json");

        var payResponse = await buyer.PostAsync($"api/orders/{created!.OrderId}/pay", payBody);
        Assert.AreEqual(HttpStatusCode.OK, payResponse.StatusCode, await payResponse.Content.ReadAsStringAsync());
        var paid = (await payResponse.Content.ReadAsStringAsync()).FromJson<PayOrderResponse>();
        Assert.AreEqual("PaymentAuthorized", paid!.Order.Status);
        Assert.IsFalse(string.IsNullOrEmpty(paid.Order.Payment!.AuthorizationId));

        var fulfilResponse = await admin.PostAsync($"api/orders/{created.OrderId}/fulfil", null);
        Assert.AreEqual(HttpStatusCode.OK, fulfilResponse.StatusCode, await fulfilResponse.Content.ReadAsStringAsync());
        var fulfilled = (await fulfilResponse.Content.ReadAsStringAsync()).FromJson<FulfilOrderResponse>();
        Assert.AreEqual("Fulfilled", fulfilled!.Order.Status);
        Assert.IsNotNull(fulfilled.Order.Payment!.CapturedAmount);
        Assert.IsNotNull(fulfilled.Order.Payment.PayPalFeeAmount);
        Assert.IsNotNull(fulfilled.Order.Payment.NetAmount);

        var refundKey = $"lifecycle-test-{created.OrderId}";
        StringContent RefundBody() => new StringContent(JsonSerializer.Serialize(new { idempotencyKey = refundKey }), Encoding.UTF8, "application/json");

        var refundResponse = await buyer.PostAsync($"api/orders/{created.OrderId}/refunds", RefundBody());
        Assert.AreEqual(HttpStatusCode.OK, refundResponse.StatusCode, await refundResponse.Content.ReadAsStringAsync());
        var refunded = (await refundResponse.Content.ReadAsStringAsync()).FromJson<RefundOrderResponse>();
        Assert.AreEqual("Refunded", refunded!.Order.Status);

        // Repeating the same idempotency key must not refund twice.
        var repeatResponse = await buyer.PostAsync($"api/orders/{created.OrderId}/refunds", RefundBody());
        Assert.AreEqual(HttpStatusCode.OK, repeatResponse.StatusCode);
        var repeat = (await repeatResponse.Content.ReadAsStringAsync()).FromJson<RefundOrderResponse>();
        Assert.AreEqual(refunded.RefundId, repeat!.RefundId);
    }
}
