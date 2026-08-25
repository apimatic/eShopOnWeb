using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PublicApiIntegrationTests.Fakes;

namespace PublicApiIntegrationTests.OrderEndpoints;

[TestClass]
public class OrderPaymentFlowTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static PaymentsApiFactory _factory = new();

    [ClassInitialize]
    public static void ClassInitialize(TestContext _) => _factory = new PaymentsApiFactory();

    private static HttpClient AuthorizedClient(string token)
    {
        var client = _factory.NewClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private static object ValidCard() => new
    {
        number = "4111111111111111",
        expiryMonth = 12,
        expiryYear = 2030,
        securityCode = "123",
        cardholderName = "Jane Doe",
        billingAddress = new { street = "123 Main St", city = "Kent", state = "OH", country = "US", zipCode = "44240" }
    };

    [TestMethod]
    public async Task FullLifecycle_Place_Pay_Fulfil_Refund_ReturnsIdentifiersAndCorrectState()
    {
        var user = AuthorizedClient(ApiTokenHelper.GetNormalUserToken());
        var admin = AuthorizedClient(ApiTokenHelper.GetAdminUserToken());

        var placeResponse = await user.PostAsJsonAsync("api/orders", new { items = new[] { new { catalogItemId = 1, quantity = 2 } } });
        Assert.AreEqual(HttpStatusCode.Created, placeResponse.StatusCode);
        var placed = JsonDocument.Parse(await placeResponse.Content.ReadAsStringAsync()).RootElement;
        var orderId = placed.GetProperty("orderId").GetInt32();
        Assert.IsTrue(orderId > 0);

        var payResponse = await user.PostAsJsonAsync($"api/orders/{orderId}/pay", new { card = ValidCard() });
        Assert.AreEqual(HttpStatusCode.OK, payResponse.StatusCode);
        var paid = JsonDocument.Parse(await payResponse.Content.ReadAsStringAsync()).RootElement;
        Assert.AreEqual("Authorized", paid.GetProperty("payment").GetProperty("status").GetString());

        // Only an administrator may fulfil.
        var fulfilAsUser = await user.PostAsync($"api/orders/{orderId}/fulfil", null);
        Assert.AreEqual(HttpStatusCode.Forbidden, fulfilAsUser.StatusCode);

        var fulfilResponse = await admin.PostAsync($"api/orders/{orderId}/fulfil", null);
        Assert.AreEqual(HttpStatusCode.OK, fulfilResponse.StatusCode);
        var fulfilled = JsonDocument.Parse(await fulfilResponse.Content.ReadAsStringAsync()).RootElement;
        Assert.AreEqual("Captured", fulfilled.GetProperty("payment").GetProperty("status").GetString());

        var refundResponse = await user.PostAsJsonAsync($"api/orders/{orderId}/refunds", new { idempotencyKey = $"itest-refund-{orderId}" });
        Assert.AreEqual(HttpStatusCode.Created, refundResponse.StatusCode);
        var refunded = JsonDocument.Parse(await refundResponse.Content.ReadAsStringAsync()).RootElement;
        Assert.IsTrue(refunded.GetProperty("refundId").GetInt32() > 0);
        Assert.AreEqual("Completed", refunded.GetProperty("status").GetString());
    }

    [TestMethod]
    public async Task DeclinedCard_ReturnsPaymentRequired()
    {
        var user = AuthorizedClient(ApiTokenHelper.GetNormalUserToken());

        var placeResponse = await user.PostAsJsonAsync("api/orders", new { items = new[] { new { catalogItemId = 2, quantity = 1 } } });
        var orderId = JsonDocument.Parse(await placeResponse.Content.ReadAsStringAsync()).RootElement.GetProperty("orderId").GetInt32();

        var card = new
        {
            number = FakePaymentGateway.DeclinedCardNumber,
            expiryMonth = 12,
            expiryYear = 2030,
            securityCode = "123",
            cardholderName = "Jane Doe",
            billingAddress = new { street = "123 Main St", city = "Kent", state = "OH", country = "US", zipCode = "44240" }
        };

        var payResponse = await user.PostAsJsonAsync($"api/orders/{orderId}/pay", new { card });
        Assert.AreEqual((HttpStatusCode)402, payResponse.StatusCode);
    }

    [TestMethod]
    public async Task DoubleClickPay_ReturnsTheSameAuthorizationWithoutError()
    {
        var user = AuthorizedClient(ApiTokenHelper.GetNormalUserToken());

        var placeResponse = await user.PostAsJsonAsync("api/orders", new { items = new[] { new { catalogItemId = 1, quantity = 1 } } });
        var orderId = JsonDocument.Parse(await placeResponse.Content.ReadAsStringAsync()).RootElement.GetProperty("orderId").GetInt32();

        var first = await user.PostAsJsonAsync($"api/orders/{orderId}/pay", new { card = ValidCard() });
        var second = await user.PostAsJsonAsync($"api/orders/{orderId}/pay", new { card = ValidCard() });

        var firstAuthId = JsonDocument.Parse(await first.Content.ReadAsStringAsync()).RootElement.GetProperty("payment").GetProperty("authorizationId").GetString();
        var secondAuthId = JsonDocument.Parse(await second.Content.ReadAsStringAsync()).RootElement.GetProperty("payment").GetProperty("authorizationId").GetString();

        Assert.AreEqual(HttpStatusCode.OK, second.StatusCode);
        Assert.AreEqual(firstAuthId, secondAuthId);
    }

    [TestMethod]
    public async Task OneShopperCannotSeeOrActOnAnotherShoppersOrder()
    {
        var owner = AuthorizedClient(ApiTokenHelper.GetNormalUserToken());
        var placeResponse = await owner.PostAsJsonAsync("api/orders", new { items = new[] { new { catalogItemId = 1, quantity = 1 } } });
        var orderId = JsonDocument.Parse(await placeResponse.Content.ReadAsStringAsync()).RootElement.GetProperty("orderId").GetInt32();

        // The admin account is a different identity - acting as "another shopper" here.
        var otherShopper = AuthorizedClient(ApiTokenHelper.GetAdminUserToken());
        var payAsOther = await otherShopper.PostAsJsonAsync($"api/orders/{orderId}/pay", new { card = ValidCard() });

        Assert.AreEqual(HttpStatusCode.NotFound, payAsOther.StatusCode);

        var myOrders = await otherShopper.GetAsync("api/my-orders");
        var orders = JsonDocument.Parse(await myOrders.Content.ReadAsStringAsync()).RootElement.GetProperty("orders");
        Assert.AreEqual(0, orders.GetArrayLength());
    }

    [TestMethod]
    public async Task Reconciliation_IsAdministratorOnly()
    {
        var user = AuthorizedClient(ApiTokenHelper.GetNormalUserToken());
        var admin = AuthorizedClient(ApiTokenHelper.GetAdminUserToken());

        var asUser = await user.GetAsync("api/reconciliation?from=2026-01-01T00:00:00Z&to=2026-01-02T00:00:00Z");
        Assert.AreEqual(HttpStatusCode.Forbidden, asUser.StatusCode);

        var asAdmin = await admin.GetAsync("api/reconciliation?from=2026-01-01T00:00:00Z&to=2026-01-02T00:00:00Z");
        Assert.AreEqual(HttpStatusCode.OK, asAdmin.StatusCode);
    }
}
