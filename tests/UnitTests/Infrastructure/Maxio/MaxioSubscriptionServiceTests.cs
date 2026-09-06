using System.Net;
using System.Net.Http;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using Microsoft.eShopWeb.Infrastructure.Maxio;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Maxio;

public class MaxioSubscriptionServiceTests
{
    private static (HttpStatusCode, string) NotFound() => (HttpStatusCode.NotFound, "");

    [Fact]
    public async Task ListPlansSkipsArchivedProductsAndOrdersByPrice()
    {
        var handler = new StubMaxioHandler(request => request.PathAndQuery switch
        {
            var p when p.Contains("/site.json") => (HttpStatusCode.OK, MaxioTestHarness.SiteJson()),
            var p when p.Contains("/products.json") => (HttpStatusCode.OK, MaxioTestHarness.ProductsJson()),
            _ => NotFound()
        });

        var result = await MaxioTestHarness.BuildService(handler).ListPlansAsync();

        Assert.True(result.IsSuccess);
        Assert.Equal(new[] { "card-plan", "basic-plan", "eshop-pro" }, result.Value!.Select(p => p.Handle));
        Assert.DoesNotContain(result.Value!, p => p.Handle == "retired-plan");
    }

    [Fact]
    public async Task ListPlansProjectsPriceIntervalAndSiteCurrency()
    {
        var handler = new StubMaxioHandler(request => request.PathAndQuery switch
        {
            var p when p.Contains("/site.json") => (HttpStatusCode.OK, MaxioTestHarness.SiteJson(currency: "EUR")),
            var p when p.Contains("/products.json") => (HttpStatusCode.OK, MaxioTestHarness.ProductsJson()),
            _ => NotFound()
        });

        var result = await MaxioTestHarness.BuildService(handler).ListPlansAsync();
        var pro = result.Value!.Single(p => p.Handle == "eshop-pro");

        Assert.Equal("Pro Plan", pro.Name);
        Assert.Equal(29900, pro.PriceInCents);
        Assert.Equal(299m, pro.Price);
        Assert.Equal("EUR", pro.Currency);
        Assert.Equal(1, pro.Interval);
        Assert.Equal("month", pro.IntervalUnit);
        Assert.False(pro.RequiresPaymentMethod);
        Assert.True(result.Value!.Single(p => p.Handle == "card-plan").RequiresPaymentMethod);
    }

    [Fact]
    public async Task ListPlansAddressesTheProductFamilyByHandle()
    {
        var handler = new StubMaxioHandler(request => request.PathAndQuery switch
        {
            var p when p.Contains("/site.json") => (HttpStatusCode.OK, MaxioTestHarness.SiteJson()),
            var p when p.Contains("/products.json") => (HttpStatusCode.OK, MaxioTestHarness.ProductsJson()),
            _ => NotFound()
        });

        await MaxioTestHarness.BuildService(handler).ListPlansAsync();

        Assert.Contains(handler.Requests, r => r.PathAndQuery.Contains("/product_families/handle:demo-plans/products.json"));
    }

    [Fact]
    public async Task SubscribeCreatesTheCustomerThenTheSubscription()
    {
        var handler = new StubMaxioHandler(request => request switch
        {
            { PathAndQuery: var p } when p.Contains("/site.json") => (HttpStatusCode.OK, MaxioTestHarness.SiteJson()),
            { PathAndQuery: var p } when p.Contains("/products.json") => (HttpStatusCode.OK, MaxioTestHarness.ProductsJson()),
            { PathAndQuery: var p } when p.Contains("/customers/lookup.json") => NotFound(),
            { Method: var m, PathAndQuery: var p } when m == HttpMethod.Post && p.Contains("/customers.json") => (HttpStatusCode.Created, MaxioTestHarness.CustomerJson()),
            { PathAndQuery: var p } when p.Contains("/customers/555/subscriptions.json") => (HttpStatusCode.OK, "[]"),
            { Method: var m, PathAndQuery: var p } when m == HttpMethod.Post && p.Contains("/subscriptions.json") => (HttpStatusCode.Created, MaxioTestHarness.SubscriptionJson()),
            _ => NotFound()
        });

        var result = await MaxioTestHarness.BuildService(handler).SubscribeAsync(new SubscribeRequest
        {
            Subscriber = MaxioTestHarness.Subscriber(),
            PlanHandle = "eshop-pro"
        });

        Assert.True(result.IsSuccess);
        Assert.False(result.Value!.AlreadyExisted);

        var subscription = result.Value.Subscription;
        Assert.Equal(900, subscription.Id);
        Assert.Equal("active", subscription.State);
        Assert.True(subscription.IsLive);
        Assert.Equal("eshop-pro", subscription.PlanHandle);
        Assert.Equal(299m, subscription.Price);
        Assert.Equal(new DateTimeOffset(2026, 10, 6, 12, 0, 0, TimeSpan.Zero), subscription.NextBillingAt);

        Assert.Equal(1, handler.CountOf(HttpMethod.Post, "/customers.json"));
        Assert.Equal(1, handler.CountOf(HttpMethod.Post, "/subscriptions.json"));
    }

    [Fact]
    public async Task SubscribeStoresTheUserKeyAsTheCustomerReferenceAndSubscribesByHandle()
    {
        string? customerBody = null;
        string? subscriptionBody = null;

        var handler = new StubMaxioHandler(request =>
        {
            switch (request)
            {
                case { PathAndQuery: var p } when p.Contains("/site.json"):
                    return (HttpStatusCode.OK, MaxioTestHarness.SiteJson());
                case { PathAndQuery: var p } when p.Contains("/products.json"):
                    return (HttpStatusCode.OK, MaxioTestHarness.ProductsJson());
                case { PathAndQuery: var p } when p.Contains("/customers/lookup.json"):
                    return NotFound();
                case { Method: var m, PathAndQuery: var p } when m == HttpMethod.Post && p.Contains("/customers.json"):
                    customerBody = request.Body;
                    return (HttpStatusCode.Created, MaxioTestHarness.CustomerJson());
                case { PathAndQuery: var p } when p.Contains("/customers/555/subscriptions.json"):
                    return (HttpStatusCode.OK, "[]");
                case { Method: var m, PathAndQuery: var p } when m == HttpMethod.Post && p.Contains("/subscriptions.json"):
                    subscriptionBody = request.Body;
                    return (HttpStatusCode.Created, MaxioTestHarness.SubscriptionJson());
                default:
                    return NotFound();
            }
        });

        await MaxioTestHarness.BuildService(handler).SubscribeAsync(new SubscribeRequest
        {
            Subscriber = MaxioTestHarness.Subscriber(),
            PlanHandle = "eshop-pro"
        });

        // The reference is lower-cased so that a normalized user name and a raw one land on the
        // same billing customer.
        Assert.Contains("\"reference\":\"eshoponweb:demouser@microsoft.com\"", customerBody);
        Assert.Contains("\"first_name\":\"Demouser\"", customerBody);
        Assert.Contains("\"email\":\"demouser@microsoft.com\"", customerBody);

        Assert.Contains("\"product_handle\":\"eshop-pro\"", subscriptionBody);
        Assert.Contains("\"customer_id\":555", subscriptionBody);
        Assert.Contains("\"reference\":\"eshoponweb:demouser@microsoft.com:eshop-pro\"", subscriptionBody);
        Assert.Contains("\"payment_collection_method\":\"remittance\"", subscriptionBody);
    }

    [Fact]
    public async Task SubscribeAsksForInvoicingOnSitesWithoutRelationshipInvoicing()
    {
        string? subscriptionBody = null;

        var handler = new StubMaxioHandler(request =>
        {
            switch (request)
            {
                case { PathAndQuery: var p } when p.Contains("/site.json"):
                    return (HttpStatusCode.OK, MaxioTestHarness.SiteJson(relationshipInvoicing: false));
                case { PathAndQuery: var p } when p.Contains("/products.json"):
                    return (HttpStatusCode.OK, MaxioTestHarness.ProductsJson());
                case { PathAndQuery: var p } when p.Contains("/customers/lookup.json"):
                    return (HttpStatusCode.OK, MaxioTestHarness.CustomerJson());
                case { PathAndQuery: var p } when p.Contains("/customers/555/subscriptions.json"):
                    return (HttpStatusCode.OK, "[]");
                case { Method: var m, PathAndQuery: var p } when m == HttpMethod.Post && p.Contains("/subscriptions.json"):
                    subscriptionBody = request.Body;
                    return (HttpStatusCode.Created, MaxioTestHarness.SubscriptionJson());
                default:
                    return NotFound();
            }
        });

        await MaxioTestHarness.BuildService(handler).SubscribeAsync(new SubscribeRequest
        {
            Subscriber = MaxioTestHarness.Subscriber(),
            PlanHandle = "eshop-pro"
        });

        Assert.Contains("\"payment_collection_method\":\"invoice\"", subscriptionBody);
    }

    [Fact]
    public async Task SubscribeReusesAnExistingCustomerAndReturnsAnExistingSubscription()
    {
        var handler = new StubMaxioHandler(request => request switch
        {
            { PathAndQuery: var p } when p.Contains("/site.json") => (HttpStatusCode.OK, MaxioTestHarness.SiteJson()),
            { PathAndQuery: var p } when p.Contains("/products.json") => (HttpStatusCode.OK, MaxioTestHarness.ProductsJson()),
            { PathAndQuery: var p } when p.Contains("/customers/lookup.json") => (HttpStatusCode.OK, MaxioTestHarness.CustomerJson()),
            { PathAndQuery: var p } when p.Contains("/customers/555/subscriptions.json") =>
                (HttpStatusCode.OK, MaxioTestHarness.SubscriptionListJson(MaxioTestHarness.SubscriptionJson())),
            _ => NotFound()
        });

        var result = await MaxioTestHarness.BuildService(handler).SubscribeAsync(new SubscribeRequest
        {
            Subscriber = MaxioTestHarness.Subscriber(),
            PlanHandle = "eshop-pro"
        });

        Assert.True(result.IsSuccess);
        Assert.True(result.Value!.AlreadyExisted);
        Assert.Equal(900, result.Value.Subscription.Id);
        Assert.Equal(0, handler.CountOf(HttpMethod.Post, "/customers.json"));
        Assert.Equal(0, handler.CountOf(HttpMethod.Post, "/subscriptions.json"));
    }

    [Fact]
    public async Task SubscribeIgnoresSubscriptionsToOtherPlansAndSubscriptionsThatHaveEnded()
    {
        var handler = new StubMaxioHandler(request => request switch
        {
            { PathAndQuery: var p } when p.Contains("/site.json") => (HttpStatusCode.OK, MaxioTestHarness.SiteJson()),
            { PathAndQuery: var p } when p.Contains("/products.json") => (HttpStatusCode.OK, MaxioTestHarness.ProductsJson()),
            { PathAndQuery: var p } when p.Contains("/customers/lookup.json") => (HttpStatusCode.OK, MaxioTestHarness.CustomerJson()),
            { PathAndQuery: var p } when p.Contains("/customers/555/subscriptions.json") => (HttpStatusCode.OK, MaxioTestHarness.SubscriptionListJson(
                MaxioTestHarness.SubscriptionJson(id: 800, state: "active", productHandle: "basic-plan"),
                MaxioTestHarness.SubscriptionJson(id: 801, state: "canceled"))),
            { Method: var m, PathAndQuery: var p } when m == HttpMethod.Post && p.Contains("/subscriptions.json") =>
                (HttpStatusCode.Created, MaxioTestHarness.SubscriptionJson(id: 902)),
            _ => NotFound()
        });

        var result = await MaxioTestHarness.BuildService(handler).SubscribeAsync(new SubscribeRequest
        {
            Subscriber = MaxioTestHarness.Subscriber(),
            PlanHandle = "eshop-pro"
        });

        Assert.True(result.IsSuccess);
        Assert.False(result.Value!.AlreadyExisted);
        Assert.Equal(902, result.Value.Subscription.Id);
    }

    [Fact]
    public async Task SubscribeTreatsAProblemStateSubscriptionAsStillInForce()
    {
        var handler = new StubMaxioHandler(request => request switch
        {
            { PathAndQuery: var p } when p.Contains("/site.json") => (HttpStatusCode.OK, MaxioTestHarness.SiteJson()),
            { PathAndQuery: var p } when p.Contains("/products.json") => (HttpStatusCode.OK, MaxioTestHarness.ProductsJson()),
            { PathAndQuery: var p } when p.Contains("/customers/lookup.json") => (HttpStatusCode.OK, MaxioTestHarness.CustomerJson()),
            { PathAndQuery: var p } when p.Contains("/customers/555/subscriptions.json") =>
                (HttpStatusCode.OK, MaxioTestHarness.SubscriptionListJson(MaxioTestHarness.SubscriptionJson(state: "past_due"))),
            _ => NotFound()
        });

        var result = await MaxioTestHarness.BuildService(handler).SubscribeAsync(new SubscribeRequest
        {
            Subscriber = MaxioTestHarness.Subscriber(),
            PlanHandle = "eshop-pro"
        });

        Assert.True(result.IsSuccess);
        Assert.True(result.Value!.AlreadyExisted);
        Assert.Equal(0, handler.CountOf(HttpMethod.Post, "/subscriptions.json"));
    }

    [Fact]
    public async Task ConcurrentSubscribeAttemptsCreateOnlyOneSubscription()
    {
        var subscriptionCreated = 0;
        var customersCreated = 0;

        var handler = new StubMaxioHandler(request =>
        {
            switch (request)
            {
                case { PathAndQuery: var p } when p.Contains("/site.json"):
                    return (HttpStatusCode.OK, MaxioTestHarness.SiteJson());
                case { PathAndQuery: var p } when p.Contains("/products.json"):
                    return (HttpStatusCode.OK, MaxioTestHarness.ProductsJson());
                case { PathAndQuery: var p } when p.Contains("/customers/lookup.json"):
                    return Volatile.Read(ref customersCreated) == 0 ? NotFound() : (HttpStatusCode.OK, MaxioTestHarness.CustomerJson());
                case { Method: var m, PathAndQuery: var p } when m == HttpMethod.Post && p.Contains("/customers.json"):
                    Interlocked.Increment(ref customersCreated);
                    return (HttpStatusCode.Created, MaxioTestHarness.CustomerJson());
                case { PathAndQuery: var p } when p.Contains("/customers/555/subscriptions.json"):
                    return Volatile.Read(ref subscriptionCreated) == 0
                        ? (HttpStatusCode.OK, "[]")
                        : (HttpStatusCode.OK, MaxioTestHarness.SubscriptionListJson(MaxioTestHarness.SubscriptionJson()));
                case { Method: var m, PathAndQuery: var p } when m == HttpMethod.Post && p.Contains("/subscriptions.json"):
                    Interlocked.Increment(ref subscriptionCreated);
                    return (HttpStatusCode.Created, MaxioTestHarness.SubscriptionJson());
                default:
                    return NotFound();
            }
        })
        {
            // Widen the race window so an unsynchronised implementation would fail this test.
            ResponseDelay = TimeSpan.FromMilliseconds(25)
        };

        var service = MaxioTestHarness.BuildService(handler);
        var request = () => service.SubscribeAsync(new SubscribeRequest
        {
            Subscriber = MaxioTestHarness.Subscriber(),
            PlanHandle = "eshop-pro"
        });

        var results = await Task.WhenAll(Task.Run(request), Task.Run(request), Task.Run(request));

        Assert.All(results, r => Assert.True(r.IsSuccess));
        Assert.All(results, r => Assert.Equal(900, r.Value!.Subscription.Id));
        Assert.Equal(1, handler.CountOf(HttpMethod.Post, "/subscriptions.json"));
        Assert.Equal(1, handler.CountOf(HttpMethod.Post, "/customers.json"));

        // Exactly one caller is told it created something; the rest get the existing subscription.
        Assert.Single(results.Where(r => !r.Value!.AlreadyExisted));
    }

    [Fact]
    public async Task ARacingCustomerCreateIsResolvedByReadingTheCustomerBack()
    {
        var customerLookups = 0;

        var handler = new StubMaxioHandler(request =>
        {
            switch (request)
            {
                case { PathAndQuery: var p } when p.Contains("/site.json"):
                    return (HttpStatusCode.OK, MaxioTestHarness.SiteJson());
                case { PathAndQuery: var p } when p.Contains("/products.json"):
                    return (HttpStatusCode.OK, MaxioTestHarness.ProductsJson());
                case { PathAndQuery: var p } when p.Contains("/customers/lookup.json"):
                    // Missing on the first look, present once the other writer has committed.
                    return ++customerLookups == 1 ? NotFound() : (HttpStatusCode.OK, MaxioTestHarness.CustomerJson());
                case { Method: var m, PathAndQuery: var p } when m == HttpMethod.Post && p.Contains("/customers.json"):
                    return (HttpStatusCode.UnprocessableEntity, MaxioTestHarness.ErrorsJson("Reference: must be unique - that value has been taken."));
                case { PathAndQuery: var p } when p.Contains("/customers/555/subscriptions.json"):
                    return (HttpStatusCode.OK, "[]");
                case { Method: var m, PathAndQuery: var p } when m == HttpMethod.Post && p.Contains("/subscriptions.json"):
                    return (HttpStatusCode.Created, MaxioTestHarness.SubscriptionJson());
                default:
                    return NotFound();
            }
        });

        var result = await MaxioTestHarness.BuildService(handler).SubscribeAsync(new SubscribeRequest
        {
            Subscriber = MaxioTestHarness.Subscriber(),
            PlanHandle = "eshop-pro"
        });

        Assert.True(result.IsSuccess);
        Assert.Equal(900, result.Value!.Subscription.Id);
    }

    [Fact]
    public async Task AReferenceCollisionWithAnEndedSubscriptionRetriesWithAFreshReference()
    {
        var references = new List<string>();

        var handler = new StubMaxioHandler(request =>
        {
            switch (request)
            {
                case { PathAndQuery: var p } when p.Contains("/site.json"):
                    return (HttpStatusCode.OK, MaxioTestHarness.SiteJson());
                case { PathAndQuery: var p } when p.Contains("/products.json"):
                    return (HttpStatusCode.OK, MaxioTestHarness.ProductsJson());
                case { PathAndQuery: var p } when p.Contains("/customers/lookup.json"):
                    return (HttpStatusCode.OK, MaxioTestHarness.CustomerJson());
                case { PathAndQuery: var p } when p.Contains("/customers/555/subscriptions.json"):
                    // The old subscription is canceled, so the shopper may sign up again - but its
                    // reference is still taken for the life of the site.
                    return (HttpStatusCode.OK, MaxioTestHarness.SubscriptionListJson(MaxioTestHarness.SubscriptionJson(id: 700, state: "canceled")));
                case { Method: var m, PathAndQuery: var p, Body: var body } when m == HttpMethod.Post && p.Contains("/subscriptions.json"):
                    references.Add(body!);
                    return references.Count == 1
                        ? (HttpStatusCode.UnprocessableEntity, MaxioTestHarness.ErrorsJson("Reference: must be unique - that value has been taken."))
                        : (HttpStatusCode.Created, MaxioTestHarness.SubscriptionJson(id: 950));
                default:
                    return NotFound();
            }
        });

        var result = await MaxioTestHarness.BuildService(handler).SubscribeAsync(new SubscribeRequest
        {
            Subscriber = MaxioTestHarness.Subscriber(),
            PlanHandle = "eshop-pro"
        });

        Assert.True(result.IsSuccess);
        Assert.False(result.Value!.AlreadyExisted);
        Assert.Equal(950, result.Value.Subscription.Id);
        Assert.Equal(2, references.Count);
        Assert.Contains("\"reference\":\"eshoponweb:demouser@microsoft.com:eshop-pro\"", references[0]);
        Assert.DoesNotContain("\"reference\":\"eshoponweb:demouser@microsoft.com:eshop-pro\"", references[1]);
    }

    [Fact]
    public async Task ACallerIdempotencyKeyIsForwardedAsMaxiosUniquenessToken()
    {
        string? subscriptionBody = null;

        var handler = new StubMaxioHandler(request =>
        {
            switch (request)
            {
                case { PathAndQuery: var p } when p.Contains("/site.json"):
                    return (HttpStatusCode.OK, MaxioTestHarness.SiteJson());
                case { PathAndQuery: var p } when p.Contains("/products.json"):
                    return (HttpStatusCode.OK, MaxioTestHarness.ProductsJson());
                case { PathAndQuery: var p } when p.Contains("/customers/lookup.json"):
                    return (HttpStatusCode.OK, MaxioTestHarness.CustomerJson());
                case { PathAndQuery: var p } when p.Contains("/customers/555/subscriptions.json"):
                    return (HttpStatusCode.OK, "[]");
                case { Method: var m, PathAndQuery: var p } when m == HttpMethod.Post && p.Contains("/subscriptions.json"):
                    subscriptionBody = request.Body;
                    return (HttpStatusCode.Created, MaxioTestHarness.SubscriptionJson());
                default:
                    return NotFound();
            }
        });

        await MaxioTestHarness.BuildService(handler).SubscribeAsync(new SubscribeRequest
        {
            Subscriber = MaxioTestHarness.Subscriber(),
            PlanHandle = "eshop-pro",
            IdempotencyKey = "3f0d1c8e-1111-2222-3333-444455556666"
        });

        Assert.Contains("\"uniqueness_token\":\"3f0d1c8e-1111-2222-3333-444455556666\"", subscriptionBody);
    }

    [Fact]
    public async Task WithoutACallerKeyNoUniquenessTokenIsSentSoAFailedAttemptCanBeRetried()
    {
        string? subscriptionBody = null;

        var handler = new StubMaxioHandler(request =>
        {
            switch (request)
            {
                case { PathAndQuery: var p } when p.Contains("/site.json"):
                    return (HttpStatusCode.OK, MaxioTestHarness.SiteJson());
                case { PathAndQuery: var p } when p.Contains("/products.json"):
                    return (HttpStatusCode.OK, MaxioTestHarness.ProductsJson());
                case { PathAndQuery: var p } when p.Contains("/customers/lookup.json"):
                    return (HttpStatusCode.OK, MaxioTestHarness.CustomerJson());
                case { PathAndQuery: var p } when p.Contains("/customers/555/subscriptions.json"):
                    return (HttpStatusCode.OK, "[]");
                case { Method: var m, PathAndQuery: var p } when m == HttpMethod.Post && p.Contains("/subscriptions.json"):
                    subscriptionBody = request.Body;
                    return (HttpStatusCode.Created, MaxioTestHarness.SubscriptionJson());
                default:
                    return NotFound();
            }
        });

        await MaxioTestHarness.BuildService(handler).SubscribeAsync(new SubscribeRequest
        {
            Subscriber = MaxioTestHarness.Subscriber(),
            PlanHandle = "eshop-pro"
        });

        Assert.DoesNotContain("uniqueness_token", subscriptionBody);
    }

    [Fact]
    public async Task AReplayedIdempotencyKeyResolvesToTheSubscriptionTheOriginalRequestCreated()
    {
        var subscriptionListCalls = 0;

        var handler = new StubMaxioHandler(request =>
        {
            switch (request)
            {
                case { PathAndQuery: var p } when p.Contains("/site.json"):
                    return (HttpStatusCode.OK, MaxioTestHarness.SiteJson());
                case { PathAndQuery: var p } when p.Contains("/products.json"):
                    return (HttpStatusCode.OK, MaxioTestHarness.ProductsJson());
                case { PathAndQuery: var p } when p.Contains("/customers/lookup.json"):
                    return (HttpStatusCode.OK, MaxioTestHarness.CustomerJson());

                // Empty on the pre-check, populated by the time the duplicate is investigated:
                // the original request is what created it.
                case { PathAndQuery: var p } when p.Contains("/customers/555/subscriptions.json"):
                    return ++subscriptionListCalls == 1
                        ? (HttpStatusCode.OK, "[]")
                        : (HttpStatusCode.OK, MaxioTestHarness.SubscriptionListJson(MaxioTestHarness.SubscriptionJson()));
                case { Method: var m, PathAndQuery: var p } when m == HttpMethod.Post && p.Contains("/subscriptions.json"):
                    return (HttpStatusCode.Conflict, MaxioTestHarness.ErrorsJson("DuplicatePrevention::DuplicateSubmissionError"));
                default:
                    return NotFound();
            }
        });

        var result = await MaxioTestHarness.BuildService(handler).SubscribeAsync(new SubscribeRequest
        {
            Subscriber = MaxioTestHarness.Subscriber(),
            PlanHandle = "eshop-pro",
            IdempotencyKey = "replayed-key"
        });

        Assert.True(result.IsSuccess);
        Assert.True(result.Value!.AlreadyExisted);
        Assert.Equal(900, result.Value.Subscription.Id);
    }

    [Fact]
    public async Task AReplayedIdempotencyKeyThatProducedNothingIsReportedAsAConflict()
    {
        var handler = new StubMaxioHandler(request => request switch
        {
            { PathAndQuery: var p } when p.Contains("/site.json") => (HttpStatusCode.OK, MaxioTestHarness.SiteJson()),
            { PathAndQuery: var p } when p.Contains("/products.json") => (HttpStatusCode.OK, MaxioTestHarness.ProductsJson()),
            { PathAndQuery: var p } when p.Contains("/customers/lookup.json") => (HttpStatusCode.OK, MaxioTestHarness.CustomerJson()),
            { PathAndQuery: var p } when p.Contains("/customers/555/subscriptions.json") => (HttpStatusCode.OK, "[]"),
            { Method: var m, PathAndQuery: var p } when m == HttpMethod.Post && p.Contains("/subscriptions.json") =>
                (HttpStatusCode.Conflict, MaxioTestHarness.ErrorsJson("DuplicatePrevention::DuplicateSubmissionError")),
            _ => NotFound()
        });

        var result = await MaxioTestHarness.BuildService(handler).SubscribeAsync(new SubscribeRequest
        {
            Subscriber = MaxioTestHarness.Subscriber(),
            PlanHandle = "eshop-pro",
            IdempotencyKey = "replayed-key"
        });

        Assert.False(result.IsSuccess);
        Assert.Equal(SubscriptionFailure.Conflict, result.Failure);
    }

    [Fact]
    public async Task SubscribingToAPlanOutsideTheConfiguredFamilyIsNotFound()
    {
        var handler = new StubMaxioHandler(request => request.PathAndQuery switch
        {
            var p when p.Contains("/site.json") => (HttpStatusCode.OK, MaxioTestHarness.SiteJson()),
            var p when p.Contains("/products.json") => (HttpStatusCode.OK, MaxioTestHarness.ProductsJson()),
            _ => NotFound()
        });

        var result = await MaxioTestHarness.BuildService(handler).SubscribeAsync(new SubscribeRequest
        {
            Subscriber = MaxioTestHarness.Subscriber(),
            PlanHandle = "some-other-sites-product"
        });

        Assert.False(result.IsSuccess);
        Assert.Equal(SubscriptionFailure.PlanNotFound, result.Failure);

        // Nothing was created before the plan was validated.
        Assert.Equal(0, handler.CountOf(HttpMethod.Post, "/customers.json"));
        Assert.Equal(0, handler.CountOf(HttpMethod.Post, "/subscriptions.json"));
    }

    [Fact]
    public async Task APlanThatNeedsACardIsRefusedBeforeAnythingIsCreated()
    {
        var handler = new StubMaxioHandler(request => request.PathAndQuery switch
        {
            var p when p.Contains("/site.json") => (HttpStatusCode.OK, MaxioTestHarness.SiteJson()),
            var p when p.Contains("/products.json") => (HttpStatusCode.OK, MaxioTestHarness.ProductsJson()),
            _ => NotFound()
        });

        var result = await MaxioTestHarness.BuildService(handler).SubscribeAsync(new SubscribeRequest
        {
            Subscriber = MaxioTestHarness.Subscriber(),
            PlanHandle = "card-plan"
        });

        Assert.False(result.IsSuccess);
        Assert.Equal(SubscriptionFailure.UpstreamRejected, result.Failure);
        Assert.Equal(0, handler.CountOf(HttpMethod.Post, "/customers.json"));
    }

    [Fact]
    public async Task AValidationFailureFromMaxioIsReportedAsAnUpstreamRejection()
    {
        var handler = new StubMaxioHandler(request => request switch
        {
            { PathAndQuery: var p } when p.Contains("/site.json") => (HttpStatusCode.OK, MaxioTestHarness.SiteJson()),
            { PathAndQuery: var p } when p.Contains("/products.json") => (HttpStatusCode.OK, MaxioTestHarness.ProductsJson()),
            { PathAndQuery: var p } when p.Contains("/customers/lookup.json") => (HttpStatusCode.OK, MaxioTestHarness.CustomerJson()),
            { PathAndQuery: var p } when p.Contains("/customers/555/subscriptions.json") => (HttpStatusCode.OK, "[]"),
            { Method: var m, PathAndQuery: var p } when m == HttpMethod.Post && p.Contains("/subscriptions.json") =>
                (HttpStatusCode.UnprocessableEntity, MaxioTestHarness.ErrorsJson("No payment method was on file for the $299.00 balance")),
            _ => NotFound()
        });

        var result = await MaxioTestHarness.BuildService(handler).SubscribeAsync(new SubscribeRequest
        {
            Subscriber = MaxioTestHarness.Subscriber(),
            PlanHandle = "eshop-pro"
        });

        Assert.False(result.IsSuccess);
        Assert.Equal(SubscriptionFailure.UpstreamRejected, result.Failure);
        Assert.Contains("No payment method", result.Message);
    }

    [Fact]
    public async Task AnOutageIsReportedAsUpstreamUnavailable()
    {
        var handler = new StubMaxioHandler(_ => (HttpStatusCode.InternalServerError, "{}"));

        var result = await MaxioTestHarness.BuildService(handler).ListPlansAsync();

        Assert.False(result.IsSuccess);
        Assert.Equal(SubscriptionFailure.UpstreamUnavailable, result.Failure);
    }

    [Fact]
    public async Task ListSubscriptionsReturnsNothingForAShopperWithNoBillingCustomerYet()
    {
        var handler = new StubMaxioHandler(_ => NotFound());

        var result = await MaxioTestHarness.BuildService(handler).ListSubscriptionsAsync(MaxioTestHarness.Subscriber());

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value!);
    }

    [Fact]
    public async Task ListSubscriptionsReturnsNewestFirst()
    {
        var handler = new StubMaxioHandler(request => request.PathAndQuery switch
        {
            var p when p.Contains("/customers/lookup.json") => (HttpStatusCode.OK, MaxioTestHarness.CustomerJson()),
            var p when p.Contains("/customers/555/subscriptions.json") => (HttpStatusCode.OK,
                "[" +
                "{\"subscription\":{\"id\":1,\"state\":\"canceled\",\"created_at\":\"2026-01-01T00:00:00+00:00\",\"product\":{\"handle\":\"basic-plan\"}}}," +
                "{\"subscription\":{\"id\":2,\"state\":\"active\",\"created_at\":\"2026-09-01T00:00:00+00:00\",\"product\":{\"handle\":\"eshop-pro\"}}}" +
                "]"),
            _ => NotFound()
        });

        var result = await MaxioTestHarness.BuildService(handler).ListSubscriptionsAsync(MaxioTestHarness.Subscriber());

        Assert.True(result.IsSuccess);
        Assert.Equal(new long[] { 2, 1 }, result.Value!.Select(s => s.Id));
        Assert.True(result.Value![0].IsLive);
        Assert.False(result.Value![1].IsLive);
    }

    [Fact]
    public async Task AnUnconfiguredDeploymentReportsItselfRatherThanCallingMaxio()
    {
        var handler = new StubMaxioHandler(_ => (HttpStatusCode.OK, "{}"));
        var service = MaxioTestHarness.BuildService(handler, new MaxioSettings());

        var plans = await service.ListPlansAsync();
        var subscriptions = await service.ListSubscriptionsAsync(MaxioTestHarness.Subscriber());
        var subscribe = await service.SubscribeAsync(new SubscribeRequest
        {
            Subscriber = MaxioTestHarness.Subscriber(),
            PlanHandle = "eshop-pro"
        });

        Assert.Equal(SubscriptionFailure.NotConfigured, plans.Failure);
        Assert.Equal(SubscriptionFailure.NotConfigured, subscriptions.Failure);
        Assert.Equal(SubscriptionFailure.NotConfigured, subscribe.Failure);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task SubscribeRequiresAPlanHandle()
    {
        var handler = new StubMaxioHandler(_ => (HttpStatusCode.OK, "{}"));

        var result = await MaxioTestHarness.BuildService(handler).SubscribeAsync(new SubscribeRequest
        {
            Subscriber = MaxioTestHarness.Subscriber(),
            PlanHandle = "  "
        });

        Assert.Equal(SubscriptionFailure.InvalidRequest, result.Failure);
        Assert.Empty(handler.Requests);
    }
}
