using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.Specification;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.Authentication.Basic;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.PublicApi.Maxio;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.PublicApi.Maxio;

public class MaxioBillingServiceTests
{
    private sealed class SequencedStubHandler : HttpMessageHandler
    {
        private readonly Queue<(HttpStatusCode Status, string Json)> _responses;
        public List<HttpRequestMessage> Requests { get; } = new();

        public SequencedStubHandler(IEnumerable<(HttpStatusCode, string)> responses)
        {
            _responses = new Queue<(HttpStatusCode, string)>(responses);
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Requests.Add(request);
            var (status, json) = _responses.Count > 0 ? _responses.Dequeue() : (HttpStatusCode.OK, "{}");
            return Task.FromResult(new HttpResponseMessage(status)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            });
        }
    }

    private static MaxioBillingService CreateService(
        SequencedStubHandler handler,
        IRepository<MaxioCustomerMapping> customerMappings,
        string productFamilyHandle = "eshop-subscribe")
    {
        var options = new MaxioAdvancedBillingClientOptions
        {
            BasicAuth = new BasicAuthCredentials { Username = "test-key", Password = "x" }
        };
        options.Server.Production.Us.Site = "test-site";
        var client = new MaxioAdvancedBillingClient(new HttpClient(handler), options);

        return new MaxioBillingService(client, customerMappings, Options.Create(new MaxioOptions
        {
            ApiKey = "test-key",
            Subdomain = "test-site",
            ProductFamilyHandle = productFamilyHandle
        }));
    }

    private static IRepository<MaxioCustomerMapping> RepositoryReturning(MaxioCustomerMapping? cached)
    {
        var repository = Substitute.For<IRepository<MaxioCustomerMapping>>();
        repository
            .FirstOrDefaultAsync(Arg.Any<ISpecification<MaxioCustomerMapping>>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(cached));
        repository
            .AddAsync(Arg.Any<MaxioCustomerMapping>(), Arg.Any<CancellationToken>())
            .Returns(callInfo => Task.FromResult(callInfo.Arg<MaxioCustomerMapping>()));
        return repository;
    }

    [Fact]
    public async Task ListPlansAsync_MapsFieldsFromResponse()
    {
        var handler = new SequencedStubHandler(new[]
        {
            (HttpStatusCode.OK, """
            [
              { "product": { "id": 7126957, "name": "Pro Plan", "handle": "eshop-pro", "price_in_cents": 29900, "interval": 1, "interval_unit": "month", "require_credit_card": false } },
              { "product": { "id": 7126958, "name": "Basic Plan", "handle": "basic-plan", "price_in_cents": 2900, "interval": 1, "interval_unit": "month", "require_credit_card": false } }
            ]
            """)
        });
        var service = CreateService(handler, RepositoryReturning(null));

        var plans = await service.ListPlansAsync(default);

        Assert.Equal(2, plans.Count);
        Assert.Equal("eshop-pro", plans[0].Handle);
        Assert.Equal(299.00m, plans[0].Price);
        Assert.Equal("month", plans[0].IntervalUnit);
        Assert.False(plans[0].RequiresCreditCard);
    }

    [Fact]
    public async Task SubscribeAsync_NoExistingCustomerOrSubscription_CreatesBoth()
    {
        var handler = new SequencedStubHandler(new[]
        {
            (HttpStatusCode.NotFound, "{}"),                                            // ReadCustomerByReference: not found
            (HttpStatusCode.Created, """{ "customer": { "id": 501, "first_name": "demouser", "last_name": "eShopOnWeb Customer", "email": "demouser@microsoft.com", "reference": "eshop-user-demouser-microsoft-com" } }"""), // CreateCustomer
            (HttpStatusCode.NotFound, "{}"),                                            // FindSubscription: not found
            (HttpStatusCode.OK, "[]"),                                                  // ListCustomerSubscriptions: none yet
            (HttpStatusCode.Created, """{ "subscription": { "id": 9001, "state": "active", "next_assessment_at": "2026-10-01T00:00:00Z", "current_period_ends_at": "2026-10-01T00:00:00Z", "current_billing_amount_in_cents": 29900, "product_price_in_cents": 29900, "currency": "USD", "product": { "id": 7126957, "name": "Pro Plan", "handle": "eshop-pro" } } }"""), // CreateSubscription
        });
        var service = CreateService(handler, RepositoryReturning(null));

        var subscription = await service.SubscribeAsync("demouser@microsoft.com", "eshop-pro", default);

        Assert.Equal(9001, subscription.SubscriptionId);
        Assert.Equal("active", subscription.State);
        Assert.Equal("eshop-pro", subscription.PlanHandle);
        Assert.Equal("Pro Plan", subscription.PlanName);
        Assert.Equal(299.00m, subscription.Price);
        Assert.Equal("USD", subscription.Currency);
        Assert.Equal(5, handler.Requests.Count);
        Assert.Equal(HttpMethod.Post, handler.Requests[^1].Method);
    }

    [Fact]
    public async Task SubscribeAsync_CustomerCreateRaceWithUnparseableErrorBody_RecoversExistingCustomer()
    {
        // Simulates two concurrent double-clicks racing to create the same customer reference:
        // the loser's 422 body doesn't match the generated error shape, so the SDK throws
        // JsonException instead of SdkException<CreateCustomerError> (see dotnet-error-handling).
        // The service must still recover by re-reading, rather than surfacing a bare 502.
        var handler = new SequencedStubHandler(new[]
        {
            (HttpStatusCode.NotFound, "{}"),                          // ReadCustomerByReference: not found
            (HttpStatusCode.UnprocessableEntity, "not-json-at-all"),  // CreateCustomer: malformed error body
            (HttpStatusCode.OK, """{ "customer": { "id": 501, "first_name": "demouser", "last_name": "eShopOnWeb Customer", "email": "demouser@microsoft.com", "reference": "eshop-user-demouser-microsoft-com" } }"""), // recovery re-read finds the race winner's customer
            (HttpStatusCode.OK, """{ "subscription": { "id": 9001, "state": "active", "next_assessment_at": "2026-10-01T00:00:00Z", "current_billing_amount_in_cents": 29900, "currency": "USD", "product": { "id": 7126957, "name": "Pro Plan", "handle": "eshop-pro" } } }"""), // FindSubscription: already created by the race winner
        });
        var service = CreateService(handler, RepositoryReturning(null));

        var subscription = await service.SubscribeAsync("demouser@microsoft.com", "eshop-pro", default);

        Assert.Equal(9001, subscription.SubscriptionId);
        Assert.Equal(4, handler.Requests.Count);
    }

    [Fact]
    public async Task SubscribeAsync_ExistingSubscriptionForReference_DoesNotCreateDuplicate()
    {
        var cachedMapping = new MaxioCustomerMapping("demouser@microsoft.com", "eshop-user-demouser-microsoft-com", 501);
        var handler = new SequencedStubHandler(new[]
        {
            (HttpStatusCode.OK, """{ "subscription": { "id": 9001, "state": "active", "next_assessment_at": "2026-10-01T00:00:00Z", "current_billing_amount_in_cents": 29900, "currency": "USD", "product": { "id": 7126957, "name": "Pro Plan", "handle": "eshop-pro" } } }"""), // FindSubscription: already exists
        });
        var service = CreateService(handler, RepositoryReturning(cachedMapping));

        var subscription = await service.SubscribeAsync("demouser@microsoft.com", "eshop-pro", default);

        Assert.Equal(9001, subscription.SubscriptionId);
        Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Get, handler.Requests[0].Method);
    }
}
