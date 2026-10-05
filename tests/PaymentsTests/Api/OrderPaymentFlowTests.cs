using System.Diagnostics;
using System.Net;
using System.Text.Json.Nodes;
using Microsoft.eShopWeb.PaymentsTests.Fakes;

namespace Microsoft.eShopWeb.PaymentsTests.Api;

public class OrderPaymentFlowTests : IClassFixture<PaymentsApiFactory>
{
    private const string Admin = "Administrators";
    private readonly PaymentsApiFactory _factory;

    public OrderPaymentFlowTests(PaymentsApiFactory factory)
    {
        _factory = factory;
    }

    private static string NewShopper() => $"shopper-{Guid.NewGuid():N}@example.test";

    [Fact]
    public async Task Authorize_capture_and_refund_a_card_payment_end_to_end()
    {
        var shopper = _factory.ClientFor(NewShopper());
        var admin = _factory.ClientFor("admin@microsoft.com", Admin);

        var orderId = await shopper.PlaceOrderAsync((1, 2), (2, 1)); // 2 x 19.50 + 8.50 = 47.50

        var pay = await shopper.PostJsonAsync($"api/orders/{orderId}/pay", new { card = ApiCalls.Card() });
        var paid = await pay.ReadJsonAsync();
        Assert.Equal(HttpStatusCode.OK, pay.StatusCode);
        Assert.Equal("PaymentAuthorized", paid["orderStatus"]!.GetValue<string>());
        var payment = paid["order"]!["payment"]!;
        Assert.Equal("Authorized", payment["status"]!.GetValue<string>());
        Assert.Equal(47.50m, payment["amount"].Dec());
        Assert.Equal("1111", payment["cardLastDigits"]!.GetValue<string>());

        // The hold PayPal was asked for equals the order total to the cent, with intent AUTHORIZE.
        var create = _factory.PayPal.Calls("POST", "/v2/checkout/orders").Last();
        Assert.Equal("AUTHORIZE", create.Json!["intent"]!.GetValue<string>());
        Assert.Equal("47.50", create.Json["purchase_units"]![0]!["amount"]!["value"]!.GetValue<string>());
        Assert.Equal("USD", create.Json["purchase_units"]![0]!["amount"]!["currency_code"]!.GetValue<string>());
        Assert.False(string.IsNullOrEmpty(create.Header("PayPal-Request-Id")));
        var authorizationId = payment["authorization"]!["id"]!.GetValue<string>();
        Assert.Empty(_factory.PayPal.Calls("POST", $"/v2/payments/authorizations/{authorizationId}/capture")); // held, not taken

        var fulfil = await admin.PostAsync($"api/orders/{orderId}/fulfil", null);
        var fulfilled = await fulfil.ReadJsonAsync();
        Assert.Equal(HttpStatusCode.OK, fulfil.StatusCode);
        Assert.Equal("Fulfilled", fulfilled["orderStatus"]!.GetValue<string>());
        var capture = fulfilled["order"]!["payment"]!["capture"]!;
        Assert.Equal(47.50m, capture["amount"].Dec());
        Assert.Equal(2.15m, capture["payPalFee"].Dec());   // what the (fake) PayPal reported
        Assert.Equal(45.35m, capture["netAmount"].Dec());
        var captureCall = _factory.PayPal.Calls("POST", $"/v2/payments/authorizations/{authorizationId}/capture").Single();
        Assert.True(captureCall.Json!["final_capture"]!.GetValue<bool>());
        Assert.Equal("47.50", captureCall.Json["amount"]!["value"]!.GetValue<string>());

        var refund1 = await shopper.PostJsonAsync($"api/orders/{orderId}/refunds", new { amount = 10m, idempotencyKey = "r-1" });
        var r1 = await refund1.ReadJsonAsync();
        Assert.Equal(HttpStatusCode.Created, refund1.StatusCode);
        Assert.True(r1["refundId"]!.GetValue<int>() > 0);
        Assert.Equal("Completed", r1["status"]!.GetValue<string>());
        Assert.Equal("PartiallyRefunded", r1["payment"]!["status"]!.GetValue<string>());

        // Same key again: no second refund at PayPal.
        var replay = await shopper.PostJsonAsync($"api/orders/{orderId}/refunds", new { amount = 10m, idempotencyKey = "r-1" });
        var rr = await replay.ReadJsonAsync();
        Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
        Assert.True(rr["replayed"]!.GetValue<bool>());
        Assert.Equal(r1["refundId"]!.GetValue<int>(), rr["refundId"]!.GetValue<int>());

        // A distinct partial refund of the same capture is legitimate.
        var refund2 = await shopper.PostJsonAsync($"api/orders/{orderId}/refunds", new { amount = 20m, idempotencyKey = "r-2" });
        Assert.Equal(HttpStatusCode.Created, refund2.StatusCode);
        Assert.Equal(2, _factory.PayPal.Calls("POST", "/v2/payments/captures/.*/refund").Count(c => c.Path.Contains(capture["id"]!.GetValue<string>())));

        // Never beyond what was captured.
        var over = await shopper.PostJsonAsync($"api/orders/{orderId}/refunds", new { amount = 17.51m, idempotencyKey = "r-3" });
        Assert.Equal(HttpStatusCode.Conflict, over.StatusCode);
        Assert.Equal("REFUND_EXCEEDS_CAPTURED", (await over.ReadJsonAsync())["code"]!.GetValue<string>());

        var rest = await shopper.PostJsonAsync($"api/orders/{orderId}/refunds", new { idempotencyKey = "r-4" });
        var restJson = await rest.ReadJsonAsync();
        Assert.Equal(HttpStatusCode.Created, rest.StatusCode);
        Assert.Equal(17.50m, restJson["amount"].Dec());
        Assert.Equal("Refunded", restJson["payment"]!["status"]!.GetValue<string>());
        Assert.Equal(0m, restJson["payment"]!["refundableAmount"].Dec());

        var mine = await (await shopper.GetAsync("api/my-orders")).ReadJsonAsync();
        var listed = mine["orders"]!.AsArray().Single(o => o!["orderId"]!.GetValue<int>() == orderId)!;
        Assert.Equal("Fulfilled", listed["status"]!.GetValue<string>());
        Assert.Equal(47.50m, listed["payment"]!["refundedAmount"].Dec());
        Assert.Equal(3, listed["payment"]!["refunds"]!.AsArray().Count(r => r!["status"]!.GetValue<string>() == "Completed"));
    }

    [Fact]
    public async Task Repeated_pay_and_fulfil_never_authorize_or_capture_twice()
    {
        var shopper = _factory.ClientFor(NewShopper());
        var admin = _factory.ClientFor("admin@microsoft.com", Admin);
        var orderId = await shopper.PlaceOrderAsync((3, 1));
        var before = _factory.PayPal.Calls("POST", "/v2/checkout/orders").Count();

        var pays = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => shopper.PostJsonAsync($"api/orders/{orderId}/pay", new { card = ApiCalls.Card() })));
        Assert.Contains(pays, p => p.StatusCode == HttpStatusCode.OK);
        Assert.All(pays, p => Assert.True(p.StatusCode is HttpStatusCode.OK or HttpStatusCode.Conflict, p.StatusCode.ToString()));
        Assert.Equal(before + 1, _factory.PayPal.Calls("POST", "/v2/checkout/orders").Count());

        var again = await shopper.PostJsonAsync($"api/orders/{orderId}/pay", new { card = ApiCalls.Card() });
        Assert.True((await again.ReadJsonAsync())["alreadyDone"]!.GetValue<bool>());

        var fulfils = await Task.WhenAll(Enumerable.Range(0, 3).Select(_ => admin.PostAsync($"api/orders/{orderId}/fulfil", null)));
        Assert.All(fulfils, f => Assert.True(f.StatusCode is HttpStatusCode.OK or HttpStatusCode.Conflict, f.StatusCode.ToString()));
        var authorizationId = (await (await shopper.GetAsync("api/my-orders")).ReadJsonAsync())["orders"]!.AsArray()
            .Single(o => o!["orderId"]!.GetValue<int>() == orderId)!["payment"]!["authorization"]!["id"]!.GetValue<string>();
        Assert.Single(_factory.PayPal.Calls("POST", $"/v2/payments/authorizations/{authorizationId}/capture"));
    }

    [Fact]
    public async Task Cancel_before_fulfilment_releases_the_hold()
    {
        var shopper = _factory.ClientFor(NewShopper());
        var admin = _factory.ClientFor("admin@microsoft.com", Admin);
        var orderId = await shopper.PlaceOrderAsync((4, 1));
        await shopper.PostJsonAsync($"api/orders/{orderId}/pay", new { card = ApiCalls.Card() });

        var cancel = await admin.PostAsync($"api/orders/{orderId}/cancel", null);
        var json = await cancel.ReadJsonAsync();
        Assert.Equal(HttpStatusCode.OK, cancel.StatusCode);
        Assert.Equal("Cancelled", json["orderStatus"]!.GetValue<string>());
        Assert.Equal("Voided", json["order"]!["payment"]!["status"]!.GetValue<string>());
        Assert.Equal("VOIDED", json["order"]!["payment"]!["authorization"]!["status"]!.GetValue<string>());
        Assert.Null(json["order"]!["payment"]!["capture"]);

        var fulfil = await admin.PostAsync($"api/orders/{orderId}/fulfil", null);
        Assert.Equal(HttpStatusCode.Conflict, fulfil.StatusCode);
        var refund = await shopper.PostJsonAsync($"api/orders/{orderId}/refunds", new { idempotencyKey = "x" });
        Assert.Equal(HttpStatusCode.Conflict, refund.StatusCode);
    }

    [Fact]
    public async Task Cancel_after_fulfilment_is_refused_in_favour_of_a_refund()
    {
        var shopper = _factory.ClientFor(NewShopper());
        var admin = _factory.ClientFor("admin@microsoft.com", Admin);
        var orderId = await shopper.PlaceOrderAsync((4, 1));
        await shopper.PostJsonAsync($"api/orders/{orderId}/pay", new { card = ApiCalls.Card() });
        await admin.PostAsync($"api/orders/{orderId}/fulfil", null);

        var cancel = await admin.PostAsync($"api/orders/{orderId}/cancel", null);
        Assert.Equal(HttpStatusCode.Conflict, cancel.StatusCode);
        Assert.Equal("ORDER_FULFILLED", (await cancel.ReadJsonAsync())["code"]!.GetValue<string>());
    }

    [Fact]
    public async Task A_declined_card_is_reported_and_the_shopper_can_pay_again()
    {
        var shopper = _factory.ClientFor(NewShopper());
        var orderId = await shopper.PlaceOrderAsync((1, 1));

        var declined = await shopper.PostJsonAsync($"api/orders/{orderId}/pay", new { card = ApiCalls.Card(FakePayPal.DeclinedCard) });
        var body = await declined.ReadJsonAsync();
        Assert.Equal(HttpStatusCode.UnprocessableEntity, declined.StatusCode);
        Assert.Equal("PAYPAL_REJECTED", body["code"]!.GetValue<string>());
        Assert.Contains("INSTRUMENT_DECLINED", body["issues"]!.AsArray().Select(i => i!.GetValue<string>()));
        Assert.Equal("fake-debug-instrument_declined", body["payPalDebugId"]!.GetValue<string>());

        var retry = await shopper.PostJsonAsync($"api/orders/{orderId}/pay", new { card = ApiCalls.Card() });
        Assert.Equal(HttpStatusCode.OK, retry.StatusCode);
    }

    [Fact]
    public async Task Order_needing_a_second_step_is_authorized_explicitly()
    {
        using var factory = new PaymentsApiFactory();
        factory.PayPal.AuthorizeInSecondStep = true;
        var shopper = factory.ClientFor(NewShopper());
        var orderId = await shopper.PlaceOrderAsync((2, 2));

        var pay = await shopper.PostJsonAsync($"api/orders/{orderId}/pay", new { card = ApiCalls.Card() });
        Assert.Equal(HttpStatusCode.OK, pay.StatusCode);
        Assert.Single(factory.PayPal.Calls("POST", "/v2/checkout/orders/.*/authorize"));
        Assert.Equal("Authorized", (await pay.ReadJsonAsync())["order"]!["payment"]!["status"]!.GetValue<string>());
    }

    [Fact]
    public async Task A_browser_challenge_is_reported_not_followed()
    {
        using var factory = new PaymentsApiFactory();
        factory.PayPal.RequirePayerAction = true;
        var shopper = factory.ClientFor(NewShopper());
        var orderId = await shopper.PlaceOrderAsync((2, 1));

        var pay = await shopper.PostJsonAsync($"api/orders/{orderId}/pay", new { card = ApiCalls.Card() });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, pay.StatusCode);
        Assert.Equal("PAYER_ACTION_REQUIRED", (await pay.ReadJsonAsync())["code"]!.GetValue<string>());
    }

    [Fact]
    public async Task Shoppers_cannot_see_or_act_on_each_others_orders_and_operator_actions_need_the_admin_role()
    {
        var owner = _factory.ClientFor(NewShopper());
        var other = _factory.ClientFor(NewShopper());
        var anonymous = _factory.CreateClient();
        var orderId = await owner.PlaceOrderAsync((1, 1));

        Assert.Equal(HttpStatusCode.NotFound, (await other.PostJsonAsync($"api/orders/{orderId}/pay", new { card = ApiCalls.Card() })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.PostJsonAsync($"api/orders/{orderId}/pay", new { card = ApiCalls.Card() })).StatusCode);
        Assert.DoesNotContain((await (await other.GetAsync("api/my-orders")).ReadJsonAsync())["orders"]!.AsArray(), o => o!["orderId"]!.GetValue<int>() == orderId);

        await owner.PostJsonAsync($"api/orders/{orderId}/pay", new { card = ApiCalls.Card() });
        Assert.Equal(HttpStatusCode.Forbidden, (await owner.PostAsync($"api/orders/{orderId}/fulfil", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await owner.PostAsync($"api/orders/{orderId}/cancel", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await owner.GetAsync("api/reconciliation?from=2026-01-01T00:00:00Z&to=2026-01-02T00:00:00Z")).StatusCode);

        await _factory.ClientFor("admin@microsoft.com", Admin).PostAsync($"api/orders/{orderId}/fulfil", null);
        Assert.Equal(HttpStatusCode.NotFound, (await other.PostJsonAsync($"api/orders/{orderId}/refunds", new { idempotencyKey = "steal" })).StatusCode);
    }

    [Fact]
    public async Task Order_placement_validates_items_and_uses_catalog_prices()
    {
        var shopper = _factory.ClientFor(NewShopper());
        var bad = await shopper.PostAsync("api/orders", new StringContent($$"""{"items":[{"catalogItemId":99999,"quantity":1}],"shipToAddress":{{ApiCalls.Shipping}}}""", System.Text.Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);

        var empty = await shopper.PostAsync("api/orders", new StringContent($$"""{"items":[],"shipToAddress":{{ApiCalls.Shipping}}}""", System.Text.Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.BadRequest, empty.StatusCode);

        var ok = await shopper.PostAsync("api/orders", new StringContent($$"""{"items":[{"catalogItemId":1,"quantity":1},{"catalogItemId":1,"quantity":2}],"shipToAddress":{{ApiCalls.Shipping}}}""", System.Text.Encoding.UTF8, "application/json"));
        var json = await ok.ReadJsonAsync();
        Assert.Equal(HttpStatusCode.Created, ok.StatusCode);
        Assert.Equal("AwaitingPayment", json["order"]!["status"]!.GetValue<string>());
        Assert.Equal(58.50m, json["order"]!["total"].Dec());
    }

    [Fact]
    public async Task Card_details_never_reach_the_logs()
    {
        using var factory = new PaymentsApiFactory();
        var shopper = factory.ClientFor(NewShopper());
        var orderId = await shopper.PlaceOrderAsync((1, 1));
        var pay = await shopper.PostJsonAsync($"api/orders/{orderId}/pay", new { card = ApiCalls.Card() });
        var save = await shopper.PostJsonAsync("api/payment-methods", new { card = ApiCalls.Card() });
        Assert.Equal(HttpStatusCode.OK, pay.StatusCode);
        Assert.Equal(HttpStatusCode.Created, save.StatusCode);

        Assert.Contains("PayPal CreateOrder", factory.Logs.All);
        Assert.DoesNotContain(ApiCalls.TestVisa, factory.Logs.All);
        Assert.DoesNotContain("\"security_code\"", factory.Logs.All);
        Assert.Contains(factory.PayPal.Calls("POST", "/v2/checkout/orders"), c => c.Body!.Contains(ApiCalls.TestVisa)); // it did go to PayPal
    }
}
