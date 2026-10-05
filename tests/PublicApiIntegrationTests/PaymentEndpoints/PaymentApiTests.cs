using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.eShopWeb.Infrastructure.Payments.PayPal;
using Microsoft.eShopWeb.PaymentTests;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PublicApiIntegrationTests.PaymentEndpoints;

/// <summary>
/// Drives every payment flow through the HTTP API, with PayPal replaced by an in-process fake (no network).
/// </summary>
[TestClass]
public class PaymentApiTests
{
    private const string CardJson = """
        {"number":"4111 1111 1111 1111","expiry":"2030-12","securityCode":"123","name":"Test Shopper",
         "billingAddress":{"addressLine1":"1 Main St","city":"San Jose","state":"CA","postalCode":"95131","countryCode":"US"}}
        """;
    private const string OrderJson = """
        {"items":[{"catalogItemId":1,"quantity":2},{"catalogItemId":3,"quantity":1}],
         "shipToAddress":{"street":"1 Main St","city":"Seattle","state":"WA","country":"US","zipCode":"98101"}}
        """;

    private static PaymentApiFactory _factory = null!;

    [ClassInitialize]
    public static void Init(TestContext _) => _factory = new PaymentApiFactory();

    [ClassCleanup]
    public static void Cleanup() => _factory.Dispose();

    [TestInitialize]
    public void Reset() => _factory.PayPal.Intercept = null;

    private static HttpClient Client(string? token)
    {
        var client = _factory.CreateClient();
        if (token is not null) client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private static HttpClient Shopper => Client(ApiTokenHelper.GetNormalUserToken());
    private static HttpClient Admin => Client(ApiTokenHelper.GetAdminUserToken());

    private static StringContent Json(string json) => new(json, Encoding.UTF8, "application/json");

    private static async Task<JsonNode> ReadAsync(HttpResponseMessage response, HttpStatusCode expected)
    {
        var body = await response.Content.ReadAsStringAsync();
        Assert.AreEqual(expected, response.StatusCode, body);
        return JsonNode.Parse(body)!;
    }

    private static async Task<int> PlaceOrderAsync(HttpClient client)
    {
        var created = await ReadAsync(await client.PostAsync("api/orders", Json(OrderJson)), HttpStatusCode.Created);
        Assert.AreEqual("AwaitingPayment", (string)created["order"]!["status"]!);
        Assert.AreEqual(51.0m, (decimal)created["order"]!["total"]!);
        return (int)created["orderId"]!;
    }

    [TestMethod]
    public async Task PayFulfilRefund_EndToEnd()
    {
        var orderId = await PlaceOrderAsync(Shopper);

        var paid = await ReadAsync(await Shopper.PostAsync($"api/orders/{orderId}/pay", Json($$"""{"card":{{CardJson}}}""")), HttpStatusCode.OK);
        Assert.AreEqual("PaymentAuthorized", (string)paid["order"]!["status"]!);
        Assert.AreEqual("Authorized", (string)paid["order"]!["payment"]!["status"]!);
        Assert.AreEqual("1111", (string)paid["order"]!["payment"]!["card"]!["lastDigits"]!);
        StringAssert.DoesNotMatch(paid.ToJsonString(), new System.Text.RegularExpressions.Regex("4111111111111111"));

        Assert.AreEqual(HttpStatusCode.Forbidden, (await Shopper.PostAsync($"api/orders/{orderId}/fulfil", null)).StatusCode);

        var fulfilled = await ReadAsync(await Admin.PostAsync($"api/orders/{orderId}/fulfil", null), HttpStatusCode.OK);
        var capture = fulfilled["order"]!["payment"]!["capture"]!;
        Assert.AreEqual("Fulfilled", (string)fulfilled["order"]!["status"]!);
        Assert.AreEqual(51.0m, (decimal)capture["amount"]!);
        Assert.AreEqual(2.27m, (decimal)capture["payPalFee"]!);
        Assert.AreEqual(48.73m, (decimal)capture["netAmount"]!);

        var refundRequest = new HttpRequestMessage(HttpMethod.Post, $"api/orders/{orderId}/refunds") { Content = Json("""{"amount":10.00}""") };
        refundRequest.Headers.Add("Idempotency-Key", "return-1");
        var refunded = await ReadAsync(await Shopper.SendAsync(refundRequest), HttpStatusCode.OK);
        var refundId = (int)refunded["refundId"]!;
        Assert.AreEqual("Succeeded", (string)refunded["refund"]!["status"]!);

        var replay = await ReadAsync(await Shopper.PostAsync($"api/orders/{orderId}/refunds", Json("""{"amount":10.00,"idempotencyKey":"return-1"}""")), HttpStatusCode.OK);
        Assert.AreEqual(refundId, (int)replay["refundId"]!);
        Assert.AreEqual(1, _factory.PayPal.Count("POST", "/refund$"));

        var tooMuch = await ReadAsync(await Shopper.PostAsync($"api/orders/{orderId}/refunds", Json("""{"amount":41.01,"idempotencyKey":"return-2"}""")), HttpStatusCode.UnprocessableEntity);
        Assert.AreEqual("exceeds_refundable", (string)tooMuch["code"]!);

        var noKey = await ReadAsync(await Shopper.PostAsync($"api/orders/{orderId}/refunds", Json("""{"amount":1.00}""")), HttpStatusCode.BadRequest);
        Assert.AreEqual("idempotency_key_required", (string)noKey["code"]!);

        var mine = await ReadAsync(await Shopper.GetAsync("api/my-orders"), HttpStatusCode.OK);
        var order = FindOrder(mine, orderId)!;
        Assert.AreEqual("PartiallyRefunded", (string)order["payment"]!["status"]!);
        Assert.AreEqual(10.0m, (decimal)order["payment"]!["refundedAmount"]!);
        Assert.AreEqual(41.0m, (decimal)order["payment"]!["refundableAmount"]!);

        var adminsView = await ReadAsync(await Admin.GetAsync("api/my-orders"), HttpStatusCode.OK);
        Assert.IsNull(FindOrder(adminsView, orderId), "another user's orders must not be listed");
    }

    [TestMethod]
    public async Task Cancel_ReleasesTheHold_AndIsOperatorOnly()
    {
        var orderId = await PlaceOrderAsync(Shopper);
        await ReadAsync(await Shopper.PostAsync($"api/orders/{orderId}/pay", Json($$"""{"card":{{CardJson}}}""")), HttpStatusCode.OK);

        Assert.AreEqual(HttpStatusCode.Forbidden, (await Shopper.PostAsync($"api/orders/{orderId}/cancel", null)).StatusCode);
        var cancelled = await ReadAsync(await Admin.PostAsync($"api/orders/{orderId}/cancel", null), HttpStatusCode.OK);

        Assert.AreEqual("Cancelled", (string)cancelled["order"]!["status"]!);
        Assert.AreEqual("Voided", (string)cancelled["order"]!["payment"]!["status"]!);
        Assert.AreEqual("VOIDED", (string)cancelled["order"]!["payment"]!["authorization"]!["status"]!);
    }

    [TestMethod]
    public async Task SavedCard_SaveListPayDelete_AndOwnership()
    {
        var saved = await ReadAsync(await Shopper.PostAsync("api/payment-methods", Json($$"""{"card":{{CardJson}}}""")), HttpStatusCode.Created);
        var methodId = (int)saved["paymentMethodId"]!;
        Assert.AreEqual("VISA", (string)saved["paymentMethod"]!["brand"]!);
        Assert.AreEqual("1111", (string)saved["paymentMethod"]!["lastDigits"]!);
        StringAssert.DoesNotMatch(saved.ToJsonString(), new System.Text.RegularExpressions.Regex("4111 ?1111 ?1111 ?1111|securityCode"));

        var list = await ReadAsync(await Shopper.GetAsync("api/payment-methods"), HttpStatusCode.OK);
        Assert.IsTrue(list["paymentMethods"]!.AsArray().Any(m => (int)m!["paymentMethodId"]! == methodId));
        var othersList = await ReadAsync(await Admin.GetAsync("api/payment-methods"), HttpStatusCode.OK);
        Assert.IsFalse(othersList["paymentMethods"]!.AsArray().Any(m => (int)m!["paymentMethodId"]! == methodId));

        // Another user can neither pay with it nor delete it.
        var adminOrder = await PlaceOrderAsync(Admin);
        Assert.AreEqual(HttpStatusCode.NotFound, (await Admin.PostAsync($"api/orders/{adminOrder}/pay", Json($$"""{"paymentMethodId":{{methodId}}}"""))).StatusCode);
        Assert.AreEqual(HttpStatusCode.NotFound, (await Admin.DeleteAsync($"api/payment-methods/{methodId}")).StatusCode);

        // The owner pays a second order with the saved card.
        var orderId = await PlaceOrderAsync(Shopper);
        var paid = await ReadAsync(await Shopper.PostAsync($"api/orders/{orderId}/pay", Json($$"""{"paymentMethodId":{{methodId}}}""")), HttpStatusCode.OK);
        Assert.AreEqual("Authorized", (string)paid["order"]!["payment"]!["status"]!);
        Assert.AreEqual(methodId, (int)paid["order"]!["payment"]!["card"]!["savedPaymentMethodId"]!);

        var deleted = await ReadAsync(await Shopper.DeleteAsync($"api/payment-methods/{methodId}"), HttpStatusCode.OK);
        Assert.IsTrue((bool)deleted["removedFromPayPal"]!);
        var after = await ReadAsync(await Shopper.GetAsync("api/payment-methods"), HttpStatusCode.OK);
        Assert.IsFalse(after["paymentMethods"]!.AsArray().Any(m => (int)m!["paymentMethodId"]! == methodId));

        var another = await PlaceOrderAsync(Shopper);
        Assert.AreEqual(HttpStatusCode.NotFound, (await Shopper.PostAsync($"api/orders/{another}/pay", Json($$"""{"paymentMethodId":{{methodId}}}"""))).StatusCode);
    }

    [TestMethod]
    public async Task Orders_AreInvisibleToOtherShoppers()
    {
        var orderId = await PlaceOrderAsync(Shopper);

        var response = await Admin.PostAsync($"api/orders/{orderId}/pay", Json($$"""{"card":{{CardJson}}}"""));

        Assert.AreEqual(HttpStatusCode.NotFound, response.StatusCode);
    }

    [TestMethod]
    public async Task Pay_RequiresExactlyOneSource()
    {
        var orderId = await PlaceOrderAsync(Shopper);

        var both = await ReadAsync(await Shopper.PostAsync($"api/orders/{orderId}/pay", Json($$"""{"card":{{CardJson}},"paymentMethodId":1}""")), HttpStatusCode.BadRequest);
        var neither = await ReadAsync(await Shopper.PostAsync($"api/orders/{orderId}/pay", Json("{}")), HttpStatusCode.BadRequest);

        Assert.AreEqual("payment_source_required", (string)both["code"]!);
        Assert.AreEqual("payment_source_required", (string)neither["code"]!);
    }

    [TestMethod]
    public async Task Declined_IsReportedWithPayPalsReason()
    {
        var orderId = await PlaceOrderAsync(Shopper);
        var declined = CardJson.Replace("4111 1111 1111 1111", FakePayPal.DeclinedCardNumber);

        var body = await ReadAsync(await Shopper.PostAsync($"api/orders/{orderId}/pay", Json($$"""{"card":{{declined}}}""")), HttpStatusCode.UnprocessableEntity);

        Assert.AreEqual("paypal_rejected", (string)body["code"]!);
        Assert.AreEqual("INSTRUMENT_DECLINED", (string)body["payPalIssue"]!);
        Assert.AreEqual("fake-debug-422", (string)body["payPalDebugId"]!);
    }

    [TestMethod]
    public async Task SilentPayPal_AnswersWithinBudget_SayingPayPalDidNotRespond()
    {
        var orderId = await PlaceOrderAsync(Shopper);
        _factory.PayPal.Intercept = async (req, _, ct) =>
        {
            if (req.RequestUri!.AbsolutePath != "/v1/oauth2/token") await Task.Delay(Timeout.Infinite, ct);
            return null;
        };

        var watch = System.Diagnostics.Stopwatch.StartNew();
        var response = await Shopper.PostAsync($"api/orders/{orderId}/pay", Json($$"""{"card":{{CardJson}}}"""));
        watch.Stop();
        var body = await ReadAsync(response, HttpStatusCode.GatewayTimeout);

        StringAssert.Contains((string)body["message"]!, "PayPal did not respond");
        Assert.IsTrue(watch.Elapsed < TimeSpan.FromSeconds(30), $"took {watch.Elapsed}");
    }

    [TestMethod]
    public async Task Endpoints_RequireAToken()
    {
        var anonymous = Client(null);

        Assert.AreEqual(HttpStatusCode.Unauthorized, (await anonymous.PostAsync("api/orders", Json(OrderJson))).StatusCode);
        Assert.AreEqual(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("api/my-orders")).StatusCode);
        Assert.AreEqual(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("api/payment-methods")).StatusCode);
    }

    [TestMethod]
    public async Task Reconciliation_IsOperatorOnly_AndValidatesDates()
    {
        var at = DateTimeOffset.UtcNow.AddDays(-1);
        _factory.PayPal.ReportTransactions.Add(FakePayPal.Transaction("UNKNOWN0000000001", at, 3m));

        Assert.AreEqual(HttpStatusCode.Forbidden, (await Shopper.GetAsync("api/reconciliation?from=2026-01-01T00:00:00Z&to=2026-01-02T00:00:00Z")).StatusCode);
        await ReadAsync(await Admin.GetAsync("api/reconciliation?from=yesterday&to=2026-01-02T00:00:00Z"), HttpStatusCode.BadRequest);

        var from = Uri.EscapeDataString(at.AddHours(-1).ToString("o"));
        var to = Uri.EscapeDataString(at.AddHours(1).ToString("o"));
        var report = await ReadAsync(await Admin.GetAsync($"api/reconciliation?from={from}&to={to}"), HttpStatusCode.OK);

        Assert.IsTrue((bool)report["complete"]!);
        Assert.IsTrue(report["payPalOnly"]!.AsArray().Any(t => (string)t!["transactionId"]! == "UNKNOWN0000000001"));
    }

    private static JsonNode? FindOrder(JsonNode myOrders, int orderId)
    {
        foreach (var o in myOrders["orders"]!.AsArray())
            if ((int)o!["orderId"]! == orderId) return o;
        return null;
    }
}

public sealed class PaymentApiFactory : WebApplicationFactory<Program>
{
    public FakePayPal PayPal { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureTestServices(services =>
            services.AddHttpClient(PayPalServiceCollectionExtensions.HttpClientName).ConfigurePrimaryHttpMessageHandler(() => PayPal));
    }
}

internal static class JsonArrayExtensions
{
    public static bool Any(this JsonArray array, Func<JsonNode?, bool> predicate)
    {
        foreach (var item in array) if (predicate(item)) return true;
        return false;
    }
}
