#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Billing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Microsoft.eShopWeb.IntegrationTests.Billing;

public class MaxioBillingGatewayTests
{
    [Fact]
    public async Task ListsConfiguredFamilyProductsUsingBaseUrlOverride()
    {
        var handler = new StubHandler((_, call) => call switch
        {
            1 => Json(HttpStatusCode.OK,
                """[{"product_family":{"id":123,"handle":"family-handle","name":"Plans"}}]"""),
            2 => Json(HttpStatusCode.OK,
                """[{"product":{"id":456,"handle":"pro-plan","name":"Pro","price_in_cents":29900,"interval":1,"interval_unit":"month"}}]"""),
            _ => throw new InvalidOperationException("Unexpected Maxio request.")
        });
        using var provider = BuildProvider(handler);
        var gateway = provider.GetRequiredService<ISubscriptionBillingGateway>();

        var plans = await gateway.ListPlansAsync(CancellationToken.None);

        var plan = Assert.Single(plans);
        Assert.Equal("pro-plan", plan.Handle);
        Assert.Equal(29900, plan.PriceInCents);
        Assert.All(handler.Requests, request => Assert.Equal("maxio.test", request.Uri.Host));
        Assert.Contains("page=1", handler.Requests[1].Uri.Query);
        Assert.Contains("per_page=100", handler.Requests[1].Uri.Query);
        Assert.All(handler.Requests, request => Assert.Equal("Basic", request.Authorization?.Scheme));
    }

    [Fact]
    public async Task CreatesSubscriptionUsingRemittanceWithoutPaymentProfileOrNumericSelectors()
    {
        var handler = new StubHandler((_, _) => Json(HttpStatusCode.Created,
            """{"subscription":{"id":789,"reference":"subscription-ref","state":"active","product_price_in_cents":29900,"next_assessment_at":"2026-09-25T00:00:00Z","product":{"handle":"pro-plan","name":"Pro","interval":1,"interval_unit":"month"}}}"""));
        using var provider = BuildProvider(handler);
        var gateway = provider.GetRequiredService<ISubscriptionBillingGateway>();

        var subscription = await gateway.CreateSubscriptionAsync(
            "pro-plan",
            "customer-ref",
            "subscription-ref",
            CancellationToken.None);

        Assert.Equal(789, subscription.Id);
        var body = Assert.Single(handler.Requests).Body;
        Assert.Contains("\"product_handle\":\"pro-plan\"", body);
        Assert.Contains("\"customer_reference\":\"customer-ref\"", body);
        Assert.Contains("\"reference\":\"subscription-ref\"", body);
        Assert.Contains("\"payment_collection_method\":\"remittance\"", body);
        Assert.DoesNotContain("product_id", body);
        Assert.DoesNotContain("customer_id", body);
        Assert.DoesNotContain("payment_profile", body);
        Assert.DoesNotContain("credit_card", body);
        Assert.DoesNotContain("bank_account", body);
    }

    [Fact]
    public async Task TransportFailureDoesNotResendSubscriptionPost()
    {
        var handler = new StubHandler((request, _) =>
        {
            if (request.Method == HttpMethod.Post)
            {
                throw new HttpRequestException("simulated connection reset");
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });
        using var provider = BuildProvider(handler);
        var gateway = provider.GetRequiredService<ISubscriptionBillingGateway>();

        var exception = await Assert.ThrowsAsync<BillingProviderException>(() =>
            gateway.CreateSubscriptionAsync(
                "pro-plan",
                "customer-ref",
                "subscription-ref",
                CancellationToken.None));

        Assert.True(exception.OutcomeMayBeUnknown);
        Assert.Single(handler.Requests.Where(request => request.Method == HttpMethod.Post));
    }

    private static ServiceProvider BuildProvider(HttpMessageHandler handler)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Maxio:ApiKey"] = "test-key",
                ["Maxio:Subdomain"] = "ignored-subdomain",
                ["Maxio:ProductFamilyHandle"] = "family-handle",
                ["Maxio:BaseUrl"] = "https://maxio.test"
            })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddMaxioSubscriptionBilling(configuration);
        services.AddHttpClient("MaxioAdvancedBilling")
            .ConfigurePrimaryHttpMessageHandler(() => handler);
        return services.BuildServiceProvider();
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string body) =>
        new(status)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };

    private sealed record CapturedRequest(
        HttpMethod Method,
        Uri Uri,
        AuthenticationHeaderValue? Authorization,
        string Body);

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, int, HttpResponseMessage> _responder;
        private int _calls;

        public StubHandler(Func<HttpRequestMessage, int, HttpResponseMessage> responder)
        {
            _responder = responder;
        }

        public List<CapturedRequest> Requests { get; } = new();

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var body = request.Content is null
                ? string.Empty
                : await request.Content.ReadAsStringAsync(cancellationToken);
            Requests.Add(new CapturedRequest(
                request.Method,
                request.RequestUri!,
                request.Headers.Authorization,
                body));
            return _responder(request, Interlocked.Increment(ref _calls));
        }
    }
}
