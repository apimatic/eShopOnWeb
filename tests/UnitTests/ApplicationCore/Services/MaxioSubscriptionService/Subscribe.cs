using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Maxio;
using Microsoft.Extensions.Options;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Services.MaxioSubscriptionServiceTests;

public class Subscribe
{
    private const string UserId = "11111111-1111-1111-1111-111111111111";
    private const string UserName = "demouser@microsoft.com";
    private const string Email = "demouser@microsoft.com";

    private readonly IMaxioClient _mockMaxio = Substitute.For<IMaxioClient>();
    private readonly IRepository<SubscriptionRecord> _mockRecordRepo = Substitute.For<IRepository<SubscriptionRecord>>();
    private readonly IAppLogger<MaxioSubscriptionService> _mockLogger = Substitute.For<IAppLogger<MaxioSubscriptionService>>();

    private MaxioSubscriptionService CreateService()
    {
        var settings = new MaxioSettings
        {
            ApiKey = "test-key",
            Subdomain = "test-site",
            Environment = "US",
            ProductFamilyHandle = "test-family",
            DefaultPlanHandle = "eshop-pro"
        };
        return new MaxioSubscriptionService(_mockMaxio, _mockRecordRepo, Options.Create(settings), _mockLogger);
    }

    private MaxioSubscriptionService CreateServiceWithoutDefaultPlan()
    {
        var settings = new MaxioSettings
        {
            ApiKey = "test-key",
            Subdomain = "test-site",
            ProductFamilyHandle = "test-family"
        };
        return new MaxioSubscriptionService(_mockMaxio, _mockRecordRepo, Options.Create(settings), _mockLogger);
    }

    private static MaxioProduct Plan(string handle, string name, int priceCents)
    {
        return new MaxioProduct
        {
            Id = handle.GetHashCode(),
            Handle = handle,
            Name = name,
            PriceInCents = priceCents,
            Interval = 1,
            IntervalUnit = "month",
            ProductFamily = new MaxioProductFamily { Handle = "test-family" }
        };
    }

    private static MaxioSubscription Subscription(string planHandle, string state, long id = 42)
    {
        return new MaxioSubscription
        {
            Id = id,
            State = state,
            ProductPriceInCents = 29900,
            Currency = "USD",
            NextAssessmentAt = System.DateTime.UtcNow.AddDays(30),
            CreatedAt = System.DateTime.UtcNow,
            Product = Plan(planHandle, "Pro Plan", 29900),
            Customer = new MaxioCustomer { Id = 7, Reference = UserId }
        };
    }

    public Subscribe()
    {
        _mockRecordRepo.ListAsync(Arg.Any<Ardalis.Specification.ISpecification<SubscriptionRecord>>())
            .Returns(new List<SubscriptionRecord>());
        _mockRecordRepo.AddAsync(Arg.Any<SubscriptionRecord>(), default)
            .Returns(callInfo => callInfo.Arg<SubscriptionRecord>());
    }

    [Fact]
    public async Task NewUser_GetsCustomerAndSubscriptionCreated()
    {
        _mockMaxio.ListFamilyProductsAsync()
            .Returns(new List<MaxioProduct> { Plan("eshop-pro", "Pro Plan", 29900), Plan("basic-plan", "Basic Plan", 2900) });
        _mockMaxio.FindCustomerByReferenceAsync(UserId)
            .Returns((MaxioCustomer?)null);
        _mockMaxio.CreateCustomerAsync(Arg.Any<MaxioNewCustomer>())
            .Returns(new MaxioCustomer { Id = 7, Reference = UserId, Email = Email });
        _mockMaxio.ListCustomerSubscriptionsAsync(7)
            .Returns(new List<MaxioSubscription>());
        _mockMaxio.CreateSubscriptionAsync(7, "eshop-pro")
            .Returns(Subscription("eshop-pro", "active"));

        var result = await CreateService().SubscribeAsync(UserId, UserName, Email);

        Assert.False(result.AlreadySubscribed);
        Assert.Equal("eshop-pro", result.Subscription.PlanHandle);
        Assert.Equal("active", result.Subscription.State);
        Assert.Equal(299m, result.Subscription.Price);
        Assert.Equal("USD", result.Subscription.Currency);
        Assert.Equal(7, result.Subscription.MaxioCustomerId);
        Assert.NotNull(result.Subscription.NextBillingDate);
        await _mockMaxio.Received().CreateSubscriptionAsync(7, "eshop-pro");
        await _mockRecordRepo.Received().AddAsync(
            Arg.Is<SubscriptionRecord>(r => r.UserId == UserId && r.MaxioSubscriptionId == 42), default);
    }

    [Fact]
    public async Task ExistingLiveSubscription_ReturnsItWithoutCreatingADuplicate()
    {
        _mockMaxio.ListFamilyProductsAsync()
            .Returns(new List<MaxioProduct> { Plan("eshop-pro", "Pro Plan", 29900) });
        _mockMaxio.FindCustomerByReferenceAsync(UserId)
            .Returns(new MaxioCustomer { Id = 7, Reference = UserId, Email = Email });
        _mockMaxio.ListCustomerSubscriptionsAsync(7)
            .Returns(new List<MaxioSubscription> { Subscription("eshop-pro", "active") });

        var result = await CreateService().SubscribeAsync(UserId, UserName, Email, "eshop-pro");

        Assert.True(result.AlreadySubscribed);
        Assert.Equal(42, result.Subscription.MaxioSubscriptionId);
        await _mockMaxio.DidNotReceive().CreateSubscriptionAsync(Arg.Any<long>(), Arg.Any<string>());
    }

    [Fact]
    public async Task CanceledSubscription_AllowsResubscribing()
    {
        _mockMaxio.ListFamilyProductsAsync()
            .Returns(new List<MaxioProduct> { Plan("eshop-pro", "Pro Plan", 29900) });
        _mockMaxio.FindCustomerByReferenceAsync(UserId)
            .Returns(new MaxioCustomer { Id = 7, Reference = UserId, Email = Email });
        _mockMaxio.ListCustomerSubscriptionsAsync(7)
            .Returns(new List<MaxioSubscription> { Subscription("eshop-pro", "canceled") });
        _mockMaxio.CreateSubscriptionAsync(7, "eshop-pro")
            .Returns(Subscription("eshop-pro", "active", id: 43));

        var result = await CreateService().SubscribeAsync(UserId, UserName, Email, "eshop-pro");

        Assert.False(result.AlreadySubscribed);
        await _mockMaxio.Received().CreateSubscriptionAsync(7, "eshop-pro");
    }

    [Fact]
    public async Task CustomerCreationRace_RecoversExistingCustomer()
    {
        _mockMaxio.ListFamilyProductsAsync()
            .Returns(new List<MaxioProduct> { Plan("eshop-pro", "Pro Plan", 29900) });
        _mockMaxio.FindCustomerByReferenceAsync(UserId)
            .Returns((MaxioCustomer?)null, new MaxioCustomer { Id = 9, Reference = UserId });
        _mockMaxio.CreateCustomerAsync(Arg.Any<MaxioNewCustomer>())
            .Throws(new MaxioApiException(422, "{\"errors\":[\"Reference has already been taken\"]}"));
        _mockMaxio.ListCustomerSubscriptionsAsync(9)
            .Returns(new List<MaxioSubscription>());
        _mockMaxio.CreateSubscriptionAsync(9, "eshop-pro")
            .Returns(Subscription("eshop-pro", "active"));

        var result = await CreateService().SubscribeAsync(UserId, UserName, Email);

        Assert.False(result.AlreadySubscribed);
        Assert.Equal(9, result.Subscription.MaxioCustomerId);
    }

    [Fact]
    public async Task UnknownPlan_ThrowsPlanNotFound()
    {
        _mockMaxio.ListFamilyProductsAsync()
            .Returns(new List<MaxioProduct> { Plan("eshop-pro", "Pro Plan", 29900) });

        await Assert.ThrowsAsync<SubscriptionPlanNotFoundException>(
            () => CreateService().SubscribeAsync(UserId, UserName, Email, "does-not-exist"));
    }

    [Fact]
    public async Task NoDefaultPlanConfigured_UsesFirstAvailablePlan()
    {
        _mockMaxio.ListFamilyProductsAsync()
            .Returns(new List<MaxioProduct> { Plan("basic-plan", "Basic Plan", 2900), Plan("eshop-pro", "Pro Plan", 29900) });
        _mockMaxio.FindCustomerByReferenceAsync(UserId)
            .Returns(new MaxioCustomer { Id = 7, Reference = UserId });
        _mockMaxio.ListCustomerSubscriptionsAsync(7)
            .Returns(new List<MaxioSubscription>());
        _mockMaxio.CreateSubscriptionAsync(7, "basic-plan")
            .Returns(Subscription("basic-plan", "active", id: 44));

        var result = await CreateServiceWithoutDefaultPlan().SubscribeAsync(UserId, UserName, Email);

        Assert.Equal("basic-plan", result.Subscription.PlanHandle);
    }
}

public class ListPlans
{
    private readonly IMaxioClient _mockMaxio = Substitute.For<IMaxioClient>();
    private readonly IRepository<SubscriptionRecord> _mockRecordRepo = Substitute.For<IRepository<SubscriptionRecord>>();
    private readonly IAppLogger<MaxioSubscriptionService> _mockLogger = Substitute.For<IAppLogger<MaxioSubscriptionService>>();

    [Fact]
    public async Task ReturnsPlansMarkingTheConfiguredDefault()
    {
        var settings = new MaxioSettings
        {
            ApiKey = "test-key",
            Subdomain = "test-site",
            ProductFamilyHandle = "test-family",
            DefaultPlanHandle = "eshop-pro"
        };
        _mockMaxio.ListFamilyProductsAsync()
            .Returns(new List<MaxioProduct>
            {
                Plan("eshop-pro", "Pro Plan", 29900),
                Plan("basic-plan", "Basic Plan", 2900)
            });

        var plans = await new MaxioSubscriptionService(_mockMaxio, _mockRecordRepo, Options.Create(settings), _mockLogger).ListPlansAsync();

        Assert.Equal(2, plans.Count);
        Assert.True(plans.Single(p => p.Handle == "eshop-pro").IsDefault);
        Assert.False(plans.Single(p => p.Handle == "basic-plan").IsDefault);
        Assert.Equal(299m, plans.Single(p => p.Handle == "eshop-pro").Price);
    }

    private static MaxioProduct Plan(string handle, string name, int priceCents)
    {
        return new MaxioProduct
        {
            Id = handle.GetHashCode(),
            Handle = handle,
            Name = name,
            PriceInCents = priceCents,
            Interval = 1,
            IntervalUnit = "month"
        };
    }
}

public class ListUserSubscriptions
{
    private const string UserId = "11111111-1111-1111-1111-111111111111";

    [Fact]
    public async Task UnknownCustomer_ReturnsEmptyList()
    {
        var mockMaxio = Substitute.For<IMaxioClient>();
        mockMaxio.FindCustomerByReferenceAsync(UserId)
            .Returns((MaxioCustomer?)null);

        var settings = new MaxioSettings { ProductFamilyHandle = "test-family" };
        var subscriptions = await new MaxioSubscriptionService(
            mockMaxio,
            Substitute.For<IRepository<SubscriptionRecord>>(),
            Options.Create(settings),
            Substitute.For<IAppLogger<MaxioSubscriptionService>>())
            .ListUserSubscriptionsAsync(UserId);

        Assert.Empty(subscriptions);
    }
}
