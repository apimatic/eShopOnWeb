using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using Microsoft.eShopWeb.PublicApi.Middleware;
using Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PublicApiIntegrationTests.SubscriptionEndpoints;

/// <summary>
/// Asserts what each subscription endpoint answers (status code and payload) for each billing outcome,
/// and how billing failures map to HTTP statuses.
/// </summary>
[TestClass]
public class SubscriptionEndpointHandlerTest
{
    private static readonly Subscriber User = new("user-1", "DEMOUSER@MICROSOFT.COM", "demouser@microsoft.com");

    private static readonly SubscriptionDetails Pro = new(500, "eshop-pro", "Pro Plan", 29900, "USD", "active",
        new DateTimeOffset(2026, 11, 15, 12, 0, 0, TimeSpan.Zero), null, DateTimeOffset.UtcNow);

    [TestMethod]
    public async Task PlansAreListedWithPrices()
    {
        var billing = new FakeBilling
        {
            Catalog = new SubscriptionPlanCatalog(new[]
            {
                new SubscriptionPlan("eshop-pro", "Pro Plan", null, 29900, 1, "month", false)
            }, IsTruncated: false)
        };

        var result = await new SubscriptionPlanListEndpoint().HandleAsync(billing);

        var response = AssertResult<ListSubscriptionPlansResponse>(result, StatusCodes.Status200OK);
        var plan = response.Plans.Single();
        Assert.AreEqual("eshop-pro", plan.Handle);
        Assert.AreEqual(299m, plan.Price);
        Assert.AreEqual("month", plan.IntervalUnit);
        Assert.IsFalse(response.IsTruncated);
    }

    [TestMethod]
    public async Task NewSubscriptionIsCreatedWithPlanPriceStateAndNextBillingDate()
    {
        var billing = new FakeBilling { Subscribe = new SubscribeResult(SubscribeOutcome.Created, Pro) };

        var result = await new CreateSubscriptionEndpoint().HandleAsync(new CreateSubscriptionRequest { PlanHandle = "eshop-pro" }, User, billing);

        var response = AssertResult<CreateSubscriptionResponse>(result, StatusCodes.Status201Created);
        Assert.AreEqual("Created", response.Outcome);
        Assert.AreEqual("eshop-pro", response.Subscription!.PlanHandle);
        Assert.AreEqual(299m, response.Subscription.Price);
        Assert.AreEqual("active", response.Subscription.State);
        Assert.AreEqual(Pro.NextBillingAt, response.Subscription.NextBillingDate);
        Assert.AreEqual("eshop-pro", billing.LastPlanHandle);
        Assert.AreSame(User, billing.LastSubscriber);
    }

    [TestMethod]
    public async Task RepeatSubscribeAnswersOkWithTheExistingSubscription()
    {
        var billing = new FakeBilling { Subscribe = new SubscribeResult(SubscribeOutcome.AlreadySubscribed, Pro) };

        var result = await new CreateSubscriptionEndpoint().HandleAsync(new CreateSubscriptionRequest { PlanHandle = "eshop-pro" }, User, billing);

        var response = AssertResult<CreateSubscriptionResponse>(result, StatusCodes.Status200OK);
        Assert.AreEqual(500, response.Subscription!.Id);
    }

    [TestMethod]
    public async Task UnconfirmedSubscribeAnswersAccepted()
    {
        var billing = new FakeBilling { Subscribe = new SubscribeResult(SubscribeOutcome.Unconfirmed, null) };

        var result = await new CreateSubscriptionEndpoint().HandleAsync(new CreateSubscriptionRequest { PlanHandle = "eshop-pro" }, User, billing);

        var response = AssertResult<CreateSubscriptionResponse>(result, StatusCodes.Status202Accepted);
        Assert.AreEqual("Unconfirmed", response.Outcome);
        Assert.IsNull(response.Subscription);
    }

    [TestMethod]
    public async Task MySubscriptionsListsWhatTheBillingProviderHolds()
    {
        var billing = new FakeBilling { Mine = new CustomerSubscriptions(new[] { Pro }, HasUnconfirmedEnrollment: false) };

        var result = await new MySubscriptionsListEndpoint().HandleAsync(User, billing);

        var response = AssertResult<ListMySubscriptionsResponse>(result, StatusCodes.Status200OK);
        Assert.AreEqual(500, response.Subscriptions.Single().Id);
        Assert.AreEqual("Pro Plan", response.Subscriptions.Single().PlanName);
    }

    [DataTestMethod]
    [DataRow(SubscriptionBillingErrorKind.PlanNotOffered, StatusCodes.Status400BadRequest)]
    [DataRow(SubscriptionBillingErrorKind.Rejected, StatusCodes.Status422UnprocessableEntity)]
    [DataRow(SubscriptionBillingErrorKind.AlreadySubscribed, StatusCodes.Status409Conflict)]
    [DataRow(SubscriptionBillingErrorKind.InProgress, StatusCodes.Status409Conflict)]
    [DataRow(SubscriptionBillingErrorKind.Unavailable, StatusCodes.Status503ServiceUnavailable)]
    [DataRow(SubscriptionBillingErrorKind.ProviderError, StatusCodes.Status502BadGateway)]
    public async Task BillingFailuresMapToDistinctStatuses(SubscriptionBillingErrorKind kind, int expectedStatus)
    {
        var middleware = new ExceptionMiddleware(_ => throw new SubscriptionBillingException(kind, "caller-safe message",
            new[] { "reason" }, new InvalidOperationException("provider internals")));
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();

        await middleware.InvokeAsync(context);

        Assert.AreEqual(expectedStatus, context.Response.StatusCode);
        context.Response.Body.Position = 0;
        var body = await new StreamReader(context.Response.Body).ReadToEndAsync();
        using var json = JsonDocument.Parse(body);
        Assert.AreEqual("caller-safe message", json.RootElement.GetProperty("Message").GetString());
        Assert.AreEqual("reason", json.RootElement.GetProperty("Details")[0].GetString());
        Assert.IsFalse(body.Contains("provider internals"));
    }

    private static T AssertResult<T>(IResult result, int expectedStatus)
    {
        Assert.AreEqual(expectedStatus, ((IStatusCodeHttpResult)result).StatusCode);
        return (T)((IValueHttpResult)result).Value!;
    }

    private sealed class FakeBilling : ISubscriptionBillingService
    {
        public SubscriptionPlanCatalog Catalog { get; init; } = new(Array.Empty<SubscriptionPlan>(), false);
        public SubscribeResult Subscribe { get; init; } = new(SubscribeOutcome.Unconfirmed, null);
        public CustomerSubscriptions Mine { get; init; } = new(Array.Empty<SubscriptionDetails>(), false);
        public Subscriber? LastSubscriber { get; private set; }
        public string? LastPlanHandle { get; private set; }

        public Task<SubscriptionPlanCatalog> ListPlansAsync(CancellationToken cancellationToken = default) => Task.FromResult(Catalog);

        public Task<SubscribeResult> SubscribeAsync(Subscriber subscriber, string planHandle, CancellationToken cancellationToken = default)
        {
            LastSubscriber = subscriber;
            LastPlanHandle = planHandle;
            return Task.FromResult(Subscribe);
        }

        public Task<CustomerSubscriptions> GetSubscriptionsAsync(Subscriber subscriber, CancellationToken cancellationToken = default) =>
            Task.FromResult(Mine);
    }
}
