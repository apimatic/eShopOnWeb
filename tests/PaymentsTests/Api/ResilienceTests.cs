using System.Diagnostics;
using System.Net;
using Microsoft.eShopWeb.PaymentsTests.Fakes;

namespace Microsoft.eShopWeb.PaymentsTests.Api;

/// <summary>Unknown outcomes, timeouts and stale authorizations — each with its own fake PayPal.</summary>
public class ResilienceTests
{
    private const string Admin = "Administrators";

    private static string NewShopper() => $"shopper-{Guid.NewGuid():N}@example.test";

    private static async Task<(PaymentsApiFactory Factory, HttpClient Shopper, HttpClient Admin, int OrderId)> AuthorizedOrderAsync(Action<PaymentsApiFactory>? configure = null)
    {
        var factory = new PaymentsApiFactory();
        configure?.Invoke(factory);
        var shopper = factory.ClientFor(NewShopper());
        var admin = factory.ClientFor("admin@microsoft.com", Admin);
        var orderId = await shopper.PlaceOrderAsync((1, 2), (2, 1));
        var pay = await shopper.PostJsonAsync($"api/orders/{orderId}/pay", new { card = ApiCalls.Card() });
        Assert.Equal(HttpStatusCode.OK, pay.StatusCode);
        return (factory, shopper, admin, orderId);
    }

    [Fact]
    public async Task Capture_whose_response_is_lost_is_settled_by_re_reading_PayPal()
    {
        var (factory, _, admin, orderId) = await AuthorizedOrderAsync();
        using var _f = factory;
        factory.PayPal.InjectFault("POST", "/v2/payments/authorizations/.*/capture", Fault.DropAfterProcessing);

        var fulfil = await admin.PostAsync($"api/orders/{orderId}/fulfil", null);
        var json = await fulfil.ReadJsonAsync();

        Assert.Equal(HttpStatusCode.OK, fulfil.StatusCode);
        Assert.Equal("Fulfilled", json["orderStatus"]!.GetValue<string>());
        Assert.Equal(2.15m, json["order"]!["payment"]!["capture"]!["payPalFee"].Dec());
        Assert.Single(factory.PayPal.Calls("POST", "/v2/payments/authorizations/.*/capture")); // never resent
        Assert.NotEmpty(factory.PayPal.Calls("GET", "/v2/checkout/orders/.*"));               // settled by a re-read
    }

    [Fact]
    public async Task Authorization_whose_response_is_lost_is_settled_by_replaying_the_same_request_id()
    {
        using var factory = new PaymentsApiFactory();
        factory.PayPal.InjectFault("POST", "/v2/checkout/orders", Fault.DropAfterProcessing);
        var shopper = factory.ClientFor(NewShopper());
        var orderId = await shopper.PlaceOrderAsync((1, 1));

        var pay = await shopper.PostJsonAsync($"api/orders/{orderId}/pay", new { card = ApiCalls.Card() });

        Assert.Equal(HttpStatusCode.OK, pay.StatusCode);
        var creates = factory.PayPal.Calls("POST", "/v2/checkout/orders").ToList();
        Assert.Equal(2, creates.Count);
        Assert.Equal(creates[0].Header("PayPal-Request-Id"), creates[1].Header("PayPal-Request-Id"));
        Assert.Equal("Authorized", (await pay.ReadJsonAsync())["order"]!["payment"]!["status"]!.GetValue<string>());
    }

    [Fact]
    public async Task Refund_whose_response_is_lost_is_replayed_under_the_same_request_id_and_counted_once()
    {
        var (factory, shopper, admin, orderId) = await AuthorizedOrderAsync();
        using var _f = factory;
        await admin.PostAsync($"api/orders/{orderId}/fulfil", null);
        factory.PayPal.InjectFault("POST", "/v2/payments/captures/.*/refund", Fault.DropAfterProcessing);

        var refund = await shopper.PostJsonAsync($"api/orders/{orderId}/refunds", new { amount = 40m, idempotencyKey = "lost-response" });
        var json = await refund.ReadJsonAsync();

        Assert.Equal(HttpStatusCode.Created, refund.StatusCode);
        Assert.Equal("Completed", json["status"]!.GetValue<string>());
        var calls = factory.PayPal.Calls("POST", "/v2/payments/captures/.*/refund").ToList();
        Assert.Equal(2, calls.Count);
        Assert.Equal(calls[0].Header("PayPal-Request-Id"), calls[1].Header("PayPal-Request-Id"));
        Assert.Equal(7.50m, json["payment"]!["refundableAmount"].Dec());
    }

    [Fact]
    public async Task An_unresponsive_PayPal_yields_a_did_not_respond_error_within_the_budget()
    {
        using var factory = new PaymentsApiFactory();
        factory.Resilience.RequestBudget = TimeSpan.FromSeconds(3);
        factory.Resilience.SettlementReserve = TimeSpan.FromSeconds(1);
        factory.Resilience.AttemptTimeout = TimeSpan.FromSeconds(30);
        var shopper = factory.ClientFor(NewShopper());
        var orderId = await shopper.PlaceOrderAsync((1, 1));
        factory.PayPal.InjectFault("POST", "/v2/checkout/orders", Fault.Hang, times: 2); // the attempt and its settlement replay

        var watch = Stopwatch.StartNew();
        var pay = await shopper.PostJsonAsync($"api/orders/{orderId}/pay", new { card = ApiCalls.Card() });
        watch.Stop();

        var json = await pay.ReadJsonAsync();
        Assert.Equal(HttpStatusCode.GatewayTimeout, pay.StatusCode);
        Assert.Equal("PAYPAL_TIMEOUT", json["code"]!.GetValue<string>());
        Assert.StartsWith("PayPal did not respond", json["message"]!.GetValue<string>());
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(10), $"took {watch.Elapsed}");

        // The attempt is remembered as unsettled; once PayPal answers, the same request id is replayed.
        var mine = await (await shopper.GetAsync("api/my-orders")).ReadJsonAsync();
        Assert.Equal("AuthorizationPending", mine["orders"]![0]!["payment"]!["status"]!.GetValue<string>());
        var retry = await shopper.PostJsonAsync($"api/orders/{orderId}/pay", new { card = ApiCalls.Card() });
        Assert.Equal(HttpStatusCode.OK, retry.StatusCode);
        Assert.Single(factory.PayPal.Calls("POST", "/v2/checkout/orders").Select(c => c.Header("PayPal-Request-Id")).Distinct());
    }

    [Fact]
    public async Task Default_budget_keeps_a_hung_PayPal_under_30_seconds()
    {
        using var factory = new PaymentsApiFactory();
        var shopper = factory.ClientFor(NewShopper());
        var orderId = await shopper.PlaceOrderAsync((1, 1));
        factory.PayPal.InjectFault("POST", "/v2/checkout/orders", Fault.Hang, times: 10);

        var watch = Stopwatch.StartNew();
        var pay = await shopper.PostJsonAsync($"api/orders/{orderId}/pay", new { card = ApiCalls.Card() });
        watch.Stop();

        Assert.Equal(HttpStatusCode.GatewayTimeout, pay.StatusCode);
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(30), $"took {watch.Elapsed}");
    }

    [Fact]
    public async Task A_stale_authorization_is_renewed_before_capture()
    {
        var (factory, _, admin, orderId) = await AuthorizedOrderAsync(f => f.PayPal.AuthorizationCreateTime = DateTimeOffset.UtcNow.AddDays(-5));
        using var _f = factory;

        var fulfil = await admin.PostAsync($"api/orders/{orderId}/fulfil", null);
        var json = await fulfil.ReadJsonAsync();

        Assert.Equal(HttpStatusCode.OK, fulfil.StatusCode);
        var authorization = json["order"]!["payment"]!["authorization"]!;
        Assert.NotEqual(authorization["originalId"]!.GetValue<string>(), authorization["id"]!.GetValue<string>());
        Assert.NotNull(authorization["reauthorizedAt"]);
        Assert.Single(factory.PayPal.Calls("POST", "/v2/payments/authorizations/.*/reauthorize"));
        var capture = Assert.Single(factory.PayPal.Calls("POST", "/v2/payments/authorizations/.*/capture"));
        Assert.Contains(authorization["id"]!.GetValue<string>(), capture.Path);
    }

    [Fact]
    public async Task A_stale_authorization_PayPal_refuses_to_renew_is_still_captured_while_valid()
    {
        var (factory, _, admin, orderId) = await AuthorizedOrderAsync(f =>
        {
            f.PayPal.AuthorizationCreateTime = DateTimeOffset.UtcNow.AddDays(-5);
            f.PayPal.ReauthorizeRefused = true;
        });
        using var _f = factory;

        var fulfil = await admin.PostAsync($"api/orders/{orderId}/fulfil", null);

        Assert.Equal(HttpStatusCode.OK, fulfil.StatusCode);
        Assert.Contains("refused to renew", factory.Logs.All);
    }

    [Fact]
    public async Task An_expired_authorization_is_reported_in_terms_an_operator_can_act_on()
    {
        var (factory, shopper, admin, orderId) = await AuthorizedOrderAsync();
        using var _f = factory;
        factory.Clock.Offset = TimeSpan.FromDays(31);

        var fulfil = await admin.PostAsync($"api/orders/{orderId}/fulfil", null);
        var json = await fulfil.ReadJsonAsync();

        Assert.Equal(HttpStatusCode.Conflict, fulfil.StatusCode);
        Assert.Equal("AUTHORIZATION_EXPIRED", json["code"]!.GetValue<string>());
        var message = json["message"]!.GetValue<string>();
        Assert.Contains("can no longer be captured or renewed", message);
        Assert.Contains("No money was taken", message);
        Assert.Contains($"/api/orders/{orderId}/pay", message);
        Assert.Empty(factory.PayPal.Calls("POST", "/v2/payments/authorizations/.*/capture"));

        // The shopper can pay again.
        factory.Clock.Offset = TimeSpan.Zero;
        var pay = await shopper.PostJsonAsync($"api/orders/{orderId}/pay", new { card = ApiCalls.Card() });
        Assert.Equal(HttpStatusCode.OK, pay.StatusCode);
    }

    [Fact]
    public async Task An_authorization_voided_at_PayPal_is_reported_and_not_captured()
    {
        var (factory, shopper, admin, orderId) = await AuthorizedOrderAsync();
        using var _f = factory;
        var mine = await (await shopper.GetAsync("api/my-orders")).ReadJsonAsync();
        var authorizationId = mine["orders"]![0]!["payment"]!["authorization"]!["id"]!.GetValue<string>();
        factory.PayPal.SetAuthorizationStatus(authorizationId, "VOIDED"); // released behind the app's back

        var fulfil = await admin.PostAsync($"api/orders/{orderId}/fulfil", null);

        Assert.Equal(HttpStatusCode.Conflict, fulfil.StatusCode);
        var json = await fulfil.ReadJsonAsync();
        Assert.Equal("AUTHORIZATION_EXPIRED", json["code"]!.GetValue<string>());
        Assert.Contains("VOIDED", json["message"]!.GetValue<string>());
        Assert.Empty(factory.PayPal.Calls("POST", "/v2/payments/authorizations/.*/capture"));
    }
}
