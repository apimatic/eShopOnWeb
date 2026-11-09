using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PublicApiIntegrationTests.PaymentEndpoints;

[TestClass]
public class PaymentEndpointsTest
{
    private static HttpClient ClientFor(string token)
    {
        var client = ProgramTest.NewClient;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    [DataTestMethod]
    [DataRow("GET", "api/payment-methods")]
    [DataRow("POST", "api/payment-methods")]
    [DataRow("DELETE", "api/payment-methods/abc")]
    [DataRow("POST", "api/orders")]
    [DataRow("GET", "api/orders/1")]
    [DataRow("POST", "api/orders/1/checkout")]
    [DataRow("POST", "api/orders/1/pay")]
    [DataRow("POST", "api/account/close")]
    public async Task ShopperEndpointsRequireAToken(string method, string url)
    {
        var response = await ProgramTest.NewClient.SendAsync(new HttpRequestMessage(new HttpMethod(method), url));
        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [DataTestMethod]
    [DataRow("GET", "api/admin/shoppers/a@b.com/payment-methods")]
    [DataRow("POST", "api/admin/orders/1/refunds")]
    [DataRow("GET", "api/admin/orders/1")]
    [DataRow("POST", "api/admin/invoice-schedules")]
    [DataRow("POST", "api/admin/invoice-schedules/abc/pause")]
    [DataRow("POST", "api/admin/invoice-schedules/abc/resume")]
    [DataRow("POST", "api/admin/sellers")]
    public async Task StaffEndpointsAreForAdministrators(string method, string url)
    {
        var anonymous = await ProgramTest.NewClient.SendAsync(new HttpRequestMessage(new HttpMethod(method), url));
        Assert.AreEqual(HttpStatusCode.Unauthorized, anonymous.StatusCode);

        var shopper = await ClientFor(ApiTokenHelper.GetNormalUserToken()).SendAsync(new HttpRequestMessage(new HttpMethod(method), url));
        Assert.AreEqual(HttpStatusCode.Forbidden, shopper.StatusCode);
    }

    [TestMethod]
    public async Task OrderNeedsItemsFromTheCatalog()
    {
        var client = ClientFor(ApiTokenHelper.GetUserToken($"orders-{Guid.NewGuid():N}@example.com"));

        var empty = await client.PostAsJsonAsync("api/orders", new { items = Array.Empty<object>() });
        Assert.AreEqual(HttpStatusCode.BadRequest, empty.StatusCode);

        var unknown = await client.PostAsJsonAsync("api/orders", new { items = new[] { new { catalogItemId = 987654, quantity = 1 } } });
        Assert.AreEqual(HttpStatusCode.NotFound, unknown.StatusCode);
    }

    [TestMethod]
    public async Task ShopperSeesOnlyOwnOrders()
    {
        var mine = ClientFor(ApiTokenHelper.GetUserToken($"mine-{Guid.NewGuid():N}@example.com"));
        var created = await mine.PostAsJsonAsync("api/orders", new { items = new[] { new { catalogItemId = 1, quantity = 2 } } });
        Assert.AreEqual(HttpStatusCode.Created, created.StatusCode);
        var order = await created.Content.ReadFromJsonAsync<JsonElement>();
        var id = order.GetProperty("orderId").GetInt32();
        Assert.IsFalse(order.GetProperty("paid").GetBoolean());

        Assert.AreEqual(HttpStatusCode.OK, (await mine.GetAsync($"api/orders/{id}")).StatusCode);

        var other = ClientFor(ApiTokenHelper.GetUserToken($"other-{Guid.NewGuid():N}@example.com"));
        Assert.AreEqual(HttpStatusCode.NotFound, (await other.GetAsync($"api/orders/{id}")).StatusCode);
    }

    [TestMethod]
    public async Task CardCheckoutPayRefundAgainstTheStaxSandbox()
    {
        if (string.IsNullOrWhiteSpace(StaxApiKey()))
            Assert.Inconclusive("Stax:ApiKey is not configured (dotnet user-secrets set \"Stax:ApiKey\" ...).");

        var email = $"sandbox-{Guid.NewGuid():N}@example.com";
        var shopper = ClientFor(ApiTokenHelper.GetUserToken(email));
        var admin = ClientFor(ApiTokenHelper.GetAdminUserToken());

        // save a card and a bank account
        var card = await shopper.PostAsJsonAsync("api/payment-methods", new
        {
            type = "card", personName = "Sandbox Shopper", cardNumber = "4111111111111111", expMonth = 12, expYear = DateTime.UtcNow.Year + 3, cvv = "123", postalCode = "12345"
        });
        Assert.AreEqual(HttpStatusCode.Created, card.StatusCode, await card.Content.ReadAsStringAsync());
        var cardId = (await card.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetString()!;

        var bank = await shopper.PostAsJsonAsync("api/payment-methods", new
        {
            type = "bank", personName = "Sandbox Shopper", accountNumber = "9876543210", routingNumber = "021000021", accountType = "checking", holderType = "personal"
        });
        Assert.AreEqual(HttpStatusCode.Created, bank.StatusCode, await bank.Content.ReadAsStringAsync());
        var bankId = (await bank.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetString()!;

        var list = await shopper.GetFromJsonAsync<JsonElement>("api/payment-methods");
        Assert.AreEqual(2, list.GetArrayLength());

        // order, checkout: the card carries the processing fee, the bank account does not
        var created = await shopper.PostAsJsonAsync("api/orders", new { items = new[] { new { catalogItemId = 1, quantity = 2 } } });
        var order = await created.Content.ReadFromJsonAsync<JsonElement>();
        var orderId = order.GetProperty("orderId").GetInt32();
        var subtotal = order.GetProperty("subtotal").GetDecimal();

        var cardQuote = await (await shopper.PostAsJsonAsync($"api/orders/{orderId}/checkout", new { paymentMethodId = cardId })).Content.ReadFromJsonAsync<JsonElement>();
        Assert.IsTrue(cardQuote.GetProperty("fee").GetDecimal() > 0);
        Assert.AreEqual(subtotal + cardQuote.GetProperty("fee").GetDecimal(), cardQuote.GetProperty("total").GetDecimal());
        var bankQuote = await (await shopper.PostAsJsonAsync($"api/orders/{orderId}/checkout", new { paymentMethodId = bankId })).Content.ReadFromJsonAsync<JsonElement>();
        Assert.AreEqual(0m, bankQuote.GetProperty("fee").GetDecimal());
        Assert.AreEqual(subtotal, bankQuote.GetProperty("total").GetDecimal());

        // pay by card: charged the quoted total, paid straight away, never twice
        var pay = await shopper.PostAsJsonAsync($"api/orders/{orderId}/pay", new { paymentMethodId = cardId });
        Assert.AreEqual(HttpStatusCode.OK, pay.StatusCode, await pay.Content.ReadAsStringAsync());
        var paid = await pay.Content.ReadFromJsonAsync<JsonElement>();
        Assert.AreEqual(cardQuote.GetProperty("total").GetDecimal(), paid.GetProperty("total").GetDecimal());
        Assert.IsTrue(paid.GetProperty("paid").GetBoolean());
        Assert.AreEqual(HttpStatusCode.Conflict, (await shopper.PostAsJsonAsync($"api/orders/{orderId}/pay", new { paymentMethodId = cardId })).StatusCode);
        Assert.IsTrue((await shopper.GetFromJsonAsync<JsonElement>($"api/orders/{orderId}")).GetProperty("paid").GetBoolean());

        // a bank payment is only accepted: it is paid once the bank settles it
        var bankOrder = await (await shopper.PostAsJsonAsync("api/orders", new { items = new[] { new { catalogItemId = 2, quantity = 1 } } })).Content.ReadFromJsonAsync<JsonElement>();
        var bankOrderId = bankOrder.GetProperty("orderId").GetInt32();
        var bankPay = await shopper.PostAsJsonAsync($"api/orders/{bankOrderId}/pay", new { paymentMethodId = bankId });
        Assert.AreEqual(HttpStatusCode.OK, bankPay.StatusCode, await bankPay.Content.ReadAsStringAsync());
        Assert.IsFalse((await shopper.GetFromJsonAsync<JsonElement>($"api/orders/{bankOrderId}")).GetProperty("paid").GetBoolean());

        // refund the card order: the whole amount, fee included
        var refund = await admin.PostAsync($"api/admin/orders/{orderId}/refunds", null);
        Assert.AreEqual(HttpStatusCode.OK, refund.StatusCode, await refund.Content.ReadAsStringAsync());
        Assert.AreEqual("refunded", (await refund.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("refundStatus").GetString());
        Assert.AreEqual("refunded", (await admin.GetFromJsonAsync<JsonElement>($"api/admin/orders/{orderId}")).GetProperty("refundStatus").GetString());
        Assert.AreEqual(HttpStatusCode.Conflict, (await admin.PostAsync($"api/admin/orders/{orderId}/refunds", null)).StatusCode);

        // the bank order's refund has to wait for the bank: it is promised and stays pending
        var bankRefund = await admin.PostAsync($"api/admin/orders/{bankOrderId}/refunds", null);
        Assert.IsTrue(bankRefund.StatusCode is HttpStatusCode.OK or HttpStatusCode.Accepted);
        Assert.IsTrue((await bankRefund.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("refundStatus").GetString() is "pending" or "refunded");

        // remove the card; support still sees it, with when it was removed
        Assert.AreEqual(HttpStatusCode.NoContent, (await shopper.DeleteAsync($"api/payment-methods/{cardId}")).StatusCode);
        Assert.AreEqual(HttpStatusCode.NotFound, (await shopper.DeleteAsync($"api/payment-methods/{cardId}")).StatusCode);
        var support = await admin.GetFromJsonAsync<JsonElement>($"api/admin/shoppers/{email}/payment-methods");
        Assert.AreEqual(2, support.GetArrayLength());
        foreach (var method in support.EnumerateArray())
        {
            var removed = method.GetProperty("removedAt");
            Assert.AreEqual(method.GetProperty("id").GetString() == cardId, removed.ValueKind != JsonValueKind.Null);
        }

        // closing the account erases the shopper
        var close = await shopper.PostAsync("api/account/close", null);
        Assert.AreEqual(HttpStatusCode.OK, close.StatusCode, await close.Content.ReadAsStringAsync());
        var after = await admin.GetFromJsonAsync<JsonElement>($"api/admin/shoppers/{email}/payment-methods");
        Assert.AreEqual(0, after.GetArrayLength());
    }

    [TestMethod]
    public async Task InvoiceSchedulesCanBePausedAndResumed()
    {
        if (string.IsNullOrWhiteSpace(StaxApiKey()))
            Assert.Inconclusive("Stax:ApiKey is not configured.");

        var admin = ClientFor(ApiTokenHelper.GetAdminUserToken());
        var created = await admin.PostAsJsonAsync("api/admin/invoice-schedules", new
        {
            customerEmail = $"billing-{Guid.NewGuid():N}@example.com", company = "Acme Test Co", amount = 125.50m,
            firstInvoiceDate = DateTime.UtcNow.Date.AddDays(10).ToString("yyyy-MM-dd")
        });
        Assert.AreEqual(HttpStatusCode.Created, created.StatusCode, await created.Content.ReadAsStringAsync());
        var id = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("scheduleId").GetString();

        var pause = await admin.PostAsync($"api/admin/invoice-schedules/{id}/pause", null);
        Assert.AreEqual(HttpStatusCode.OK, pause.StatusCode);
        Assert.IsFalse((await pause.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("active").GetBoolean());
        Assert.AreEqual(HttpStatusCode.Conflict, (await admin.PostAsync($"api/admin/invoice-schedules/{id}/pause", null)).StatusCode);

        var resume = await admin.PostAsync($"api/admin/invoice-schedules/{id}/resume", null);
        Assert.AreEqual(HttpStatusCode.OK, resume.StatusCode);
        Assert.IsTrue((await resume.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("active").GetBoolean());

        var past = await admin.PostAsJsonAsync("api/admin/invoice-schedules", new { customerEmail = "a@b.com", company = "X", amount = 1, firstInvoiceDate = "2020-01-01" });
        Assert.AreEqual(HttpStatusCode.BadRequest, past.StatusCode);
    }

    private static string? StaxApiKey()
    {
        // The test host reads the same user-secrets as the API (Development environment).
        var config = new ConfigurationBuilder()
            .AddUserSecrets(typeof(Program).Assembly, optional: true)
            .AddEnvironmentVariables()
            .Build();
        return config["Stax:ApiKey"];
    }
}
