#pragma warning disable CS8602 // Dereference of a possibly null reference (results are asserted)
using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Claims;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using BlazorShared.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.eShopWeb.PublicApi.Services.Maxio;
using Microsoft.eShopWeb.PublicApi.Services.Subscriptions;
using Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;
using Microsoft.eShopWeb.PublicApi.SubscriptionPlanEndpoints;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PublicApiIntegrationTests.SubscriptionEndpoints;

[TestClass]
public class CreateSubscriptionEndpointTests
{
    private static ClaimsPrincipal User(string? name = "demouser@microsoft.com")
    {
        if (name is null)
        {
            return new ClaimsPrincipal(new ClaimsIdentity());
        }

        return new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.Name, name)
        }));
    }

    private static CreateSubscriptionRequest ValidRequest()
        => new CreateSubscriptionRequest
        {
            PlanHandle = "eshop-pro",
            FirstName = "Demo",
            LastName = "Shopper"
        };

    private static CreateSubscriptionEndpoint NewEndpoint() => new CreateSubscriptionEndpoint();

    private static async Task<(int StatusCode, string Body)> ExecuteAsync(IResult result)
    {
        var httpContext = new DefaultHttpContext();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(Options.Create(new JsonOptions()));
        httpContext.RequestServices = services.BuildServiceProvider();
        httpContext.Response.Body = new MemoryStream();
        await result.ExecuteAsync(httpContext);
        httpContext.Response.Body.Position = 0;
        using var reader = new StreamReader(httpContext.Response.Body);
        var body = await reader.ReadToEndAsync();
        return (httpContext.Response.StatusCode, body);
    }

    private static SubscribeResult NewSubscription(bool alreadySubscribed)
        => new SubscribeResult(
            new CurrentSubscription(123L, "active", "eshop-pro", "Pro Plan", 29900L,
                new DateTimeOffset(2026, 10, 8, 0, 0, 0, TimeSpan.Zero),
                new DateTimeOffset(2026, 9, 8, 0, 0, 0, TimeSpan.Zero)),
            alreadySubscribed);

    [TestMethod]
    public async Task Returns400WhenPlanHandleMissing()
    {
        var endpoint = NewEndpoint();
        var request = new CreateSubscriptionRequest { FirstName = "Demo", LastName = "Shopper" };

        var result = await endpoint.HandleAsync(request, User(), new FakeSubscriptionService());

        Assert.AreEqual(StatusCodes.Status400BadRequest, ((IStatusCodeHttpResult)result).StatusCode);
    }

    [TestMethod]
    public async Task Returns400WhenCustomerNamesMissing()
    {
        var endpoint = NewEndpoint();
        var request = new CreateSubscriptionRequest { PlanHandle = "eshop-pro" };

        var result = await endpoint.HandleAsync(request, User(), new FakeSubscriptionService());

        Assert.AreEqual(StatusCodes.Status400BadRequest, ((IStatusCodeHttpResult)result).StatusCode);
    }

    [TestMethod]
    public async Task Returns401WhenTokenHasNoUserName()
    {
        var endpoint = NewEndpoint();

        var result = await endpoint.HandleAsync(ValidRequest(), User(null), new FakeSubscriptionService());

        Assert.AreEqual(StatusCodes.Status401Unauthorized, ((IStatusCodeHttpResult)result).StatusCode);
    }

    [TestMethod]
    public async Task Returns201WithSubscriptionWhenCreated()
    {
        var endpoint = NewEndpoint();
        var service = new FakeSubscriptionService(NewSubscription(alreadySubscribed: false));

        var result = await endpoint.HandleAsync(ValidRequest(), User(), service);

        var created = HttpResultAssert.As<Created<CreateSubscriptionResponse>>(result, StatusCodes.Status201Created);
        Assert.IsFalse(created.Value.AlreadySubscribed);
        Assert.AreEqual(123L, created.Value.Subscription.Id);
        Assert.AreEqual("eshop-pro", created.Value.Subscription.PlanHandle);
        Assert.AreEqual("Pro Plan", created.Value.Subscription.PlanName);
        Assert.AreEqual(29900L, created.Value.Subscription.PriceInCents);
    }

    [TestMethod]
    public async Task Returns200WhenAlreadySubscribed()
    {
        var endpoint = NewEndpoint();
        var service = new FakeSubscriptionService(NewSubscription(alreadySubscribed: true));

        var result = await endpoint.HandleAsync(ValidRequest(), User(), service);

        var ok = HttpResultAssert.As<Ok<CreateSubscriptionResponse>>(result, StatusCodes.Status200OK);
        Assert.IsTrue(ok.Value.AlreadySubscribed);
        Assert.AreEqual(123L, ok.Value.Subscription.Id);
    }

    [TestMethod]
    public async Task Returns400WhenPlanUnknown()
    {
        var endpoint = NewEndpoint();
        var service = new FakeSubscriptionService();
        service.ThrowOnSubscribe = new UnknownPlanException("missing-plan");

        var result = await endpoint.HandleAsync(ValidRequest(), User(), service);

        var (statusCode, body) = await ExecuteAsync(result);
        Assert.AreEqual(StatusCodes.Status400BadRequest, statusCode);
        var error = JsonSerializer.Deserialize<ErrorDetails>(body, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        StringAssert.Contains(error.Message, "missing-plan");
    }

    [TestMethod]
    public async Task Returns503WhenMaxioNotConfigured()
    {
        var endpoint = NewEndpoint();
        var service = new FakeSubscriptionService();
        service.ThrowOnSubscribe = new MaxioNotConfiguredException("Maxio not configured");

        var result = await endpoint.HandleAsync(ValidRequest(), User(), service);

        Assert.AreEqual(StatusCodes.Status503ServiceUnavailable, ((IStatusCodeHttpResult)result).StatusCode);
    }
}

[TestClass]
public class ListSubscriptionPlansEndpointTests
{
    [TestMethod]
    public async Task ReturnsPlansMappedFromService()
    {
        var endpoint = new ListSubscriptionPlansEndpoint();
        var service = new FakeSubscriptionService(new List<SubscriptionPlan>
        {
            new SubscriptionPlan(1L, "eshop-pro", "Pro Plan", null, 29900L, 299m, 1, "month"),
            new SubscriptionPlan(2L, "basic-plan", "Basic Plan", null, 2900L, 29m, 1, "month")
        });

        var result = await endpoint.HandleAsync(User(), service);

        var ok = HttpResultAssert.As<Ok<ListSubscriptionPlansResponse>>(result, StatusCodes.Status200OK);
        Assert.AreEqual(2, ok.Value.Plans.Count);
        Assert.AreEqual("eshop-pro", ok.Value.Plans[0].Handle);
        Assert.AreEqual(29900L, ok.Value.Plans[0].PriceInCents);
        Assert.AreEqual(299m, ok.Value.Plans[0].Price);
    }

    private static ClaimsPrincipal User()
    {
        return new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.Name, "demouser@microsoft.com") }));
    }
}

[TestClass]
public class ListMySubscriptionsEndpointTests
{
    [TestMethod]
    public async Task ReturnsSubscriptionsMappedFromService()
    {
        var endpoint = new ListMySubscriptionsEndpoint();
        var service = new FakeSubscriptionService(new List<CurrentSubscription>
        {
            new CurrentSubscription(123L, "active", "eshop-pro", "Pro Plan", 29900L,
                new DateTimeOffset(2026, 10, 8, 0, 0, 0, TimeSpan.Zero), null)
        });

        var result = await endpoint.HandleAsync(User(), service);

        var ok = HttpResultAssert.As<Ok<ListMySubscriptionsResponse>>(result, StatusCodes.Status200OK);
        Assert.AreEqual(1, ok.Value.Subscriptions.Count);
        Assert.AreEqual(123L, ok.Value.Subscriptions[0].Id);
        Assert.AreEqual("active", ok.Value.Subscriptions[0].State);
        Assert.AreEqual("eshop-pro", ok.Value.Subscriptions[0].PlanHandle);
    }

    [TestMethod]
    public async Task ReturnsEmptyWhenUserHasNoSubscriptions()
    {
        var endpoint = new ListMySubscriptionsEndpoint();

        var result = await endpoint.HandleAsync(User(), new FakeSubscriptionService());

        var ok = HttpResultAssert.As<Ok<ListMySubscriptionsResponse>>(result, StatusCodes.Status200OK);
        Assert.AreEqual(0, ok.Value.Subscriptions.Count);
    }

    private static ClaimsPrincipal User()
    {
        return new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.Name, "demouser@microsoft.com") }));
    }
}

internal static class HttpResultAssert
{
    public static T As<T>(IResult result, int expectedStatusCode) where T : class, IStatusCodeHttpResult
    {
        Assert.AreEqual(expectedStatusCode, result is IStatusCodeHttpResult statusResult
            ? statusResult.StatusCode
            : 0);
        return result is T typed
            ? typed
            : throw new AssertFailedException($"Expected result of type {typeof(T).Name} but was {result.GetType().Name}.");
    }
}

internal sealed class FakeSubscriptionService : ISubscriptionService
{
    private readonly IReadOnlyList<SubscriptionPlan>? _plans;
    private readonly IReadOnlyList<CurrentSubscription>? _subscriptions;
    private readonly SubscribeResult? _subscribeResult;

    public FakeSubscriptionService()
    {
    }

    public FakeSubscriptionService(IReadOnlyList<SubscriptionPlan> plans)
    {
        _plans = plans;
    }

    public FakeSubscriptionService(IReadOnlyList<CurrentSubscription> subscriptions)
    {
        _subscriptions = subscriptions;
    }

    public FakeSubscriptionService(SubscribeResult subscribeResult)
    {
        _subscribeResult = subscribeResult;
    }

    public Exception? ThrowOnSubscribe { get; set; }

    public Task<IReadOnlyList<SubscriptionPlan>> ListAvailablePlansAsync(CancellationToken cancellationToken)
        => Task.FromResult(_plans ?? (IReadOnlyList<SubscriptionPlan>)new List<SubscriptionPlan>());

    public Task<SubscribeResult> SubscribeAsync(string userName, string planHandle, string firstName, string lastName, string? email, CancellationToken cancellationToken)
    {
        if (ThrowOnSubscribe is not null)
        {
            throw ThrowOnSubscribe;
        }

        return Task.FromResult(_subscribeResult ?? new SubscribeResult(
            new CurrentSubscription(1L, "active", planHandle, "Pro Plan", 29900L, null, null), false));
    }

    public Task<IReadOnlyList<CurrentSubscription>> ListSubscriptionsForUserAsync(string userName, CancellationToken cancellationToken)
        => Task.FromResult(_subscriptions ?? (IReadOnlyList<CurrentSubscription>)new List<CurrentSubscription>());
}
