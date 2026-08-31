using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PublicApiIntegrationTests.InvoiceEndpoints;

/// <summary>
/// Authorization gating for the invoicing endpoints. These paths are rejected by the auth middleware before
/// any handler runs, so no call reaches the provider — they assert only who may call what.
/// </summary>
[TestClass]
public class InvoiceAuthorizationTests
{
    private static HttpClient Client(string? token)
    {
        var client = ProgramTest.NewClient;
        if (token is not null)
        {
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }
        return client;
    }

    private static StringContent EmptyJson() => new("{}", Encoding.UTF8, "application/json");

    [TestMethod]
    public async Task Issue_IsForbiddenForNormalUser()
    {
        var response = await Client(ApiTokenHelper.GetNormalUserToken())
            .PostAsync("api/invoices/1/issue", EmptyJson());
        Assert.AreEqual(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [TestMethod]
    public async Task Issue_IsUnauthorizedForAnonymous()
    {
        var response = await Client(null).PostAsync("api/invoices/1/issue", EmptyJson());
        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [TestMethod]
    public async Task Withdraw_IsForbiddenForNormalUser()
    {
        var response = await Client(ApiTokenHelper.GetNormalUserToken())
            .PostAsync("api/invoices/1/withdraw", EmptyJson());
        Assert.AreEqual(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [TestMethod]
    public async Task Reconciliation_IsForbiddenForNormalUser()
    {
        var response = await Client(ApiTokenHelper.GetNormalUserToken())
            .GetAsync("api/invoices/reconciliation?from=2020-01-01T00:00:00Z&to=2030-01-01T00:00:00Z");
        Assert.AreEqual(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [TestMethod]
    public async Task MyInvoices_IsUnauthorizedForAnonymous()
    {
        var response = await Client(null).GetAsync("api/my-invoices");
        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [TestMethod]
    public async Task MyInvoices_IsEmptyForNewNormalUser()
    {
        // Shopper-scoped and provider-free: it queries only this caller's stored bills.
        var response = await Client(ApiTokenHelper.GetNormalUserToken()).GetAsync("api/my-invoices");
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadAsStringAsync();
        StringAssert.Contains(body, "\"invoices\"");
    }

    [TestMethod]
    public async Task CreateOrder_IsUnauthorizedForAnonymous()
    {
        var response = await Client(null).PostAsync("api/orders", EmptyJson());
        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
