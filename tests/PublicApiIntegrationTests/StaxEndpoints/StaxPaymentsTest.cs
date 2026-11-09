using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Constants;
using Microsoft.IdentityModel.Tokens;

namespace PublicApiIntegrationTests.StaxEndpoints;

/// <summary>
/// These tests talk to the real Stax sandbox (credentials come from the Stax: configuration section, i.e. user-secrets).
/// They are skipped when no API key is configured.
/// </summary>
[TestClass]
public class StaxPaymentsTest
{
    private static string Token(string email, params string[] roles)
    {
        var claims = new System.Collections.Generic.List<Claim> { new(ClaimTypes.Name, email) };
        foreach (var role in roles)
        {
            claims.Add(new Claim(ClaimTypes.Role, role));
        }

        var key = Encoding.ASCII.GetBytes(AuthorizationConstants.JWT_SECRET_KEY);
        var handler = new JwtSecurityTokenHandler();
        var token = handler.CreateToken(new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(claims),
            Expires = DateTime.UtcNow.AddHours(1),
            SigningCredentials = new SigningCredentials(new SymmetricSecurityKey(key), SecurityAlgorithms.HmacSha256Signature)
        });
        return handler.WriteToken(token);
    }

    private static HttpClient Client(string token)
    {
        var client = ProgramTest.NewClient;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private static async Task<JsonElement> Json(HttpResponseMessage response, HttpStatusCode expected)
    {
        var body = await response.Content.ReadAsStringAsync();
        if (response.StatusCode == HttpStatusCode.ServiceUnavailable)
        {
            Assert.Inconclusive("Stax is not configured: " + body);
        }

        Assert.AreEqual(expected, response.StatusCode, body);
        return string.IsNullOrEmpty(body) ? default : JsonDocument.Parse(body).RootElement.Clone();
    }

    private static object Card(string number = "4111111111111111") =>
        new { type = "card", holderName = "Test Shopper", cardNumber = number, expMonth = 12, expYear = DateTime.UtcNow.Year + 4 };

    private static object Bank() =>
        new { type = "bank", holderName = "Test Shopper", accountNumber = "9876543210", routingNumber = "021000021" };

    private static string NewShopper() => $"shopper-{Guid.NewGuid():N}@example.com";

    [TestMethod]
    public async Task PaymentMethodsRequireAToken()
    {
        var response = await ProgramTest.NewClient.GetAsync("api/payment-methods");
        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [TestMethod]
    public async Task ShopperSavesListsAndRemovesPaymentMethods_SupportSeesRemovedOnes()
    {
        var email = NewShopper();
        var shopper = Client(Token(email));
        var admin = Client(Token("admin@microsoft.com", "Administrators"));

        var card = await Json(await shopper.PostAsJsonAsync("api/payment-methods", Card()), HttpStatusCode.Created);
        var bank = await Json(await shopper.PostAsJsonAsync("api/payment-methods", Bank()), HttpStatusCode.Created);
        Assert.AreEqual("1111", card.GetProperty("last4").GetString());
        Assert.AreEqual("bank", bank.GetProperty("type").GetString());

        var list = await Json(await shopper.GetAsync("api/payment-methods"), HttpStatusCode.OK);
        Assert.AreEqual(2, list.GetArrayLength());

        var cardId = card.GetProperty("id").GetString();
        Assert.AreEqual(HttpStatusCode.NoContent, (await shopper.DeleteAsync($"api/payment-methods/{cardId}")).StatusCode);
        Assert.AreEqual(HttpStatusCode.NotFound, (await shopper.DeleteAsync($"api/payment-methods/{cardId}")).StatusCode);
        list = await Json(await shopper.GetAsync("api/payment-methods"), HttpStatusCode.OK);
        Assert.AreEqual(1, list.GetArrayLength());

        Assert.AreEqual(HttpStatusCode.Forbidden, (await shopper.GetAsync($"api/admin/shoppers/{email}/payment-methods")).StatusCode);
        var support = await Json(await admin.GetAsync($"api/admin/shoppers/{email}/payment-methods"), HttpStatusCode.OK);
        Assert.AreEqual(2, support.GetArrayLength());
        var removed = 0;
        foreach (var m in support.EnumerateArray())
        {
            if (m.GetProperty("removedAt").ValueKind != JsonValueKind.Null)
            {
                removed++;
                Assert.AreEqual(cardId, m.GetProperty("id").GetString());
            }
        }

        Assert.AreEqual(1, removed);
        await Json(await shopper.PostAsync("api/account/close", null), HttpStatusCode.OK);
    }

    [TestMethod]
    public async Task ShopperChecksOutPaysAndStaffRefundsACardOrder()
    {
        var email = NewShopper();
        var shopper = Client(Token(email));
        var admin = Client(Token("admin@microsoft.com", "Administrators"));

        var card = await Json(await shopper.PostAsJsonAsync("api/payment-methods", Card()), HttpStatusCode.Created);
        var cardId = card.GetProperty("id").GetString();
        var order = await Json(await shopper.PostAsJsonAsync("api/orders", new { items = new[] { new { catalogItemId = 1, quantity = 2 } } }),
            HttpStatusCode.Created);
        var orderId = order.GetProperty("id").GetInt32();
        Assert.IsFalse(order.GetProperty("paid").GetBoolean());

        var checkout = await Json(await shopper.PostAsJsonAsync($"api/orders/{orderId}/checkout", new { paymentMethodId = cardId }), HttpStatusCode.OK);
        var amount = checkout.GetProperty("amount").GetDecimal();
        Assert.AreEqual(amount + checkout.GetProperty("fee").GetDecimal(), checkout.GetProperty("total").GetDecimal());

        // somebody else cannot see or pay it
        var other = Client(Token(NewShopper()));
        Assert.AreEqual(HttpStatusCode.NotFound, (await other.GetAsync($"api/orders/{orderId}")).StatusCode);

        var paid = await Json(await shopper.PostAsJsonAsync($"api/orders/{orderId}/pay",
            new { paymentMethodId = cardId, expectedTotal = checkout.GetProperty("total").GetDecimal() }), HttpStatusCode.OK);
        Assert.IsTrue(paid.GetProperty("paid").GetBoolean());
        Assert.AreEqual(HttpStatusCode.Conflict, (await shopper.PostAsJsonAsync($"api/orders/{orderId}/pay", new { paymentMethodId = cardId })).StatusCode);

        var status = await Json(await shopper.GetAsync($"api/orders/{orderId}"), HttpStatusCode.OK);
        Assert.IsTrue(status.GetProperty("paid").GetBoolean());

        Assert.AreEqual(HttpStatusCode.Forbidden, (await shopper.PostAsync($"api/admin/orders/{orderId}/refunds", null)).StatusCode);
        var refund = await Json(await admin.PostAsync($"api/admin/orders/{orderId}/refunds", null), HttpStatusCode.OK);
        Assert.AreEqual("refunded", refund.GetProperty("refundStatus").GetString());
        var staff = await Json(await admin.GetAsync($"api/admin/orders/{orderId}"), HttpStatusCode.OK);
        Assert.AreEqual("refunded", staff.GetProperty("refundStatus").GetString());
        Assert.AreEqual(HttpStatusCode.Conflict, (await admin.PostAsync($"api/admin/orders/{orderId}/refunds", null)).StatusCode);

        await Json(await shopper.PostAsync("api/account/close", null), HttpStatusCode.OK);
        Assert.AreEqual(HttpStatusCode.NotFound, (await shopper.GetAsync($"api/orders/{orderId}")).StatusCode);
    }

    [TestMethod]
    public async Task BankPaymentIsPendingUntilTheBankSettlesAndItsRefundWaits()
    {
        var shopper = Client(Token(NewShopper()));
        var admin = Client(Token("admin@microsoft.com", "Administrators"));

        var bank = await Json(await shopper.PostAsJsonAsync("api/payment-methods", Bank()), HttpStatusCode.Created);
        var order = await Json(await shopper.PostAsJsonAsync("api/orders", new { items = new[] { new { catalogItemId = 2, quantity = 1 } } }),
            HttpStatusCode.Created);
        var orderId = order.GetProperty("id").GetInt32();
        var pay = await Json(await shopper.PostAsJsonAsync($"api/orders/{orderId}/pay", new { paymentMethodId = bank.GetProperty("id").GetString() }),
            HttpStatusCode.OK);
        Assert.IsFalse(pay.GetProperty("paid").GetBoolean());
        Assert.AreEqual("pending", pay.GetProperty("paymentStatus").GetString());
        Assert.AreEqual(0m, pay.GetProperty("fee").GetDecimal());

        // the bank does not allow refunding yet: the refund is accepted and sent later, automatically
        var refund = await Json(await admin.PostAsync($"api/admin/orders/{orderId}/refunds", null), HttpStatusCode.Accepted);
        Assert.AreEqual("pending", refund.GetProperty("refundStatus").GetString());

        // a shopper cannot close the account while money is moving
        Assert.AreEqual(HttpStatusCode.Conflict, (await shopper.PostAsync("api/account/close", null)).StatusCode);
    }

    [TestMethod]
    public async Task OrdersRejectUnknownItems()
    {
        var shopper = Client(Token(NewShopper()));
        var response = await shopper.PostAsJsonAsync("api/orders", new { items = new[] { new { catalogItemId = 987654, quantity = 1 } } });
        Assert.AreEqual(HttpStatusCode.NotFound, response.StatusCode);
        response = await shopper.PostAsJsonAsync("api/orders", new { items = Array.Empty<object>() });
        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [TestMethod]
    public async Task InvoiceSchedulesCanBePausedAndResumed()
    {
        var admin = Client(Token("admin@microsoft.com", "Administrators"));
        var created = await Json(await admin.PostAsJsonAsync("api/admin/invoice-schedules",
            new { email = $"ap-{Guid.NewGuid():N}@example.com", company = "Test Co", amount = 125.50m }), HttpStatusCode.Created);
        var id = created.GetProperty("scheduleId").GetString();
        Assert.AreEqual("active", created.GetProperty("status").GetString());

        var paused = await Json(await admin.PostAsync($"api/admin/invoice-schedules/{id}/pause", null), HttpStatusCode.OK);
        Assert.AreEqual("paused", paused.GetProperty("status").GetString());
        Assert.AreEqual(HttpStatusCode.Conflict, (await admin.PostAsync($"api/admin/invoice-schedules/{id}/pause", null)).StatusCode);
        var resumed = await Json(await admin.PostAsync($"api/admin/invoice-schedules/{id}/resume", null), HttpStatusCode.OK);
        Assert.AreEqual("active", resumed.GetProperty("status").GetString());

        var past = await admin.PostAsJsonAsync("api/admin/invoice-schedules",
            new { email = "ap@example.com", company = "Test Co", amount = 10m, firstInvoiceDate = "2020-01-01" });
        Assert.AreEqual(HttpStatusCode.BadRequest, past.StatusCode);
    }

    [TestMethod]
    public async Task SellerOnboardingIsForAdministrators()
    {
        var shopper = Client(Token(NewShopper()));
        var response = await shopper.PostAsJsonAsync("api/admin/sellers", new { company = "Seller", contact = new { email = "s@example.com" } });
        Assert.AreEqual(HttpStatusCode.Forbidden, response.StatusCode);
    }
}
