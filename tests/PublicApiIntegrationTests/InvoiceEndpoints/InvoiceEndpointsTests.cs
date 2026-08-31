using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PublicApiIntegrationTests.InvoiceEndpoints;

[TestClass]
public class InvoiceEndpointsTests
{
    private static InvoiceApiFactory _factory = null!;

    [ClassInitialize]
    public static void ClassInitialize(TestContext _) => _factory = new InvoiceApiFactory();

    [ClassCleanup]
    public static void ClassCleanup() => _factory.Dispose();

    private static HttpClient ClientFor(string token)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private static async Task<int> PlaceOrderAsync(HttpClient client, int catalogItemId = 1, int quantity = 2)
    {
        var response = await client.PostAsJsonAsync("api/orders", new
        {
            items = new[] { new { catalogItemId, quantity } }
        });
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        return body.GetProperty("orderId").GetInt32();
    }

    private static async Task<string> RaiseInvoiceAsync(HttpClient client, int orderId, string dueDate = "2026-10-15")
    {
        var response = await client.PostAsJsonAsync($"api/orders/{orderId}/invoice", new { dueDate });
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        return body.GetProperty("invoiceId").GetString()!;
    }

    private static string UniqueShopper() => $"shopper-{Guid.NewGuid():N}@test.local";

    [TestMethod]
    public async Task FullLifecycle_Raise_Correct_Issue_Pay_Withdraw()
    {
        var shopper = ClientFor(InvoiceApiFactory.ShopperToken(UniqueShopper()));
        var admin = ClientFor(InvoiceApiFactory.AdminToken());

        var orderId = await PlaceOrderAsync(shopper);
        var invoiceId = await RaiseInvoiceAsync(shopper, orderId);

        // Draft: no payment link yet.
        var draft = await (await shopper.GetAsync($"api/invoices/{invoiceId}")).Content.ReadFromJsonAsync<JsonElement>();
        Assert.AreEqual("Draft", draft.GetProperty("state").GetString());
        Assert.AreEqual(JsonValueKind.Null, draft.GetProperty("paymentLink").ValueKind);

        // Correct while draft.
        var patch = await shopper.PatchAsJsonAsync($"api/invoices/{invoiceId}", new { dueDate = "2026-11-01" });
        Assert.AreEqual(HttpStatusCode.OK, patch.StatusCode);

        // Issue (operator) -> payment link available.
        var issue = await admin.PostAsync($"api/invoices/{invoiceId}/issue", null);
        Assert.AreEqual(HttpStatusCode.OK, issue.StatusCode);
        var issued = await issue.Content.ReadFromJsonAsync<JsonElement>();
        Assert.AreEqual("Issued", issued.GetProperty("state").GetString());
        Assert.IsFalse(string.IsNullOrEmpty(issued.GetProperty("paymentLink").GetString()));

        // Read back the payment link as a top-level field.
        var afterIssue = await (await shopper.GetAsync($"api/invoices/{invoiceId}")).Content.ReadFromJsonAsync<JsonElement>();
        Assert.AreEqual("Issued", afterIssue.GetProperty("state").GetString());
        Assert.IsFalse(string.IsNullOrEmpty(afterIssue.GetProperty("paymentLink").GetString()));

        // Withdraw (operator) -> no longer payable, link no longer handed out.
        var withdraw = await admin.PostAsync($"api/invoices/{invoiceId}/withdraw", null);
        Assert.AreEqual(HttpStatusCode.OK, withdraw.StatusCode);
        var afterWithdraw = await (await shopper.GetAsync($"api/invoices/{invoiceId}")).Content.ReadFromJsonAsync<JsonElement>();
        Assert.AreEqual("Withdrawn", afterWithdraw.GetProperty("state").GetString());
        Assert.AreEqual(JsonValueKind.Null, afterWithdraw.GetProperty("paymentLink").ValueKind);
    }

    [TestMethod]
    public async Task Issue_And_Withdraw_And_Reconciliation_ForbiddenForNonAdmin()
    {
        var shopper = ClientFor(InvoiceApiFactory.ShopperToken(UniqueShopper()));
        var orderId = await PlaceOrderAsync(shopper);
        var invoiceId = await RaiseInvoiceAsync(shopper, orderId);

        Assert.AreEqual(HttpStatusCode.Forbidden, (await shopper.PostAsync($"api/invoices/{invoiceId}/issue", null)).StatusCode);
        Assert.AreEqual(HttpStatusCode.Forbidden, (await shopper.PostAsync($"api/invoices/{invoiceId}/withdraw", null)).StatusCode);
        Assert.AreEqual(HttpStatusCode.Forbidden,
            (await shopper.GetAsync("api/invoices/reconciliation?from=2026-01-01T00:00:00Z&to=2027-01-01T00:00:00Z")).StatusCode);
    }

    [TestMethod]
    public async Task Patch_AfterIssue_IsRefusedWithConflict()
    {
        var shopper = ClientFor(InvoiceApiFactory.ShopperToken(UniqueShopper()));
        var admin = ClientFor(InvoiceApiFactory.AdminToken());

        var orderId = await PlaceOrderAsync(shopper);
        var invoiceId = await RaiseInvoiceAsync(shopper, orderId);
        (await admin.PostAsync($"api/invoices/{invoiceId}/issue", null)).EnsureSuccessStatusCode();

        var patch = await shopper.PatchAsJsonAsync($"api/invoices/{invoiceId}", new { dueDate = "2026-12-01" });
        Assert.AreEqual(HttpStatusCode.Conflict, patch.StatusCode);
    }

    [TestMethod]
    public async Task Get_AnotherShoppersInvoice_IsNotFound_ButOperatorCanSee()
    {
        var ownerId = UniqueShopper();
        var owner = ClientFor(InvoiceApiFactory.ShopperToken(ownerId));
        var otherShopper = ClientFor(InvoiceApiFactory.ShopperToken(UniqueShopper()));
        var admin = ClientFor(InvoiceApiFactory.AdminToken());

        var orderId = await PlaceOrderAsync(owner);
        var invoiceId = await RaiseInvoiceAsync(owner, orderId);

        Assert.AreEqual(HttpStatusCode.NotFound, (await otherShopper.GetAsync($"api/invoices/{invoiceId}")).StatusCode);
        Assert.AreEqual(HttpStatusCode.OK, (await owner.GetAsync($"api/invoices/{invoiceId}")).StatusCode);
        Assert.AreEqual(HttpStatusCode.OK, (await admin.GetAsync($"api/invoices/{invoiceId}")).StatusCode);
    }

    [TestMethod]
    public async Task Raise_OnAnotherShoppersOrder_IsNotFound()
    {
        var owner = ClientFor(InvoiceApiFactory.ShopperToken(UniqueShopper()));
        var attacker = ClientFor(InvoiceApiFactory.ShopperToken(UniqueShopper()));

        var orderId = await PlaceOrderAsync(owner);

        var response = await attacker.PostAsJsonAsync($"api/orders/{orderId}/invoice", new { dueDate = "2026-10-15" });
        Assert.AreEqual(HttpStatusCode.NotFound, response.StatusCode);
    }

    [TestMethod]
    public async Task MyInvoices_ReturnsOnlyCallersOwnBills()
    {
        var aId = UniqueShopper();
        var a = ClientFor(InvoiceApiFactory.ShopperToken(aId));
        var b = ClientFor(InvoiceApiFactory.ShopperToken(UniqueShopper()));

        var aInvoice = await RaiseInvoiceAsync(a, await PlaceOrderAsync(a));
        var bInvoice = await RaiseInvoiceAsync(b, await PlaceOrderAsync(b));

        var mine = await (await a.GetAsync("api/my-invoices")).Content.ReadFromJsonAsync<JsonElement>();
        var ids = mine.GetProperty("invoices").EnumerateArray().Select(e => e.GetProperty("invoiceId").GetString()).ToList();

        CollectionAssert.Contains(ids, aInvoice);
        CollectionAssert.DoesNotContain(ids, bInvoice);
    }

    [TestMethod]
    public async Task Reconciliation_ByOperator_MatchesOwnBills_AndFlagsProviderOnly()
    {
        var shopper = ClientFor(InvoiceApiFactory.ShopperToken(UniqueShopper()));
        var admin = ClientFor(InvoiceApiFactory.AdminToken());

        var invoiceId = await RaiseInvoiceAsync(shopper, await PlaceOrderAsync(shopper));

        var report = await (await admin.GetAsync("api/invoices/reconciliation?from=2026-01-01T00:00:00Z&to=2027-01-01T00:00:00Z"))
            .Content.ReadFromJsonAsync<JsonElement>();

        var entries = report.GetProperty("entries").EnumerateArray().ToList();

        var ours = entries.Single(e => e.GetProperty("invoiceId").GetString() == invoiceId);
        Assert.AreEqual("Both", ours.GetProperty("source").GetString());
        Assert.IsTrue(ours.GetProperty("knownToEShop").GetBoolean());
        Assert.IsTrue(ours.GetProperty("knownToProvider").GetBoolean());

        var providerOnly = entries.Single(e => e.GetProperty("invoiceId").GetString() == FakeInvoicingService.OtherActivityInvoiceId);
        Assert.AreEqual("ProviderOnly", providerOnly.GetProperty("source").GetString());
        Assert.IsFalse(providerOnly.GetProperty("knownToEShop").GetBoolean());
    }
}
