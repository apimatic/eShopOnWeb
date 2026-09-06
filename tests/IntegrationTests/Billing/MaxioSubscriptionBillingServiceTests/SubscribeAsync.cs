using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using Xunit;

namespace Microsoft.eShopWeb.IntegrationTests.Billing.MaxioSubscriptionBillingServiceTests;

public class SubscribeAsync
{
    private const string PlanHandle = "pro-plan";

    private static Subscriber Shopper() => new Subscriber("shopper@example.com", "shopper@example.com");

    private static HttpResponseMessage PlansResponse() => StubTransport.Ok(MaxioTestHarness.ProductsJson(
        MaxioTestHarness.Product(PlanHandle, "Pro Plan", 29900)));

    [Fact]
    public async Task CreatesTheCustomerThenTheSubscriptionWhenNeitherExists()
    {
        var transport = new StubTransport(request =>
        {
            if (request.Matches(HttpMethod.Get, "/products.json")) return PlansResponse();
            if (request.Matches(HttpMethod.Get, "/customers/lookup.json")) return StubTransport.Json(HttpStatusCode.NotFound, "{}");
            if (request.Matches(HttpMethod.Post, "/customers.json")) return StubTransport.Json(HttpStatusCode.Created, MaxioTestHarness.CustomerJson(500, "eshoponweb-shopper@example.com"));
            if (request.Matches(HttpMethod.Get, "/subscriptions.json") && request.Method == HttpMethod.Get) return StubTransport.Ok("[]");
            if (request.Matches(HttpMethod.Post, "/subscriptions.json")) return StubTransport.Json(HttpStatusCode.Created, MaxioTestHarness.SubscriptionJson(9001, "active", PlanHandle, 29900));
            return StubTransport.Ok("[]");
        });

        var result = await MaxioTestHarness.CreateService(transport).SubscribeAsync(Shopper(), PlanHandle);

        Assert.False(result.AlreadySubscribed);
        Assert.Equal(9001, result.Subscription.Id);
        Assert.Equal("active", result.Subscription.State);
        Assert.True(result.Subscription.IsLive);
        Assert.Equal(PlanHandle, result.Subscription.PlanHandle);
        Assert.Equal(299.00m, result.Subscription.Price);
        Assert.Equal(new DateTimeOffset(2026, 10, 6, 16, 47, 12, TimeSpan.FromHours(5)), result.Subscription.NextBillingDate);
    }

    [Fact]
    public async Task SendsTheCustomerReferenceDerivedFromTheAccountSoTheProviderCanEnforceOneCustomerPerShopper()
    {
        var transport = NewCustomerTransport();

        await MaxioTestHarness.CreateService(transport).SubscribeAsync(Shopper(), PlanHandle);

        var createCustomer = transport.FirstOf(HttpMethod.Post, "/customers.json");
        Assert.Contains("\"reference\":\"eshoponweb-shopper@example.com\"", createCustomer.Body);
        Assert.Contains("\"email\":\"shopper@example.com\"", createCustomer.Body);

        var lookup = transport.FirstOf(HttpMethod.Get, "/customers/lookup.json");
        Assert.Contains("reference=eshoponweb-shopper%40example.com", lookup.Uri.Query);
    }

    [Fact]
    public async Task SubscribesByPlanHandleAndCustomerIdWithoutCapturingAPaymentMethod()
    {
        var transport = NewCustomerTransport();

        await MaxioTestHarness.CreateService(transport).SubscribeAsync(Shopper(), PlanHandle);

        var body = transport.FirstOf(HttpMethod.Post, "/subscriptions.json").Body;
        Assert.Contains("\"product_handle\":\"" + PlanHandle + "\"", body);
        Assert.Contains("\"customer_id\":500", body);
        Assert.Contains("\"reference\":\"eshoponweb-shopper@example.com-pro-plan\"", body);
        Assert.Contains("\"payment_collection_method\":\"remittance\"", body);
        Assert.DoesNotContain("credit_card", body);
    }

    [Fact]
    public async Task ReturnsTheExistingSubscriptionInsteadOfCreatingASecondOne()
    {
        var transport = new StubTransport(request =>
        {
            if (request.Matches(HttpMethod.Get, "/products.json")) return PlansResponse();
            if (request.Matches(HttpMethod.Get, "/customers/lookup.json")) return StubTransport.Ok(MaxioTestHarness.CustomerJson(500, "eshoponweb-shopper@example.com"));
            if (request.Matches(HttpMethod.Get, "/customers/500/subscriptions.json"))
            {
                return StubTransport.Ok(MaxioTestHarness.SubscriptionsJson(
                    MaxioTestHarness.SubscriptionJson(9001, "active", PlanHandle, 29900)));
            }

            return StubTransport.Ok("[]");
        });

        var result = await MaxioTestHarness.CreateService(transport).SubscribeAsync(Shopper(), PlanHandle);

        Assert.True(result.AlreadySubscribed);
        Assert.Equal(9001, result.Subscription.Id);
        Assert.Equal(0, transport.CountOf(HttpMethod.Post, "/subscriptions.json"));
        Assert.Equal(0, transport.CountOf(HttpMethod.Post, "/customers.json"));
    }

    [Theory]
    [InlineData("canceled")]
    [InlineData("expired")]
    [InlineData("failed_to_create")]
    public async Task CreatesAFreshSubscriptionWhenTheOnlyExistingOneHasEnded(string terminalState)
    {
        var transport = new StubTransport(request =>
        {
            if (request.Matches(HttpMethod.Get, "/products.json")) return PlansResponse();
            if (request.Matches(HttpMethod.Get, "/customers/lookup.json")) return StubTransport.Ok(MaxioTestHarness.CustomerJson(500, "eshoponweb-shopper@example.com"));
            if (request.Matches(HttpMethod.Get, "/customers/500/subscriptions.json"))
            {
                return StubTransport.Ok(MaxioTestHarness.SubscriptionsJson(
                    MaxioTestHarness.SubscriptionJson(8000, terminalState, PlanHandle, 29900)));
            }

            if (request.Matches(HttpMethod.Post, "/subscriptions.json"))
            {
                return StubTransport.Json(HttpStatusCode.Created, MaxioTestHarness.SubscriptionJson(9002, "active", PlanHandle, 29900));
            }

            return StubTransport.Ok("[]");
        });

        var result = await MaxioTestHarness.CreateService(transport).SubscribeAsync(Shopper(), PlanHandle);

        Assert.False(result.AlreadySubscribed);
        Assert.Equal(9002, result.Subscription.Id);
    }

    [Fact]
    public async Task RejectsAPlanThatIsNotInTheConfiguredFamilyAndSaysWhatIsOnOffer()
    {
        var transport = new StubTransport(_ => PlansResponse());

        var exception = await Assert.ThrowsAsync<SubscriptionBillingException>(
            () => MaxioTestHarness.CreateService(transport).SubscribeAsync(Shopper(), "not-on-offer"));

        Assert.Equal(HttpStatusCode.NotFound, exception.StatusCode);
        Assert.Contains(PlanHandle, exception.Details);
        Assert.Equal(0, transport.CountOf(HttpMethod.Post, "/subscriptions.json"));
    }

    [Fact]
    public async Task SurfacesAProviderValidationRejectionAsUnprocessableEntityWithItsMessages()
    {
        var transport = new StubTransport(request =>
        {
            if (request.Matches(HttpMethod.Get, "/products.json")) return PlansResponse();
            if (request.Matches(HttpMethod.Get, "/customers/lookup.json")) return StubTransport.Ok(MaxioTestHarness.CustomerJson(500, "eshoponweb-shopper@example.com"));
            if (request.Matches(HttpMethod.Post, "/subscriptions.json"))
            {
                return StubTransport.Json((HttpStatusCode)422, "{\"errors\":[\"No payment method was on file for the $299.00 balance\"]}");
            }

            return StubTransport.Ok("[]");
        });

        var exception = await Assert.ThrowsAsync<SubscriptionBillingException>(
            () => MaxioTestHarness.CreateService(transport).SubscribeAsync(Shopper(), PlanHandle));

        Assert.Equal(HttpStatusCode.UnprocessableEntity, exception.StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, exception.ProviderStatusCode);
        Assert.Contains("No payment method was on file for the $299.00 balance", exception.Details);
    }

    [Fact]
    public async Task SendsTheSubscriptionCreateExactlyOnceWhenTheConnectionFails()
    {
        // Transport failures are retried on every verb and that cannot be switched off, so without
        // the write-once guard one click could enroll the shopper more than once.
        var transport = new StubTransport(request =>
        {
            if (request.Matches(HttpMethod.Get, "/products.json")) return PlansResponse();
            if (request.Matches(HttpMethod.Get, "/customers/lookup.json")) return StubTransport.Ok(MaxioTestHarness.CustomerJson(500, "eshoponweb-shopper@example.com"));
            if (request.Matches(HttpMethod.Post, "/subscriptions.json")) throw new HttpRequestException("connection reset");
            return StubTransport.Ok("[]");
        });

        var exception = await Assert.ThrowsAsync<SubscriptionBillingException>(
            () => MaxioTestHarness.CreateService(transport).SubscribeAsync(Shopper(), PlanHandle));

        Assert.Equal(1, transport.CountOf(HttpMethod.Post, "/subscriptions.json"));
        Assert.Equal(HttpStatusCode.ServiceUnavailable, exception.StatusCode);
    }

    [Fact]
    public async Task ReturnsTheSubscriptionFoundByReconciliationWhenTheCreateOutcomeIsUnknown()
    {
        var createAttempted = 0;
        var transport = new StubTransport(request =>
        {
            if (request.Matches(HttpMethod.Get, "/products.json")) return PlansResponse();
            if (request.Matches(HttpMethod.Get, "/customers/lookup.json")) return StubTransport.Ok(MaxioTestHarness.CustomerJson(500, "eshoponweb-shopper@example.com"));

            if (request.Matches(HttpMethod.Post, "/subscriptions.json"))
            {
                // The write did reach the provider; only the answer was lost.
                Interlocked.Increment(ref createAttempted);
                throw new HttpRequestException("connection reset after the request was received");
            }

            if (request.Matches(HttpMethod.Get, "/customers/500/subscriptions.json"))
            {
                return createAttempted == 0
                    ? StubTransport.Ok("[]")
                    : StubTransport.Ok(MaxioTestHarness.SubscriptionsJson(
                        MaxioTestHarness.SubscriptionJson(9003, "active", PlanHandle, 29900)));
            }

            return StubTransport.Ok("[]");
        });

        var result = await MaxioTestHarness.CreateService(transport).SubscribeAsync(Shopper(), PlanHandle);

        Assert.Equal(9003, result.Subscription.Id);
        Assert.Equal(1, transport.CountOf(HttpMethod.Post, "/subscriptions.json"));
    }

    [Fact]
    public async Task ContinuesWithTheExistingCustomerWhenTheCreateLosesTheRaceOnTheReference()
    {
        var lookups = 0;
        var transport = new StubTransport(request =>
        {
            if (request.Matches(HttpMethod.Get, "/products.json")) return PlansResponse();

            if (request.Matches(HttpMethod.Get, "/customers/lookup.json"))
            {
                // Miss first, then a concurrent request wins the race and the customer exists.
                return Interlocked.Increment(ref lookups) == 1
                    ? StubTransport.Json(HttpStatusCode.NotFound, "{}")
                    : StubTransport.Ok(MaxioTestHarness.CustomerJson(500, "eshoponweb-shopper@example.com"));
            }

            if (request.Matches(HttpMethod.Post, "/customers.json"))
            {
                return StubTransport.Json((HttpStatusCode)422, "{\"errors\":{}}");
            }

            if (request.Matches(HttpMethod.Post, "/subscriptions.json"))
            {
                return StubTransport.Json(HttpStatusCode.Created, MaxioTestHarness.SubscriptionJson(9004, "active", PlanHandle, 29900));
            }

            return StubTransport.Ok("[]");
        });

        var result = await MaxioTestHarness.CreateService(transport).SubscribeAsync(Shopper(), PlanHandle);

        Assert.Equal(9004, result.Subscription.Id);
        Assert.Equal(1, transport.CountOf(HttpMethod.Post, "/customers.json"));
    }

    [Fact]
    public async Task CreatesOnlyOneSubscriptionWhenTheSameShopperSubscribesConcurrently()
    {
        var created = 0;
        var transport = new StubTransport(request =>
        {
            if (request.Matches(HttpMethod.Get, "/products.json")) return PlansResponse();
            if (request.Matches(HttpMethod.Get, "/customers/lookup.json")) return StubTransport.Ok(MaxioTestHarness.CustomerJson(500, "eshoponweb-shopper@example.com"));

            if (request.Matches(HttpMethod.Post, "/subscriptions.json"))
            {
                Interlocked.Increment(ref created);
                return StubTransport.Json(HttpStatusCode.Created, MaxioTestHarness.SubscriptionJson(9005, "active", PlanHandle, 29900));
            }

            if (request.Matches(HttpMethod.Get, "/customers/500/subscriptions.json"))
            {
                return Volatile.Read(ref created) == 0
                    ? StubTransport.Ok("[]")
                    : StubTransport.Ok(MaxioTestHarness.SubscriptionsJson(
                        MaxioTestHarness.SubscriptionJson(9005, "active", PlanHandle, 29900)));
            }

            return StubTransport.Ok("[]");
        });

        var service = MaxioTestHarness.CreateService(transport);
        var shopper = Shopper();

        var results = await Task.WhenAll(Enumerable.Range(0, 8)
            .Select(_ => service.SubscribeAsync(shopper, PlanHandle)));

        Assert.Equal(1, transport.CountOf(HttpMethod.Post, "/subscriptions.json"));
        Assert.All(results, result => Assert.Equal(9005, result.Subscription.Id));
        Assert.Single(results.Where(result => !result.AlreadySubscribed));
    }

    private static StubTransport NewCustomerTransport() => new StubTransport(request =>
    {
        if (request.Matches(HttpMethod.Get, "/products.json")) return PlansResponse();
        if (request.Matches(HttpMethod.Get, "/customers/lookup.json")) return StubTransport.Json(HttpStatusCode.NotFound, "{}");
        if (request.Matches(HttpMethod.Post, "/customers.json")) return StubTransport.Json(HttpStatusCode.Created, MaxioTestHarness.CustomerJson(500, "eshoponweb-shopper@example.com"));
        if (request.Matches(HttpMethod.Post, "/subscriptions.json")) return StubTransport.Json(HttpStatusCode.Created, MaxioTestHarness.SubscriptionJson(9001, "active", PlanHandle, 29900));
        return StubTransport.Ok("[]");
    });
}
