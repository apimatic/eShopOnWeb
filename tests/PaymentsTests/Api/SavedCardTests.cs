using System.Net;
using Microsoft.eShopWeb.PaymentsTests.Fakes;

namespace Microsoft.eShopWeb.PaymentsTests.Api;

public class SavedCardTests : IClassFixture<PaymentsApiFactory>
{
    private readonly PaymentsApiFactory _factory;

    public SavedCardTests(PaymentsApiFactory factory)
    {
        _factory = factory;
    }

    private static string NewShopper() => $"shopper-{Guid.NewGuid():N}@example.test";

    [Fact]
    public async Task Save_list_pay_with_and_delete_a_card()
    {
        var shopper = _factory.ClientFor(NewShopper());
        var admin = _factory.ClientFor("admin@microsoft.com", "Administrators");

        var save = await shopper.PostJsonAsync("api/payment-methods", new { card = ApiCalls.Card(), alias = "Work Visa" });
        var saved = await save.ReadJsonAsync();
        Assert.Equal(HttpStatusCode.Created, save.StatusCode);
        var paymentMethodId = saved["paymentMethodId"]!.GetValue<int>();
        Assert.Equal("1111", saved["paymentMethod"]!["last4"]!.GetValue<string>());
        Assert.Equal("VISA", saved["paymentMethod"]!["brand"]!.GetValue<string>());
        Assert.Equal("2030-12", saved["paymentMethod"]!["expiry"]!.GetValue<string>());
        Assert.DoesNotContain(ApiCalls.TestVisa, saved.ToJsonString());
        Assert.DoesNotContain("pt0", saved.ToJsonString()); // the PayPal vault token stays server-side

        var list = await (await shopper.GetAsync("api/payment-methods")).ReadJsonAsync();
        Assert.Single(list["paymentMethods"]!.AsArray());

        // Reuse it for a later order: the order is created with the vault id, not card details.
        var orderId = await shopper.PlaceOrderAsync((3, 1), (5, 3)); // 12 + 3 x 8.50 = 37.50
        var pay = await shopper.PostJsonAsync($"api/orders/{orderId}/pay", new { paymentMethodId });
        var paid = await pay.ReadJsonAsync();
        Assert.Equal(HttpStatusCode.OK, pay.StatusCode);
        Assert.Equal(paymentMethodId, paid["order"]!["payment"]!["paymentMethodId"]!.GetValue<int>());
        var create = _factory.PayPal.Calls("POST", "/v2/checkout/orders").Last();
        Assert.NotNull(create.Json!["payment_source"]!["card"]!["vault_id"]);
        Assert.Null(create.Json["payment_source"]!["card"]!["number"]);
        Assert.Equal("37.50", create.Json["purchase_units"]![0]!["amount"]!["value"]!.GetValue<string>());

        var fulfil = await admin.PostAsync($"api/orders/{orderId}/fulfil", null);
        Assert.Equal(HttpStatusCode.OK, fulfil.StatusCode);

        var delete = await shopper.DeleteAsync($"api/payment-methods/{paymentMethodId}");
        Assert.Equal(HttpStatusCode.OK, delete.StatusCode);
        Assert.Single(_factory.PayPal.Calls("DELETE", "/v3/vault/payment-tokens/.*"));
        Assert.Empty((await (await shopper.GetAsync("api/payment-methods")).ReadJsonAsync())["paymentMethods"]!.AsArray());

        var next = await shopper.PlaceOrderAsync((1, 1));
        var payDeleted = await shopper.PostJsonAsync($"api/orders/{next}/pay", new { paymentMethodId });
        Assert.Equal(HttpStatusCode.NotFound, payDeleted.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await shopper.DeleteAsync($"api/payment-methods/{paymentMethodId}")).StatusCode);
    }

    [Fact]
    public async Task A_card_belongs_to_the_shopper_who_saved_it()
    {
        var owner = _factory.ClientFor(NewShopper());
        var other = _factory.ClientFor(NewShopper());
        var paymentMethodId = (await (await owner.PostJsonAsync("api/payment-methods", new { card = ApiCalls.Card() })).ReadJsonAsync())["paymentMethodId"]!.GetValue<int>();

        Assert.Empty((await (await other.GetAsync("api/payment-methods")).ReadJsonAsync())["paymentMethods"]!.AsArray());

        var othersOrder = await other.PlaceOrderAsync((1, 1));
        var creates = _factory.PayPal.Calls("POST", "/v2/checkout/orders").Count();
        Assert.Equal(HttpStatusCode.NotFound, (await other.PostJsonAsync($"api/orders/{othersOrder}/pay", new { paymentMethodId })).StatusCode);
        Assert.Equal(creates, _factory.PayPal.Calls("POST", "/v2/checkout/orders").Count());

        Assert.Equal(HttpStatusCode.NotFound, (await other.DeleteAsync($"api/payment-methods/{paymentMethodId}")).StatusCode);
        Assert.Single((await (await owner.GetAsync("api/payment-methods")).ReadJsonAsync())["paymentMethods"]!.AsArray());
    }

    [Fact]
    public async Task Saving_under_the_same_idempotency_key_vaults_once()
    {
        var shopper = _factory.ClientFor(NewShopper());
        var before = _factory.PayPal.Calls("POST", "/v3/vault/payment-tokens").Count();

        var first = await shopper.PostJsonAsync("api/payment-methods", new { card = ApiCalls.Card(), idempotencyKey = "save-1" });
        var second = await shopper.PostJsonAsync("api/payment-methods", new { card = ApiCalls.Card(), idempotencyKey = "save-1" });

        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        Assert.Equal((await first.ReadJsonAsync())["paymentMethodId"]!.GetValue<int>(), (await second.ReadJsonAsync())["paymentMethodId"]!.GetValue<int>());
        Assert.Equal(before + 1, _factory.PayPal.Calls("POST", "/v3/vault/payment-tokens").Count());
    }

    [Fact]
    public async Task Invalid_card_input_is_rejected_before_reaching_PayPal()
    {
        var shopper = _factory.ClientFor(NewShopper());
        var before = _factory.PayPal.Requests.Count;

        var badNumber = await shopper.PostJsonAsync("api/payment-methods", new { card = ApiCalls.Card("4111111111111112") });
        var expired = await shopper.PostJsonAsync("api/payment-methods", new { card = new { number = ApiCalls.TestVisa, expiry = "2020-01" } });
        var orderId = await shopper.PlaceOrderAsync((1, 1));
        var both = await shopper.PostJsonAsync($"api/orders/{orderId}/pay", new { card = ApiCalls.Card(), paymentMethodId = 1 });

        Assert.Equal(HttpStatusCode.BadRequest, badNumber.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, expired.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, both.StatusCode);
        Assert.DoesNotContain(ApiCalls.TestVisa, (await badNumber.ReadJsonAsync()).ToJsonString());
        Assert.Equal(before, _factory.PayPal.Requests.Count);
    }
}
