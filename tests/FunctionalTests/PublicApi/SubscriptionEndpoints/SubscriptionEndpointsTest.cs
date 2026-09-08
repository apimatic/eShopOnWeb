using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.SubscriptionBilling;
using Microsoft.eShopWeb.FunctionalTests.Web.Api;
using Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;
using NSubstitute;
using Xunit;

namespace Microsoft.eShopWeb.FunctionalTests.PublicApi.SubscriptionEndpoints;

[Collection("Sequential")]
public class SubscriptionEndpointsTest : IClassFixture<SubscriptionTestApplication>
{
    private readonly SubscriptionTestApplication _factory;
    private readonly HttpClient _client;

    public SubscriptionEndpointsTest(SubscriptionTestApplication factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task ListPlansReturnsMappedPlansForAuthenticatedUser()
    {
        var plans = new List<SubscriptionPlan>
        {
            new SubscriptionPlan
            {
                Id = 7130995,
                Handle = "eshop-pro",
                Name = "Pro Plan",
                PriceInCents = 29900,
                Currency = "USD",
                Interval = 1,
                IntervalUnit = "month"
            }
        };
        _factory.BillingService.ListPlansAsync(Arg.Any<CancellationToken>()).Returns(plans);

        var response = await SendAuthedGet("/api/subscription-plans");
        response.EnsureSuccessStatusCode();

        var model = (await response.Content.ReadAsStringAsync()).FromJson<ListSubscriptionPlansResponse>();
        var plan = Assert.Single(model!.SubscriptionPlans);
        Assert.Equal("eshop-pro", plan.Handle);
        Assert.Equal("Pro Plan", plan.Name);
        Assert.Equal(29900, plan.PriceInCents);
        Assert.Equal("USD", plan.Currency);
    }

    [Fact]
    public async Task ListPlansRequiresAuthentication()
    {
        var response = await _client.GetAsync("/api/subscription-plans");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task SubscribeReturns201ForNewSubscription()
    {
        var purchase = NewPurchase(alreadySubscribed: false);
        _factory.BillingService.SubscribeAsync(Arg.Any<SubscriberProfile>(), "eshop-pro", Arg.Any<CancellationToken>())
            .Returns(purchase);

        var response = await PostSubscribe("eshop-pro");
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var model = (await response.Content.ReadAsStringAsync()).FromJson<SubscribeResponse>();
        Assert.Equal(94251975, model!.Subscription.Id);
        Assert.Equal("eshop-pro", model.Subscription.PlanHandle);
        Assert.Equal("active", model.Subscription.State);
        Assert.NotNull(model.Subscription.NextBillingAt);
    }

    [Fact]
    public async Task SubscribeReturns200WithExistingSubscriptionOnRepeat()
    {
        var purchase = NewPurchase(alreadySubscribed: true);
        _factory.BillingService.SubscribeAsync(Arg.Any<SubscriberProfile>(), "eshop-pro", Arg.Any<CancellationToken>())
            .Returns(purchase);

        var response = await PostSubscribe("eshop-pro");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task SubscribeToUnknownPlanReturns404()
    {
        _factory.BillingService
            .SubscribeAsync(Arg.Any<SubscriberProfile>(), "no-such-plan", Arg.Any<CancellationToken>())
            .Returns(Task.FromException<SubscriptionPurchase>(new SubscriptionPlanNotFoundException("no-such-plan")));

        var response = await PostSubscribe("no-such-plan");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task MySubscriptionsReturnsUsersSubscriptions()
    {
        var purchase = NewPurchase(alreadySubscribed: true);
        _factory.BillingService.ListSubscriptionsAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new List<SubscriptionPurchase> { purchase });

        var response = await SendAuthedGet("/api/my-subscriptions");
        response.EnsureSuccessStatusCode();

        var model = (await response.Content.ReadAsStringAsync()).FromJson<MySubscriptionsResponse>();
        var subscription = Assert.Single(model!.Subscriptions);
        Assert.Equal("eshop-pro", subscription.PlanHandle);
        Assert.Equal("Pro Plan", subscription.PlanName);
        Assert.Equal("active", subscription.State);
    }

    [Fact]
    public async Task MySubscriptionsRequiresAuthentication()
    {
        var response = await _client.GetAsync("/api/my-subscriptions");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private async Task<HttpResponseMessage> SendAuthedGet(string path)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ApiTokenHelper.GetNormalUserToken());
        return await _client.SendAsync(request);
    }

    private async Task<HttpResponseMessage> PostSubscribe(string planHandle)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/subscriptions")
        {
            Content = new StringContent($"{{\"planHandle\":\"{planHandle}\"}}", Encoding.UTF8, "application/json")
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ApiTokenHelper.GetNormalUserToken());
        return await _client.SendAsync(request);
    }

    private static SubscriptionPurchase NewPurchase(bool alreadySubscribed)
    {
        return new SubscriptionPurchase
        {
            SubscriptionId = 94251975,
            State = "active",
            PlanHandle = "eshop-pro",
            PlanName = "Pro Plan",
            ProductPriceInCents = 29900,
            Currency = "USD",
            CurrentPeriodEndsAt = DateTimeOffset.Parse("2026-10-09T04:06:50+05:00"),
            NextBillingAt = DateTimeOffset.Parse("2026-10-09T04:06:50+05:00"),
            ActivatedAt = DateTimeOffset.Parse("2026-09-09T04:06:51+05:00"),
            CreatedAt = DateTimeOffset.Parse("2026-09-09T04:06:50+05:00"),
            BalanceInCents = 29900,
            AlreadySubscribed = alreadySubscribed
        };
    }
}
