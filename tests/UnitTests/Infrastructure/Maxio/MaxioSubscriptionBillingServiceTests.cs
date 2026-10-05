using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Maxio;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Maxio;

public class MaxioSubscriptionBillingServiceTests
{
    private static readonly string UserId = "11111111-1111-1111-1111-111111111111";
    private const string UserKey = "demouser@microsoft.com";

    private static MaxioProduct ProPlan() => new()
    {
        Id = 7130997,
        Handle = "eshop-pro",
        Name = "Pro Plan",
        PriceInCents = 29900,
        Interval = 1,
        IntervalUnit = "month",
        ProductFamily = new MaxioProductFamily { Id = 3023074, Handle = "eshop-subscribe" }
    };

    private static MaxioCustomer Customer() => new()
    {
        Id = 500,
        FirstName = "demouser",
        LastName = "Customer",
        Email = UserKey,
        Reference = UserKey
    };

    private static MaxioSubscription ActiveSubscription(string reference, string handle = "eshop-pro") => new()
    {
        Id = 9001,
        State = "active",
        ProductPriceInCents = 29900,
        Reference = reference,
        CurrentPeriodEndsAt = DateTimeOffset.UtcNow.AddDays(20),
        ActivatedAt = DateTimeOffset.UtcNow,
        CreatedAt = DateTimeOffset.UtcNow,
        Customer = Customer(),
        Product = new MaxioProduct
        {
            Id = 7130997,
            Handle = handle,
            Name = "Pro Plan",
            PriceInCents = 29900,
            ProductFamily = new MaxioProductFamily { Handle = "eshop-subscribe" }
        }
    };

    private static (MaxioSubscriptionBillingService Service, IMaxioApiClient Api, IRepository<SubscriptionLink> Repo)
        CreateService(IMaxioApiClient? api = null)
    {
        api ??= Substitute.For<IMaxioApiClient>();
        var repo = Substitute.For<IRepository<SubscriptionLink>>();
        repo.ListAsync(Arg.Any<Ardalis.Specification.ISpecification<SubscriptionLink>>(), Arg.Any<CancellationToken>())
            .Returns(new List<SubscriptionLink>());
        repo.AddAsync(Arg.Any<SubscriptionLink>(), Arg.Any<CancellationToken>())
            .Returns(callInfo => callInfo.Arg<SubscriptionLink>());

        var options = Options.Create(new MaxioOptions
        {
            ApiKey = "key",
            Subdomain = "cp-exp-3",
            Environment = "US",
            ProductFamilyHandle = "eshop-subscribe"
        });
        var logger = Substitute.For<IAppLogger<MaxioSubscriptionBillingService>>();
        var service = new MaxioSubscriptionBillingService(api, repo, options, logger);
        return (service, api, repo);
    }

    [Fact]
public void BuildSubscriptionReference_IsDeterministicAndLowercase()
    {
        var a = MaxioSubscriptionBillingService.BuildSubscriptionReference(UserKey, "eshop-pro");
        var b = MaxioSubscriptionBillingService.BuildSubscriptionReference(UserKey.ToUpperInvariant(), "eshop-pro");

        Assert.Equal($"eshop-{UserKey}-eshop-pro", a);
        Assert.Equal(a, b);
    }

    [Fact]
    public async Task SubscribeAsync_CreatesCustomerAndSubscription()
    {
        var api = Substitute.For<IMaxioApiClient>();
        api.GetProductByHandleAsync("eshop-pro", Arg.Any<CancellationToken>()).Returns(ProPlan());
        api.GetCustomerByReferenceAsync(UserKey, Arg.Any<CancellationToken>()).Returns((MaxioCustomer?)null);
        api.CreateCustomerAsync(Arg.Any<CreateMaxioCustomerRequest>(), Arg.Any<CancellationToken>()).Returns(Customer());
        api.FindSubscriptionByReferenceAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns((MaxioSubscription?)null);
        api.CreateSubscriptionAsync(Arg.Any<CreateMaxioSubscriptionRequest>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                var request = callInfo.Arg<CreateMaxioSubscriptionRequest>();
                var subscription = ActiveSubscription(request.Subscription.Reference);
                subscription.Customer = Customer();
                return subscription;
            });
        var (service, _, repo) = CreateService(api);

        var enrollment = await service.SubscribeAsync(UserId, UserKey, UserKey, "eshop-pro");

        Assert.Equal(9001, enrollment.SubscriptionId);
        Assert.Equal("active", enrollment.State);
        Assert.Equal(29900, enrollment.PriceInCents);
        Assert.False(enrollment.AlreadySubscribed);
        Assert.Equal(Customer().Id.ToString(), enrollment.CustomerId);
        Assert.NotNull(enrollment.NextBillingDate);

await api.Received(1).CreateCustomerAsync(Arg.Is<CreateMaxioCustomerRequest>(c =>
            c.FirstName == "demouser" && c.Email == UserKey && c.Reference == UserKey));
        await api.Received(1).CreateSubscriptionAsync(Arg.Is<CreateMaxioSubscriptionRequest>(r =>
            r.Subscription.ProductHandle == "eshop-pro" &&
            r.Subscription.CustomerId == 500 &&
            r.Subscription.Reference == MaxioSubscriptionBillingService.BuildSubscriptionReference(UserKey, "eshop-pro")));
        await repo.Received(1).AddAsync(Arg.Any<SubscriptionLink>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SubscribeAsync_IsIdempotentForSameUserAndPlan()
    {
        var api = Substitute.For<IMaxioApiClient>();
        api.GetProductByHandleAsync("eshop-pro", Arg.Any<CancellationToken>()).Returns(ProPlan());
        api.GetCustomerByReferenceAsync(UserKey, Arg.Any<CancellationToken>()).Returns(Customer());
        var existingReference = MaxioSubscriptionBillingService.BuildSubscriptionReference(UserKey, "eshop-pro");
        api.FindSubscriptionByReferenceAsync(existingReference, Arg.Any<CancellationToken>())
            .Returns(ActiveSubscription(existingReference));
        var (service, _, _) = CreateService(api);

        var first = await service.SubscribeAsync(UserId, UserKey, UserKey, "eshop-pro");
        var second = await service.SubscribeAsync(UserId, UserKey, UserKey, "eshop-pro");

        Assert.True(first.AlreadySubscribed);
        Assert.True(second.AlreadySubscribed);
        Assert.Equal(first.SubscriptionId, second.SubscriptionId);
        await api.DidNotReceive().CreateSubscriptionAsync(Arg.Any<CreateMaxioSubscriptionRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SubscribeAsync_UnknownPlanThrows()
    {
        var api = Substitute.For<IMaxioApiClient>();
        api.GetProductByHandleAsync("nope", Arg.Any<CancellationToken>()).Returns((MaxioProduct?)null);
        var (service, _, _) = CreateService(api);

        await Assert.ThrowsAsync<UnknownSubscriptionPlanException>(
            () => service.SubscribeAsync(UserId, UserKey, UserKey, "nope"));
    }

    [Fact]
    public async Task SubscribeAsync_RejectsPlanOutsideConfiguredFamily()
    {
        var api = Substitute.For<IMaxioApiClient>();
        var roguePlan = ProPlan();
        roguePlan.ProductFamily!.Handle = "other-family";
        api.GetProductByHandleAsync("rogue-plan", Arg.Any<CancellationToken>()).Returns(roguePlan);
        var (service, _, _) = CreateService(api);

        await Assert.ThrowsAsync<UnknownSubscriptionPlanException>(
            () => service.SubscribeAsync(UserId, UserKey, UserKey, "rogue-plan"));
    }

    [Fact]
    public async Task SubscribeAsync_AfterCancellation_CreatesNewSubscriptionWithSuffixedReference()
    {
        var api = Substitute.For<IMaxioApiClient>();
        api.GetProductByHandleAsync("eshop-pro", Arg.Any<CancellationToken>()).Returns(ProPlan());
        api.GetCustomerByReferenceAsync(UserKey, Arg.Any<CancellationToken>()).Returns(Customer());
        var baseReference = MaxioSubscriptionBillingService.BuildSubscriptionReference(UserKey, "eshop-pro");
        var canceled = ActiveSubscription(baseReference);
        canceled.State = "canceled";
        api.FindSubscriptionByReferenceAsync(baseReference, Arg.Any<CancellationToken>()).Returns(canceled);
        api.FindSubscriptionByReferenceAsync($"{baseReference}-2", Arg.Any<CancellationToken>())
            .Returns((MaxioSubscription?)null);
        CreateMaxioSubscriptionRequest? createdRequest = null;
        api.CreateSubscriptionAsync(Arg.Any<CreateMaxioSubscriptionRequest>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                createdRequest = callInfo.Arg<CreateMaxioSubscriptionRequest>();
                var subscription = ActiveSubscription(createdRequest!.Subscription.Reference);
                subscription.Customer = Customer();
                return subscription;
            });
        var (service, _, _) = CreateService(api);

        var enrollment = await service.SubscribeAsync(UserId, UserKey, UserKey, "eshop-pro");

        Assert.False(enrollment.AlreadySubscribed);
        Assert.Equal($"{baseReference}-2", createdRequest!.Subscription.Reference);
    }

    [Fact]
    public async Task GetSubscriptionsForUserAsync_ReturnsEmpty_WhenNoBillingCustomer()
    {
        var api = Substitute.For<IMaxioApiClient>();
        api.GetCustomerByReferenceAsync(UserKey, Arg.Any<CancellationToken>()).Returns((MaxioCustomer?)null);
        var (service, _, _) = CreateService(api);

        var result = await service.GetSubscriptionsForUserAsync(UserId, UserKey, UserKey);

        Assert.Empty(result);
        await api.DidNotReceive().ListCustomerSubscriptionsAsync(Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetSubscriptionsForUserAsync_MapsCustomerSubscriptions()
    {
        var api = Substitute.For<IMaxioApiClient>();
        api.GetCustomerByReferenceAsync(UserKey, Arg.Any<CancellationToken>()).Returns(Customer());
        api.ListCustomerSubscriptionsAsync(500, Arg.Any<CancellationToken>())
            .Returns(new List<MaxioSubscription>
            {
                ActiveSubscription("ref-1", "eshop-pro"),
                ActiveSubscription("ref-2", "basic-plan")
            });
        var (service, _, _) = CreateService(api);

        var result = await service.GetSubscriptionsForUserAsync(UserId, UserKey, UserKey);

        Assert.Equal(2, result.Count);
        Assert.Equal("eshop-pro", result[0].PlanHandle);
        Assert.Equal("basic-plan", result[1].PlanHandle);
        Assert.Equal("active", result[0].State);
    }

    [Fact]
    public async Task ListPlansAsync_FiltersToConfiguredFamilyAndExcludesArchived()
    {
        var api = Substitute.For<IMaxioApiClient>();
        var archivedPlan = ProPlan();
        archivedPlan.Handle = "old-plan";
        archivedPlan.ArchivedAt = DateTimeOffset.UtcNow;
        var foreignPlan = ProPlan();
        foreignPlan.Handle = "foreign-plan";
        foreignPlan.ProductFamily!.Handle = "other-family";
        api.ListProductsAsync(Arg.Any<CancellationToken>())
            .Returns(new List<MaxioProduct> { ProPlan(), archivedPlan, foreignPlan });
        var (service, _, _) = CreateService(api);

        var plans = await service.ListPlansAsync();

        var handles = plans.Select(p => p.Handle).ToList();
        Assert.Contains("eshop-pro", handles);
        Assert.DoesNotContain("old-plan", handles);
        Assert.DoesNotContain("foreign-plan", handles);
        Assert.Single(plans);
    }
}