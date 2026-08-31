using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PublicApiIntegrationTests.InvoiceEndpoints;

/// <summary>
/// Drives the invoice/order endpoints through the real routing + JWT pipeline (with a fake billing
/// service) to prove auth, operator-role gating, ownership scoping and the response identifiers.
/// </summary>
[TestClass]
public class InvoiceEndpointsTest
{
    private static InvoiceApiFactory _factory = null!;

    [ClassInitialize]
    public static void Init(TestContext _) => _factory = new InvoiceApiFactory();

    [ClassCleanup]
    public static void Cleanup() => _factory.Dispose();

    private static HttpClient Client(string? token)
    {
        var client = _factory.CreateClient();
        if (token is not null)
        {
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        return client;
    }

    private static StringContent Json(string json) => new(json, Encoding.UTF8, "application/json");

    [TestMethod]
    public async Task PlaceOrder_ReturnsCreatedWithOrderId()
    {
        var client = Client(ApiTokenHelper.GetNormalUserToken());

        var response = await client.PostAsync("api/orders", Json("""{"items":[{"catalogItemId":1,"quantity":2}]}"""));

        Assert.AreEqual(HttpStatusCode.Created, response.StatusCode);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.IsTrue(doc.RootElement.TryGetProperty("orderId", out var orderId));
        Assert.IsTrue(orderId.GetInt32() > 0);
    }

    [TestMethod]
    public async Task RaiseInvoice_ReturnsCreatedWithInvoiceId()
    {
        var client = Client(ApiTokenHelper.GetNormalUserToken());

        var response = await client.PostAsync("api/orders/1/invoice", Json("""{"dueDate":"2026-10-01"}"""));

        Assert.AreEqual(HttpStatusCode.Created, response.StatusCode);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.AreEqual("INV-NEW", doc.RootElement.GetProperty("invoiceId").GetString());
    }

    [TestMethod]
    public async Task GetInvoice_ReturnsPaymentLinkAtTopLevel()
    {
        var client = Client(ApiTokenHelper.GetNormalUserToken());

        var response = await client.GetAsync("api/invoices/INV-1");

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.AreEqual("INV-1", doc.RootElement.GetProperty("invoiceId").GetString());
        Assert.AreEqual("https://pay.example/INV-1", doc.RootElement.GetProperty("paymentLink").GetString());
    }

    [TestMethod]
    public async Task MyInvoices_ReturnsCallersInvoicesWithIds()
    {
        var client = Client(ApiTokenHelper.GetNormalUserToken());

        var response = await client.GetAsync("api/my-invoices");

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var first = doc.RootElement.GetProperty("invoices")[0];
        Assert.AreEqual("INV-1", first.GetProperty("invoiceId").GetString());
    }

    [TestMethod]
    public async Task MyInvoices_WithoutToken_IsUnauthorized()
    {
        var client = Client(token: null);

        var response = await client.GetAsync("api/my-invoices");

        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [TestMethod]
    public async Task Issue_AsNormalUser_IsForbidden()
    {
        var client = Client(ApiTokenHelper.GetNormalUserToken());

        var response = await client.PostAsync("api/invoices/INV-1/issue", content: null);

        Assert.AreEqual(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [TestMethod]
    public async Task Issue_AsAdmin_Succeeds()
    {
        var client = Client(ApiTokenHelper.GetAdminUserToken());

        var response = await client.PostAsync("api/invoices/INV-7/issue", content: null);

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        CollectionAssert.Contains(_factory.Fake.Issued, "INV-7");
    }

    [TestMethod]
    public async Task Withdraw_AsNormalUser_IsForbidden()
    {
        var client = Client(ApiTokenHelper.GetNormalUserToken());

        var response = await client.PostAsync("api/invoices/INV-1/withdraw", content: null);

        Assert.AreEqual(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [TestMethod]
    public async Task Reconciliation_AsNormalUser_IsForbidden()
    {
        var client = Client(ApiTokenHelper.GetNormalUserToken());

        var response = await client.GetAsync("api/invoices/reconciliation?from=2026-08-01T00:00:00Z&to=2026-09-01T00:00:00Z");

        Assert.AreEqual(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [TestMethod]
    public async Task Reconciliation_AsAdmin_ReturnsReportDistinguishingProviderOnly()
    {
        var client = Client(ApiTokenHelper.GetAdminUserToken());

        var response = await client.GetAsync("api/invoices/reconciliation?from=2026-08-01T00:00:00Z&to=2026-09-01T00:00:00Z");

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = doc.RootElement;
        Assert.AreEqual(1, root.GetProperty("providerOnlyCount").GetInt32());

        var entries = root.GetProperty("entries");
        var sawProviderOnlyNotEShop = false;
        foreach (var e in entries.EnumerateArray())
        {
            if (e.GetProperty("presence").GetString() == "ProviderOnly")
            {
                Assert.IsFalse(e.GetProperty("isEShopInvoice").GetBoolean());
                sawProviderOnlyNotEShop = true;
            }
        }

        Assert.IsTrue(sawProviderOnlyNotEShop);
    }
}
