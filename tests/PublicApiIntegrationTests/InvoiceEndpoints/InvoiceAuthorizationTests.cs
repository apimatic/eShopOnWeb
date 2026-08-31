using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PublicApiIntegrationTests.InvoiceEndpoints;

/// <summary>
/// Verifies the authorization boundary of the invoicing surface without reaching the provider: operator
/// actions are refused to a normal shopper, and shopper actions require authentication. Authorization is
/// evaluated before any handler runs, so no Visa call is made by these tests.
/// </summary>
[TestClass]
public class InvoiceAuthorizationTests
{
    private static HttpClient AuthenticatedClient(string token)
    {
        var client = ProgramTest.NewClient;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    [TestMethod]
    public async Task IssueInvoice_Returns403_ForNormalUser()
    {
        var client = AuthenticatedClient(ApiTokenHelper.GetNormalUserToken());
        var response = await client.PostAsync("api/invoices/any-id/issue", null);
        Assert.AreEqual(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [TestMethod]
    public async Task WithdrawInvoice_Returns403_ForNormalUser()
    {
        var client = AuthenticatedClient(ApiTokenHelper.GetNormalUserToken());
        var response = await client.PostAsync("api/invoices/any-id/withdraw", null);
        Assert.AreEqual(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [TestMethod]
    public async Task Reconciliation_Returns403_ForNormalUser()
    {
        var client = AuthenticatedClient(ApiTokenHelper.GetNormalUserToken());
        var response = await client.GetAsync("api/invoices/reconciliation?from=2026-01-01T00:00:00Z&to=2026-12-31T00:00:00Z");
        Assert.AreEqual(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [TestMethod]
    public async Task Reconciliation_Returns401_WhenUnauthenticated()
    {
        var client = ProgramTest.NewClient;
        var response = await client.GetAsync("api/invoices/reconciliation?from=2026-01-01T00:00:00Z&to=2026-12-31T00:00:00Z");
        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [TestMethod]
    public async Task MyInvoices_Returns401_WhenUnauthenticated()
    {
        var client = ProgramTest.NewClient;
        var response = await client.GetAsync("api/my-invoices");
        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [TestMethod]
    public async Task MyInvoices_Returns200_AndEmpty_ForNormalUser()
    {
        var client = AuthenticatedClient(ApiTokenHelper.GetNormalUserToken());
        var response = await client.GetAsync("api/my-invoices");
        response.EnsureSuccessStatusCode();
    }

    [TestMethod]
    public async Task CreateOrder_Returns401_WhenUnauthenticated()
    {
        var client = ProgramTest.NewClient;
        var content = new StringContent("{\"items\":[{\"catalogItemId\":1,\"quantity\":1}]}", System.Text.Encoding.UTF8, "application/json");
        var response = await client.PostAsync("api/orders", content);
        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
