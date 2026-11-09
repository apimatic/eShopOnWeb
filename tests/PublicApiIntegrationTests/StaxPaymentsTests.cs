using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PublicApiIntegrationTests;

/// <summary>End-to-end tests against the real Stax sandbox (needs Stax:ApiKey in user-secrets or STAX_API_KEY).</summary>
[TestClass]
public class StaxPaymentsTests
{
    private static HttpClient As(string email, bool admin = false)
    {
        var client = ProgramTest.NewClient;
        var token = ApiTokenHelper.CreateToken(email, admin ? new[] { "Administrators" } : Array.Empty<string>());
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private static string NewEmail() => $"it-{Guid.NewGuid():N}@example.com";

    private static async Task<JsonElement> Json(HttpResponseMessage r)
    {
        var text = await r.Content.ReadAsStringAsync();
        return string.IsNullOrEmpty(text) ? default : JsonDocument.Parse(text).RootElement.Clone();
    }

    private static async Task<string> SaveCard(HttpClient c)
    {
        var r = await c.PostAsJsonAsync("api/payment-methods", new
        {
            type = "card", personName = "Test Shopper", cardNumber = "4111111111111111", cardExp = "1230", cardCvv = "123"
        });
        Assert.AreEqual(HttpStatusCode.Created, r.StatusCode, await r.Content.ReadAsStringAsync());
        return (await Json(r)).GetProperty("id").GetString()!;
    }

    private static async Task<string> SaveBank(HttpClient c)
    {
        var r = await c.PostAsJsonAsync("api/payment-methods", new
        {
            type = "bank", personName = "Test Shopper", bankAccount = "9876543210", bankRouting = "021000021",
            bankName = "Test Bank", bankType = "checking", bankHolderType = "personal"
        });
        Assert.AreEqual(HttpStatusCode.Created, r.StatusCode, await r.Content.ReadAsStringAsync());
        return (await Json(r)).GetProperty("id").GetString()!;
    }

    private static async Task<int> NewOrder(HttpClient c)
    {
        var r = await c.PostAsJsonAsync("api/orders", new { items = new[] { new { catalogItemId = 1, quantity = 2 } } });
        Assert.AreEqual(HttpStatusCode.Created, r.StatusCode, await r.Content.ReadAsStringAsync());
        return (await Json(r)).GetProperty("orderId").GetInt32();
    }

    [TestMethod]
    public async Task PaymentMethods_SaveListRemove_AndSupportSeesRemoved()
    {
        var email = NewEmail();
        var shopper = As(email);
        var cardId = await SaveCard(shopper);
        var bankId = await SaveBank(shopper);

        var list = await Json(await shopper.GetAsync("api/payment-methods"));
        Assert.AreEqual(2, list.GetArrayLength());

        Assert.AreEqual(HttpStatusCode.NoContent, (await shopper.DeleteAsync($"api/payment-methods/{bankId}")).StatusCode);
        Assert.AreEqual(HttpStatusCode.NotFound, (await shopper.DeleteAsync($"api/payment-methods/{bankId}")).StatusCode);
        Assert.AreEqual(1, (await Json(await shopper.GetAsync("api/payment-methods"))).GetArrayLength());

        // someone else cannot remove it
        Assert.AreEqual(HttpStatusCode.NotFound, (await As(NewEmail()).DeleteAsync($"api/payment-methods/{cardId}")).StatusCode);

        // only administrators see the support view
        Assert.AreEqual(HttpStatusCode.Forbidden, (await shopper.GetAsync($"api/admin/shoppers/{email}/payment-methods")).StatusCode);
        var admin = await Json(await As("admin@microsoft.com", true).GetAsync($"api/admin/shoppers/{email}/payment-methods"));
        Assert.AreEqual(2, admin.GetArrayLength());
        foreach (var pm in admin.EnumerateArray())
        {
            var removed = pm.GetProperty("removedAt");
            if (pm.GetProperty("id").GetString() == bankId) Assert.AreNotEqual(JsonValueKind.Null, removed.ValueKind);
            else Assert.AreEqual(JsonValueKind.Null, removed.ValueKind);
        }
    }

    [TestMethod]
    public async Task Card_CheckoutPayAndRefund()
    {
        var shopper = As(NewEmail());
        var cardId = await SaveCard(shopper);
        var orderId = await NewOrder(shopper);

        var quote = await Json(await shopper.PostAsJsonAsync($"api/orders/{orderId}/checkout", new { paymentMethodId = cardId }));
        Assert.IsTrue(quote.GetProperty("total").GetDecimal() >= quote.GetProperty("subtotal").GetDecimal());
        Assert.AreEqual(quote.GetProperty("subtotal").GetDecimal() + quote.GetProperty("fee").GetDecimal(), quote.GetProperty("total").GetDecimal());

        var pay = await shopper.PostAsJsonAsync($"api/orders/{orderId}/pay", new { paymentMethodId = cardId });
        Assert.AreEqual(HttpStatusCode.OK, pay.StatusCode, await pay.Content.ReadAsStringAsync());
        Assert.IsTrue((await Json(pay)).GetProperty("paid").GetBoolean());
        Assert.AreEqual(HttpStatusCode.Conflict, (await shopper.PostAsJsonAsync($"api/orders/{orderId}/pay", new { paymentMethodId = cardId })).StatusCode);

        // other shoppers cannot see it
        Assert.AreEqual(HttpStatusCode.NotFound, (await As(NewEmail()).GetAsync($"api/orders/{orderId}")).StatusCode);

        var admin = As("admin@microsoft.com", true);
        var refund = await admin.PostAsync($"api/admin/orders/{orderId}/refunds", null);
        Assert.AreEqual(HttpStatusCode.OK, refund.StatusCode, await refund.Content.ReadAsStringAsync());
        Assert.AreEqual("refunded", (await Json(refund)).GetProperty("refundStatus").GetString());
        Assert.AreEqual("refunded", (await Json(await admin.GetAsync($"api/admin/orders/{orderId}"))).GetProperty("refundStatus").GetString());
        Assert.AreEqual(HttpStatusCode.Conflict, (await admin.PostAsync($"api/admin/orders/{orderId}/refunds", null)).StatusCode);
    }

    [TestMethod]
    public async Task Bank_PayStaysUnpaidUntilSettled_RefundIsPending()
    {
        var shopper = As(NewEmail());
        var bankId = await SaveBank(shopper);
        var orderId = await NewOrder(shopper);

        var quote = await Json(await shopper.PostAsJsonAsync($"api/orders/{orderId}/checkout", new { paymentMethodId = bankId }));
        Assert.AreEqual(0m, quote.GetProperty("fee").GetDecimal());

        var pay = await shopper.PostAsJsonAsync($"api/orders/{orderId}/pay", new { paymentMethodId = bankId });
        Assert.AreEqual(HttpStatusCode.OK, pay.StatusCode, await pay.Content.ReadAsStringAsync());
        Assert.IsFalse((await Json(pay)).GetProperty("paid").GetBoolean());
        Assert.IsFalse((await Json(await shopper.GetAsync($"api/orders/{orderId}"))).GetProperty("paid").GetBoolean());

        var refund = await Json(await As("admin@microsoft.com", true).PostAsync($"api/admin/orders/{orderId}/refunds", null));
        Assert.AreEqual("pending", refund.GetProperty("refundStatus").GetString());
    }

    [TestMethod]
    public async Task Validation_ReturnsClientErrors()
    {
        var shopper = As(NewEmail());
        Assert.AreEqual(HttpStatusCode.BadRequest, (await shopper.PostAsJsonAsync("api/payment-methods", new { type = "crypto" })).StatusCode);
        Assert.AreEqual(HttpStatusCode.BadRequest, (await shopper.PostAsJsonAsync("api/orders", new { items = Array.Empty<object>() })).StatusCode);
        Assert.AreEqual(HttpStatusCode.NotFound, (await shopper.PostAsJsonAsync("api/orders", new { items = new[] { new { catalogItemId = 999999, quantity = 1 } } })).StatusCode);
        Assert.AreEqual(HttpStatusCode.NotFound, (await shopper.PostAsJsonAsync("api/orders/999999/pay", new { paymentMethodId = "x" })).StatusCode);
        Assert.AreEqual(HttpStatusCode.Unauthorized, (await ProgramTest.NewClient.GetAsync("api/payment-methods")).StatusCode);
        var admin = As("admin@microsoft.com", true);
        Assert.AreEqual(HttpStatusCode.NotFound, (await admin.PostAsync("api/admin/invoice-schedules/does-not-exist/pause", null)).StatusCode);
    }

    [TestMethod]
    public async Task InvoiceSchedule_CreatePauseResume()
    {
        var admin = As("admin@microsoft.com", true);
        var create = await admin.PostAsJsonAsync("api/admin/invoice-schedules", new { customerEmail = NewEmail(), company = "Acme Ltd", amount = 125.50m });
        Assert.AreEqual(HttpStatusCode.Created, create.StatusCode, await create.Content.ReadAsStringAsync());
        var body = await Json(create);
        var id = body.GetProperty("scheduleId").GetString();
        Assert.IsTrue(body.GetProperty("active").GetBoolean());
        Assert.AreEqual(DateTime.UtcNow.Date, body.GetProperty("nextInvoiceAt").GetDateTimeOffset().UtcDateTime.Date);

        var pause = await admin.PostAsync($"api/admin/invoice-schedules/{id}/pause", null);
        Assert.AreEqual(HttpStatusCode.OK, pause.StatusCode, await pause.Content.ReadAsStringAsync());
        Assert.IsFalse((await Json(pause)).GetProperty("active").GetBoolean());

        var resume = await admin.PostAsync($"api/admin/invoice-schedules/{id}/resume", null);
        Assert.AreEqual(HttpStatusCode.OK, resume.StatusCode, await resume.Content.ReadAsStringAsync());
        Assert.IsTrue((await Json(resume)).GetProperty("active").GetBoolean());

        var later = await admin.PostAsJsonAsync("api/admin/invoice-schedules",
            new { customerEmail = NewEmail(), company = "Acme Ltd", amount = 10m, startDate = DateTime.UtcNow.Date.AddDays(10).ToString("yyyy-MM-dd") });
        Assert.AreEqual(HttpStatusCode.Created, later.StatusCode, await later.Content.ReadAsStringAsync());
        Assert.AreEqual(DateTime.UtcNow.Date.AddDays(10), (await Json(later)).GetProperty("nextInvoiceAt").GetDateTimeOffset().UtcDateTime.Date);
    }

    [TestMethod]
    public async Task Seller_RequiresAdministratorAndPlatformKey()
    {
        Assert.AreEqual(HttpStatusCode.Forbidden, (await As(NewEmail()).PostAsJsonAsync("api/admin/sellers", new { company = "X" })).StatusCode);
        var r = await As("admin@microsoft.com", true).PostAsJsonAsync("api/admin/sellers",
            new { company = "Seller Co", contact = new { name = "Sam Seller", email = "sam@example.com" } });
        // No platform key in the sandbox setup: a clear 4xx instead of a fabricated answer.
        if (!(r.StatusCode == HttpStatusCode.Created))
        {
            Assert.IsTrue((int)r.StatusCode is >= 400 and < 500, r.StatusCode + await r.Content.ReadAsStringAsync());
            StringAssert.Contains(await r.Content.ReadAsStringAsync(), "Stax");
        }
    }

    [TestMethod]
    public async Task CloseAccount_ErasesStaxAndShopData()
    {
        var email = NewEmail();
        var shopper = As(email);
        await SaveCard(shopper);
        var orderId = await NewOrder(shopper);

        Assert.AreEqual(HttpStatusCode.NoContent, (await shopper.PostAsync("api/account/close", null)).StatusCode);
        Assert.AreEqual(0, (await Json(await shopper.GetAsync("api/payment-methods"))).GetArrayLength());
        Assert.AreEqual(HttpStatusCode.NotFound, (await shopper.GetAsync($"api/orders/{orderId}")).StatusCode);
        var admin = await Json(await As("admin@microsoft.com", true).GetAsync($"api/admin/shoppers/{email}/payment-methods"));
        Assert.AreEqual(0, admin.GetArrayLength());
    }
}
