#nullable enable

using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using Microsoft.eShopWeb.Infrastructure.Billing.Maxio;
using Microsoft.Extensions.Options;
using Xunit;

namespace Microsoft.eShopWeb.IntegrationTests.Billing;

public class MaxioClientTests
{
    [Fact]
    public async Task ListsActiveProductsFromConfiguredFamilyHandle()
    {
        var handler = new RecordingHandler(_ => JsonResponse("""
            [
              { "product": { "id": 2, "name": "Archived", "handle": "old", "description": "", "price_in_cents": 1, "interval": 1, "interval_unit": "month", "archived_at": "2025-01-01T00:00:00Z", "product_family": { "handle": "family" } } },
              { "product": { "id": 1, "name": "Pro", "handle": "pro", "description": "Pro plan", "price_in_cents": 29900, "interval": 1, "interval_unit": "month", "archived_at": null, "product_family": { "handle": "family" } } }
            ]
            """));
        var client = CreateClient(handler);

        var plans = await client.ListPlansAsync();

        var plan = Assert.Single(plans);
        Assert.Equal("pro", plan.Handle);
        Assert.Equal(29900, plan.PriceInCents);
        Assert.Contains("product_families/handle%3Afamily/products.json", handler.LastRequestUri!.AbsoluteUri);
        Assert.Equal("Basic", handler.LastAuthorizationScheme);
        Assert.Equal("api-key:x", handler.LastDecodedCredentials);
    }

    [Fact]
    public async Task CreatesRemittanceSubscriptionUsingStableHandlesAndReferences()
    {
        var handler = new RecordingHandler(request => JsonResponse("""
            { "subscription": {
                "id": 42,
                "state": "active",
                "product_price_in_cents": 29900,
                "current_period_ends_at": "2026-09-21T00:00:00Z",
                "next_assessment_at": "2026-09-21T00:00:00Z",
                "currency": "USD",
                "reference": "subscription-ref",
                "customer": { "id": 7, "reference": "customer-ref" },
                "product": { "id": 1, "name": "Pro", "handle": "pro", "price_in_cents": 29900, "interval": 1, "interval_unit": "month", "product_family": { "handle": "family" } }
            } }
            """));
        var client = CreateClient(handler);

        var subscription = await client.CreateSubscriptionAsync("customer-ref", "pro", "subscription-ref");

        Assert.Equal(42, subscription.Details.Id);
        Assert.Equal("active", subscription.Details.State);
        Assert.Equal("USD", subscription.Details.Currency);
        using var body = JsonDocument.Parse(handler.LastBody!);
        var request = body.RootElement.GetProperty("subscription");
        Assert.Equal("pro", request.GetProperty("product_handle").GetString());
        Assert.Equal("customer-ref", request.GetProperty("customer_reference").GetString());
        Assert.Equal("subscription-ref", request.GetProperty("reference").GetString());
        Assert.Equal("remittance", request.GetProperty("payment_collection_method").GetString());
    }

    private static MaxioClient CreateClient(HttpMessageHandler handler)
    {
        var options = Options.Create(new MaxioOptions
        {
            ApiKey = "api-key",
            ProductFamilyHandle = "family",
            BaseUrl = "https://maxio.test/root"
        });
        return new MaxioClient(new HttpClient(handler), options);
    }

    private static HttpResponseMessage JsonResponse(string json)
    {
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
    }

    private sealed class RecordingHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responseFactory;

        public RecordingHandler(Func<HttpRequestMessage, HttpResponseMessage> responseFactory)
        {
            _responseFactory = responseFactory;
        }

        public Uri? LastRequestUri { get; private set; }
        public string? LastAuthorizationScheme { get; private set; }
        public string? LastDecodedCredentials { get; private set; }
        public string? LastBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequestUri = request.RequestUri;
            LastAuthorizationScheme = request.Headers.Authorization?.Scheme;
            LastDecodedCredentials = Encoding.ASCII.GetString(
                Convert.FromBase64String(request.Headers.Authorization?.Parameter ?? string.Empty));
            LastBody = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            return _responseFactory(request);
        }
    }
}
