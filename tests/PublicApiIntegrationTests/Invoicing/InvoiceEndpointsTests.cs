using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PublicApiIntegrationTests.Invoicing;

[TestClass]
public class InvoiceEndpointsTests
{
    private static readonly string DueDate = DateTime.UtcNow.AddDays(20).ToString("yyyy-MM-dd");

    // The EF in-memory store is shared across test-host instances, so each test uses fresh shopper
    // identities to stay independent of what other tests have raised.
    private static string UniqueShopper() => $"shopper-{Guid.NewGuid():N}@example.com";

    private static HttpClient Client(InvoicingApiFactory factory, string token)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private static async Task<(HttpStatusCode Status, JsonElement Body)> ReadAsync(HttpResponseMessage response)
    {
        var raw = await response.Content.ReadAsStringAsync();
        var element = string.IsNullOrWhiteSpace(raw)
            ? default
            : JsonDocument.Parse(raw).RootElement.Clone();
        return (response.StatusCode, element);
    }

    private static async Task<string> PlaceOrderAsync(HttpClient client, int catalogItemId = 1, int quantity = 2)
    {
        var response = await client.PostAsJsonAsync("api/orders", new
        {
            items = new[] { new { catalogItemId, quantity } }
        });
        response.EnsureSuccessStatusCode();
        var (_, body) = await ReadAsync(response);
        return body.GetProperty("orderId").GetInt32().ToString();
    }

    private static async Task<(HttpStatusCode, JsonElement)> RaiseInvoiceAsync(HttpClient client, string orderId, string? dueDate = null)
    {
        var response = await client.PostAsJsonAsync($"api/orders/{orderId}/invoice", new { dueDate = dueDate ?? DueDate });
        return await ReadAsync(response);
    }

    [TestMethod]
    public async Task PlaceOrder_ReturnsOrderId()
    {
        using var factory = new InvoicingApiFactory();
        var client = Client(factory, ApiTokenHelper.GetNormalUserToken());

        var response = await client.PostAsJsonAsync("api/orders", new
        {
            items = new[] { new { catalogItemId = 1, quantity = 2 } }
        });

        var (status, body) = await ReadAsync(response);
        Assert.AreEqual(HttpStatusCode.Created, status);
        Assert.IsTrue(body.GetProperty("orderId").GetInt32() > 0);
        Assert.AreEqual(1, body.GetProperty("itemCount").GetInt32());
    }

    [TestMethod]
    public async Task PlaceOrder_WithUnknownCatalogItem_ReturnsBadRequest()
    {
        using var factory = new InvoicingApiFactory();
        var client = Client(factory, ApiTokenHelper.GetNormalUserToken());

        var response = await client.PostAsJsonAsync("api/orders", new
        {
            items = new[] { new { catalogItemId = 999999, quantity = 1 } }
        });

        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [TestMethod]
    public async Task RaiseInvoice_StartsAsDraft_WithNoPaymentLink()
    {
        using var factory = new InvoicingApiFactory();
        var client = Client(factory, ApiTokenHelper.GetNormalUserToken());
        var orderId = await PlaceOrderAsync(client);

        var (status, body) = await RaiseInvoiceAsync(client, orderId);

        Assert.AreEqual(HttpStatusCode.Created, status);
        Assert.AreEqual("Draft", body.GetProperty("state").GetString());
        var invoiceId = body.GetProperty("invoiceId").GetString();
        Assert.IsFalse(string.IsNullOrWhiteSpace(invoiceId));

        var (getStatus, get) = await ReadAsync(await client.GetAsync($"api/invoices/{invoiceId}"));
        Assert.AreEqual(HttpStatusCode.OK, getStatus);
        Assert.AreEqual("DRAFT", get.GetProperty("status").GetString());
        Assert.AreEqual(JsonValueKind.Null, get.GetProperty("paymentLink").ValueKind);
    }

    [TestMethod]
    public async Task Issue_ThenWithdraw_PaymentLinkAppearsThenIsWithheld()
    {
        using var factory = new InvoicingApiFactory();
        var shopper = Client(factory, ApiTokenHelper.GetNormalUserToken());
        var operatorClient = Client(factory, ApiTokenHelper.GetAdminUserToken());

        var orderId = await PlaceOrderAsync(shopper);
        var (_, raised) = await RaiseInvoiceAsync(shopper, orderId);
        var invoiceId = raised.GetProperty("invoiceId").GetString()!;

        // Issue (operator) -> payment link handed out.
        var (issueStatus, issued) = await ReadAsync(await operatorClient.PostAsync($"api/invoices/{invoiceId}/issue", null));
        Assert.AreEqual(HttpStatusCode.OK, issueStatus);
        Assert.AreEqual("Issued", issued.GetProperty("state").GetString());
        Assert.IsFalse(string.IsNullOrWhiteSpace(issued.GetProperty("paymentLink").GetString()));

        // Shopper can read the payment link.
        var (_, afterIssue) = await ReadAsync(await shopper.GetAsync($"api/invoices/{invoiceId}"));
        Assert.IsFalse(string.IsNullOrWhiteSpace(afterIssue.GetProperty("paymentLink").GetString()));

        // Withdraw (operator) -> no longer payable, link withheld.
        var (withdrawStatus, withdrawn) = await ReadAsync(await operatorClient.PostAsync($"api/invoices/{invoiceId}/withdraw", null));
        Assert.AreEqual(HttpStatusCode.OK, withdrawStatus);
        Assert.AreEqual("Withdrawn", withdrawn.GetProperty("state").GetString());
        Assert.IsFalse(withdrawn.GetProperty("payable").GetBoolean());

        var (_, afterWithdraw) = await ReadAsync(await shopper.GetAsync($"api/invoices/{invoiceId}"));
        Assert.AreEqual(JsonValueKind.Null, afterWithdraw.GetProperty("paymentLink").ValueKind);
    }

    [TestMethod]
    public async Task Correct_BeforeIssue_Succeeds_ButNotAfter()
    {
        using var factory = new InvoicingApiFactory();
        var shopper = Client(factory, ApiTokenHelper.GetNormalUserToken());
        var operatorClient = Client(factory, ApiTokenHelper.GetAdminUserToken());

        var orderId = await PlaceOrderAsync(shopper);
        var (_, raised) = await RaiseInvoiceAsync(shopper, orderId);
        var invoiceId = raised.GetProperty("invoiceId").GetString()!;
        var originalAmount = raised.GetProperty("amount").GetDecimal();

        var newDue = DateTime.UtcNow.AddDays(45).ToString("yyyy-MM-dd");
        var patch = new HttpRequestMessage(HttpMethod.Patch, $"api/invoices/{invoiceId}")
        {
            Content = JsonContent.Create(new { dueDate = newDue, customerName = "Ada Lovelace", customerEmail = "ada@example.com" })
        };
        var (patchStatus, patched) = await ReadAsync(await shopper.SendAsync(patch));
        Assert.AreEqual(HttpStatusCode.OK, patchStatus);
        Assert.AreEqual(newDue, patched.GetProperty("dueDate").GetString());
        Assert.AreEqual("Ada Lovelace", patched.GetProperty("customerName").GetString());
        // The amount comes from the order and is not correctable.
        Assert.AreEqual(originalAmount, patched.GetProperty("amount").GetDecimal());

        // After issue, correction is refused.
        await operatorClient.PostAsync($"api/invoices/{invoiceId}/issue", null);
        var patch2 = new HttpRequestMessage(HttpMethod.Patch, $"api/invoices/{invoiceId}")
        {
            Content = JsonContent.Create(new { customerName = "Too Late" })
        };
        var refused = await shopper.SendAsync(patch2);
        Assert.AreEqual(HttpStatusCode.Conflict, refused.StatusCode);
    }

    [TestMethod]
    public async Task Bill_BelongsToOwningShopper_OthersAreRefused()
    {
        using var factory = new InvoicingApiFactory();
        var ada = Client(factory, ApiTokenHelper.GetTokenForUser(UniqueShopper()));
        var grace = Client(factory, ApiTokenHelper.GetTokenForUser(UniqueShopper()));

        var orderId = await PlaceOrderAsync(ada);
        var (_, raised) = await RaiseInvoiceAsync(ada, orderId);
        var invoiceId = raised.GetProperty("invoiceId").GetString()!;

        // Another shopper cannot see it.
        Assert.AreEqual(HttpStatusCode.NotFound, (await grace.GetAsync($"api/invoices/{invoiceId}")).StatusCode);

        // Another shopper cannot correct it.
        var patch = new HttpRequestMessage(HttpMethod.Patch, $"api/invoices/{invoiceId}")
        {
            Content = JsonContent.Create(new { customerName = "Intruder" })
        };
        Assert.AreEqual(HttpStatusCode.NotFound, (await grace.SendAsync(patch)).StatusCode);

        // Another shopper cannot raise a bill against the owner's order.
        var (foreignRaiseStatus, _) = await RaiseInvoiceAsync(grace, orderId);
        Assert.AreEqual(HttpStatusCode.NotFound, foreignRaiseStatus);

        // The owner still can.
        Assert.AreEqual(HttpStatusCode.OK, (await ada.GetAsync($"api/invoices/{invoiceId}")).StatusCode);
    }

    [TestMethod]
    public async Task OperatorActions_AreForbiddenToShoppers()
    {
        using var factory = new InvoicingApiFactory();
        var shopper = Client(factory, ApiTokenHelper.GetNormalUserToken());

        var orderId = await PlaceOrderAsync(shopper);
        var (_, raised) = await RaiseInvoiceAsync(shopper, orderId);
        var invoiceId = raised.GetProperty("invoiceId").GetString()!;

        Assert.AreEqual(HttpStatusCode.Forbidden, (await shopper.PostAsync($"api/invoices/{invoiceId}/issue", null)).StatusCode);
        Assert.AreEqual(HttpStatusCode.Forbidden, (await shopper.PostAsync($"api/invoices/{invoiceId}/withdraw", null)).StatusCode);
        Assert.AreEqual(HttpStatusCode.Forbidden,
            (await shopper.GetAsync("api/invoices/reconciliation?from=2026-01-01T00:00:00Z&to=2026-12-31T00:00:00Z")).StatusCode);
    }

    [TestMethod]
    public async Task MyInvoices_ReturnsOnlyTheCallersBills()
    {
        using var factory = new InvoicingApiFactory();
        var ada = Client(factory, ApiTokenHelper.GetTokenForUser(UniqueShopper()));
        var grace = Client(factory, ApiTokenHelper.GetTokenForUser(UniqueShopper()));

        var adaOrder = await PlaceOrderAsync(ada);
        await RaiseInvoiceAsync(ada, adaOrder);
        var graceOrder = await PlaceOrderAsync(grace);
        await RaiseInvoiceAsync(grace, graceOrder);

        var (_, mine) = await ReadAsync(await ada.GetAsync("api/my-invoices"));
        var invoices = mine.GetProperty("invoices");
        Assert.AreEqual(1, invoices.GetArrayLength());
        Assert.IsFalse(string.IsNullOrWhiteSpace(invoices[0].GetProperty("invoiceId").GetString()));
    }

    [TestMethod]
    public async Task Reconciliation_DistinguishesEShopBillsFromForeignOnes()
    {
        using var factory = new InvoicingApiFactory();
        var shopper = Client(factory, ApiTokenHelper.GetNormalUserToken());
        var operatorClient = Client(factory, ApiTokenHelper.GetAdminUserToken());

        var orderId = await PlaceOrderAsync(shopper);
        var (_, raised) = await RaiseInvoiceAsync(shopper, orderId);
        var invoiceId = raised.GetProperty("invoiceId").GetString()!;

        var (status, report) = await ReadAsync(await operatorClient.GetAsync(
            "api/invoices/reconciliation?from=2026-01-01T00:00:00Z&to=2027-01-01T00:00:00Z"));

        Assert.AreEqual(HttpStatusCode.OK, status);
        Assert.IsTrue(report.GetProperty("matchedCount").GetInt32() >= 1);
        // The two seeded foreign bills must be visible and flagged as not eShop's.
        Assert.IsTrue(report.GetProperty("providerOnlyCount").GetInt32() >= 2);

        var entries = report.GetProperty("entries");
        var sawOurs = false;
        var sawForeign = false;
        foreach (var entry in entries.EnumerateArray())
        {
            var id = entry.GetProperty("invoiceId").GetString();
            if (id == invoiceId)
            {
                sawOurs = true;
                Assert.AreEqual("Both", entry.GetProperty("source").GetString());
                Assert.IsTrue(entry.GetProperty("isEShopInvoice").GetBoolean());
                Assert.IsTrue(entry.GetProperty("orderId").GetInt32() > 0);
            }
            else if (id is "FOREIGN-A" or "FOREIGN-B")
            {
                sawForeign = true;
                Assert.AreEqual("ProviderOnly", entry.GetProperty("source").GetString());
                Assert.IsFalse(entry.GetProperty("isEShopInvoice").GetBoolean());
            }
        }

        Assert.IsTrue(sawOurs, "eShop's own bill should appear in the report.");
        Assert.IsTrue(sawForeign, "Foreign provider bills should appear in the report.");
    }

    [TestMethod]
    public async Task Endpoints_RequireAuthentication()
    {
        using var factory = new InvoicingApiFactory();
        var anonymous = factory.CreateClient();

        Assert.AreEqual(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("api/my-invoices")).StatusCode);
    }
}
