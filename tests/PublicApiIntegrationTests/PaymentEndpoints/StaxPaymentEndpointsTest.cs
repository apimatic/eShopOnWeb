using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PublicApiIntegrationTests.PaymentEndpoints;

/// <summary>These tests talk to the real Stax sandbox (Stax:ApiKey from user-secrets or STAX_API_KEY).</summary>
[TestClass]
public class StaxPaymentEndpointsTest
{
    private static HttpClient ClientFor(string token)
    {
        var client = ProgramTest.NewClient;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private static string NewShopperEmail() => $"shopper-{Guid.NewGuid():N}@example.com";

    private static async Task<JsonNode> Json(HttpResponseMessage r) =>
        JsonNode.Parse(await r.Content.ReadAsStringAsync())!;

    private static async Task<string> SaveCard(HttpClient shopper)
    {
        var r = await shopper.PostAsJsonAsync("api/payment-methods", new
        {
            type = "card",
            personName = "Test Shopper",
            cardNumber = "4111111111111111",
            cardCvv = "123",
            cardExp = "1230",
            addressZip = "32801",
        });
        Assert.AreEqual(HttpStatusCode.Created, r.StatusCode, await r.Content.ReadAsStringAsync());
        return (await Json(r))["paymentMethodId"]!.GetValue<string>();
    }

    private static async Task<int> PlaceOrder(HttpClient shopper)
    {
        var r = await shopper.PostAsJsonAsync("api/orders", new { items = new[] { new { catalogItemId = 1, quantity = 2 } } });
        Assert.AreEqual(HttpStatusCode.Created, r.StatusCode, await r.Content.ReadAsStringAsync());
        return (await Json(r))["orderId"]!.GetValue<int>();
    }

    [TestMethod]
    public async Task PaymentEndpointsRequireAToken()
    {
        var client = ProgramTest.NewClient;
        Assert.AreEqual(HttpStatusCode.Unauthorized, (await client.GetAsync("api/payment-methods")).StatusCode);
        Assert.AreEqual(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("api/orders", new { })).StatusCode);
    }

    [TestMethod]
    public async Task StaffEndpointsRejectShoppers()
    {
        var shopper = ClientFor(ApiTokenHelper.GetShopperToken(NewShopperEmail()));
        Assert.AreEqual(HttpStatusCode.Forbidden, (await shopper.PostAsync("api/admin/orders/1/refunds", null)).StatusCode);
        Assert.AreEqual(HttpStatusCode.Forbidden, (await shopper.GetAsync("api/admin/orders/1")).StatusCode);
        Assert.AreEqual(HttpStatusCode.Forbidden, (await shopper.GetAsync("api/admin/shoppers/a%40b.com/payment-methods")).StatusCode);
    }

    [TestMethod]
    public async Task ShopperSavesListsAndRemovesAPaymentMethod_AndStaffSeeTheRemovedOne()
    {
        var email = NewShopperEmail();
        var shopper = ClientFor(ApiTokenHelper.GetShopperToken(email));
        var id = await SaveCard(shopper);

        var list = await Json(await shopper.GetAsync("api/payment-methods"));
        Assert.AreEqual(1, list.AsArray().Count);
        Assert.AreEqual("1111", list[0]!["last4"]!.GetValue<string>());

        // another shopper can neither see nor remove it
        var other = ClientFor(ApiTokenHelper.GetShopperToken(NewShopperEmail()));
        Assert.AreEqual(HttpStatusCode.NotFound, (await other.DeleteAsync($"api/payment-methods/{id}")).StatusCode);

        Assert.AreEqual(HttpStatusCode.OK, (await shopper.DeleteAsync($"api/payment-methods/{id}")).StatusCode);
        Assert.AreEqual(0, (await Json(await shopper.GetAsync("api/payment-methods"))).AsArray().Count);

        var admin = ClientFor(ApiTokenHelper.GetAdminUserToken());
        var support = await Json(await admin.GetAsync($"api/admin/shoppers/{Uri.EscapeDataString(email)}/payment-methods"));
        Assert.AreEqual(1, support.AsArray().Count);
        Assert.IsNotNull(support[0]!["removedAt"]);
    }

    [TestMethod]
    public async Task CheckoutPayAndRefundACardOrder()
    {
        var shopper = ClientFor(ApiTokenHelper.GetShopperToken(NewShopperEmail()));
        var pm = await SaveCard(shopper);
        var orderId = await PlaceOrder(shopper);

        var quote = await Json(await shopper.PostAsJsonAsync($"api/orders/{orderId}/checkout", new { paymentMethodId = pm }));
        var fee = quote["fee"]!.GetValue<decimal>();
        var total = quote["total"]!.GetValue<decimal>();
        Assert.AreEqual(quote["subtotal"]!.GetValue<decimal>() + fee, total);

        var pay = await shopper.PostAsJsonAsync($"api/orders/{orderId}/pay", new { paymentMethodId = pm });
        Assert.AreEqual(HttpStatusCode.OK, pay.StatusCode, await pay.Content.ReadAsStringAsync());
        var order = await Json(await shopper.GetAsync($"api/orders/{orderId}"));
        Assert.IsTrue(order["paid"]!.GetValue<bool>());
        Assert.AreEqual(total, order["total"]!.GetValue<decimal>());

        // paying twice is refused
        Assert.AreEqual(HttpStatusCode.Conflict,
            (await shopper.PostAsJsonAsync($"api/orders/{orderId}/pay", new { paymentMethodId = pm })).StatusCode);

        // other shoppers cannot see the order
        var other = ClientFor(ApiTokenHelper.GetShopperToken(NewShopperEmail()));
        Assert.AreEqual(HttpStatusCode.NotFound, (await other.GetAsync($"api/orders/{orderId}")).StatusCode);

        var admin = ClientFor(ApiTokenHelper.GetAdminUserToken());
        var refund = await Json(await admin.PostAsync($"api/admin/orders/{orderId}/refunds", null));
        Assert.AreEqual("refunded", refund["refundStatus"]!.GetValue<string>());
        var staff = await Json(await admin.GetAsync($"api/admin/orders/{orderId}"));
        Assert.AreEqual("refunded", staff["refundStatus"]!.GetValue<string>());
    }

    [TestMethod]
    public async Task ClosingAnAccountBlocksTheShopperAndErasesTheirData()
    {
        var email = NewShopperEmail();
        var shopper = ClientFor(ApiTokenHelper.GetShopperToken(email));
        await SaveCard(shopper);

        var close = await shopper.PostAsync("api/account/close", null);
        Assert.AreEqual(HttpStatusCode.OK, close.StatusCode, await close.Content.ReadAsStringAsync());
        Assert.AreEqual(HttpStatusCode.Forbidden, (await shopper.GetAsync("api/payment-methods")).StatusCode);

        var admin = ClientFor(ApiTokenHelper.GetAdminUserToken());
        Assert.AreEqual(HttpStatusCode.NotFound,
            (await admin.GetAsync($"api/admin/shoppers/{Uri.EscapeDataString(email)}/payment-methods")).StatusCode);
    }

    [TestMethod]
    public async Task OrdersRejectUnknownItemsAndEmptyOrders()
    {
        var shopper = ClientFor(ApiTokenHelper.GetShopperToken(NewShopperEmail()));
        var unknown = await shopper.PostAsJsonAsync("api/orders", new { items = new[] { new { catalogItemId = 99999, quantity = 1 } } });
        Assert.AreEqual(HttpStatusCode.UnprocessableEntity, unknown.StatusCode);
        var empty = await shopper.PostAsJsonAsync("api/orders", new { items = Array.Empty<object>() });
        Assert.AreEqual(HttpStatusCode.BadRequest, empty.StatusCode);
    }

    [TestMethod]
    public async Task InvoiceSchedulesCanBeCreatedPausedAndResumed()
    {
        var admin = ClientFor(ApiTokenHelper.GetAdminUserToken());
        var created = await admin.PostAsJsonAsync("api/admin/invoice-schedules",
            new { email = $"billing-{Guid.NewGuid():N}@example.com", company = "Test Co", amount = 125.5m });
        Assert.AreEqual(HttpStatusCode.Created, created.StatusCode, await created.Content.ReadAsStringAsync());
        var id = (await Json(created))["scheduleId"]!.GetValue<string>();

        var paused = await Json(await admin.PostAsync($"api/admin/invoice-schedules/{id}/pause", null));
        Assert.AreEqual("paused", paused["status"]!.GetValue<string>());
        var resumed = await Json(await admin.PostAsync($"api/admin/invoice-schedules/{id}/resume", null));
        Assert.AreEqual("active", resumed["status"]!.GetValue<string>());

        var past = await admin.PostAsJsonAsync("api/admin/invoice-schedules",
            new { email = "a@example.com", company = "Test Co", amount = 10, startDate = "2020-01-01" });
        Assert.AreEqual(HttpStatusCode.BadRequest, past.StatusCode);
    }

    [TestMethod]
    public async Task SellerOnboardingIsRefusedWithAReasonWhenThereIsNoPlatformKey()
    {
        var admin = ClientFor(ApiTokenHelper.GetAdminUserToken());
        var r = await admin.PostAsJsonAsync("api/admin/sellers",
            new { company = "Acme", contact = new { name = "Jo", email = "jo@example.com" } });
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("STAX_PLATFORM_API_KEY")))
        {
            Assert.AreEqual(HttpStatusCode.UnprocessableEntity, r.StatusCode);
            StringAssert.Contains(await r.Content.ReadAsStringAsync(), "PlatformApiKey");
        }
    }
}
