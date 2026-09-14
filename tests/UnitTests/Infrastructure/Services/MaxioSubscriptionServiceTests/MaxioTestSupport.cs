using System.Net;
using System.Net.Http;
using System.Text;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.Authentication.Basic;
using Microsoft.eShopWeb.ApplicationCore;
using Microsoft.eShopWeb.Infrastructure.Services;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Services.MaxioSubscriptionServiceTests;

/// <summary>
/// A queue of canned responses played back in call order - the seam recommended for testing an
/// APIMatic .NET SDK client is its HttpClient, not the SDK's internal plumbing.
/// </summary>
internal sealed class MaxioSequencedHandler : HttpMessageHandler
{
    private readonly Queue<Func<HttpRequestMessage, HttpResponseMessage>> _responders;

    public List<HttpRequestMessage> Requests { get; } = new();

    public MaxioSequencedHandler(params Func<HttpRequestMessage, HttpResponseMessage>[] responders)
    {
        _responders = new Queue<Func<HttpRequestMessage, HttpResponseMessage>>(responders);
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        Requests.Add(request);
        if (_responders.Count == 0)
            throw new InvalidOperationException($"No stubbed response left for {request.Method} {request.RequestUri}.");

        return Task.FromResult(_responders.Dequeue()(request));
    }
}

internal static class MaxioTestSupport
{
    public static HttpResponseMessage Json(HttpStatusCode status, string json) => new(status)
    {
        Content = new StringContent(json, Encoding.UTF8, "application/json")
    };

    /// <summary>
    /// A stubbed ReadSite response - SubscribeAsync reads this once (cached) to pick a
    /// PaymentCollectionMethod that doesn't require a payment profile. Legacy Statements Architecture
    /// (relationship_invoicing_enabled: false) is the shape used across these tests.
    /// </summary>
    public static Func<HttpRequestMessage, HttpResponseMessage> ReadSiteResponder(bool relationshipInvoicingEnabled = false) =>
        _ => Json(HttpStatusCode.OK, $$"""{ "site": { "relationship_invoicing_enabled": {{(relationshipInvoicingEnabled ? "true" : "false")}} } }""");

    public static MaxioAdvancedBillingClient CreateClient(MaxioSequencedHandler handler) =>
        new(new HttpClient(handler), new MaxioAdvancedBillingClientOptions
        {
            BasicAuth = new BasicAuthCredentials { Username = "test-api-key", Password = "x" }
        });

    public static MaxioSubscriptionService CreateService(MaxioSequencedHandler handler) =>
        new(CreateClient(handler), Options.Create(new MaxioSettings { ProductFamilyHandle = "eshop-subscribe" }));
}
