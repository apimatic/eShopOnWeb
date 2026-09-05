using System.Net;
using System.Net.Http;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Services.MaxioSubscriptionServiceTests;

/// <summary>
/// A plan with RequireCreditCard:false still charges the full price immediately (no trial) under the
/// default Automatic collection method, which Maxio rejects outright with no payment profile on file
/// (confirmed live against the sandbox: "No payment method was on file for the $299.00 balance"). The
/// site's billing architecture decides which non-card PaymentCollectionMethod is legal. See maxio-plan.md §5.
/// </summary>
public class SubscribeAsyncPaymentCollectionMethod
{
    [Fact]
    public async Task UsesInvoiceCollectionOnLegacyStatementsArchitecture()
    {
        var handler = new MaxioSequencedHandler(
            _ => MaxioTestSupport.Json(HttpStatusCode.OK, """{ "customer": { "id": 555 } }"""),
            _ => MaxioTestSupport.Json(HttpStatusCode.OK, "[]"),
            MaxioTestSupport.ReadSiteResponder(relationshipInvoicingEnabled: false),
            request =>
            {
                var body = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
                Assert.Contains("\"payment_collection_method\":\"invoice\"", body);
                Assert.Contains("\"net_terms\":\"0\"", body);
                return MaxioTestSupport.Json(HttpStatusCode.Created, """
                    { "subscription": { "id": 999, "state": "active", "product": { "handle": "eshop-pro" } } }
                    """);
            });
        var service = MaxioTestSupport.CreateService(handler);

        await service.SubscribeAsync("user-1", "jane.doe@example.com", "eshop-pro");
    }

    [Fact]
    public async Task UsesRemittanceCollectionOnRelationshipInvoicingArchitecture()
    {
        var handler = new MaxioSequencedHandler(
            _ => MaxioTestSupport.Json(HttpStatusCode.OK, """{ "customer": { "id": 555 } }"""),
            _ => MaxioTestSupport.Json(HttpStatusCode.OK, "[]"),
            MaxioTestSupport.ReadSiteResponder(relationshipInvoicingEnabled: true),
            request =>
            {
                var body = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
                Assert.Contains("\"payment_collection_method\":\"remittance\"", body);
                return MaxioTestSupport.Json(HttpStatusCode.Created, """
                    { "subscription": { "id": 999, "state": "active", "product": { "handle": "eshop-pro" } } }
                    """);
            });
        var service = MaxioTestSupport.CreateService(handler);

        await service.SubscribeAsync("user-1", "jane.doe@example.com", "eshop-pro");
    }

    [Fact]
    public async Task OnlyReadsTheSiteOnceAcrossMultipleSubscribeCalls()
    {
        var handler = new MaxioSequencedHandler(
            _ => MaxioTestSupport.Json(HttpStatusCode.OK, """{ "customer": { "id": 555 } }"""),
            _ => MaxioTestSupport.Json(HttpStatusCode.OK, "[]"),
            MaxioTestSupport.ReadSiteResponder(),
            _ => MaxioTestSupport.Json(HttpStatusCode.Created, """
                { "subscription": { "id": 999, "state": "active", "product": { "handle": "eshop-pro" } } }
                """),
            _ => MaxioTestSupport.Json(HttpStatusCode.OK, """{ "customer": { "id": 555 } }"""),
            _ => MaxioTestSupport.Json(HttpStatusCode.OK, "[]"),
            _ => MaxioTestSupport.Json(HttpStatusCode.Created, """
                { "subscription": { "id": 1000, "state": "active", "product": { "handle": "basic-plan" } } }
                """));
        var service = MaxioTestSupport.CreateService(handler);

        await service.SubscribeAsync("user-1", "jane.doe@example.com", "eshop-pro");
        await service.SubscribeAsync("user-1", "jane.doe@example.com", "basic-plan");

        Assert.Equal(7, handler.Requests.Count);
        var siteRequests = handler.Requests.Count(r => r.RequestUri!.AbsolutePath.Contains("site", System.StringComparison.OrdinalIgnoreCase));
        Assert.Equal(1, siteRequests);
    }
}
