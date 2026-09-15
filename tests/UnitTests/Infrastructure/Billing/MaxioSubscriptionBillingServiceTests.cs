using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Billing;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.Infrastructure.Billing;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Billing;

public class MaxioSubscriptionBillingServiceTests
{
    private const string FamilyHandle = "eshop-subscribe";
    private readonly IMaxioAdvancedBillingClient _maxio = Substitute.For<IMaxioAdvancedBillingClient>();
    private readonly MaxioSubscriptionBillingService _service;

    public MaxioSubscriptionBillingServiceTests()
    {
        var options = Options.Create(new MaxioOptions
        {
            ApiKey = "test-key",
            Subdomain = "example",
            ProductFamilyHandle = FamilyHandle
        });

        _service = new MaxioSubscriptionBillingService(
            _maxio,
            new StaticOptionsMonitor(options.Value),
            NullLogger<MaxioSubscriptionBillingService>.Instance);
    }

    [Fact]
    public async Task ListPlansAsync_MapsCentsToDollarsAndSkipsArchived()
    {
        _maxio.ListProductsForProductFamilyAsync(FamilyHandle, Arg.Any<CancellationToken>()).Returns(new List<MaxioProductDto>
        {
            new()
            {
                Handle = "eshop-pro",
                Name = "Pro Plan",
                Description = "Pro",
                PriceInCents = 29900,
                Interval = 1,
                IntervalUnit = "month",
                ProductFamilyHandle = FamilyHandle
            },
            new()
            {
                Handle = "archived",
                Name = "Old",
                PriceInCents = 100,
                Interval = 1,
                IntervalUnit = "month",
                ProductFamilyHandle = FamilyHandle,
                IsArchived = true
            }
        });

        var plans = await _service.ListPlansAsync();

        var plan = Assert.Single(plans);
        Assert.Equal("eshop-pro", plan.Handle);
        Assert.Equal(299.00m, plan.Price);
        Assert.Equal("month", plan.IntervalUnit);
    }

    [Fact]
    public async Task SubscribeAsync_ReturnsExistingLiveSubscriptionWithoutCreatingAnother()
    {
        var customer = new BillingCustomer("user-1", "a@b.com", "Ann", "Shopper");
        SetupProduct("eshop-pro");
        _maxio.ReadCustomerByReferenceAsync(customer.Reference, Arg.Any<CancellationToken>())
            .Returns(new MaxioCustomerDto { Id = 42, Reference = customer.Reference });
        _maxio.FindSubscriptionByReferenceAsync($"{customer.Reference}:eshop-pro", Arg.Any<CancellationToken>())
            .Returns(new MaxioSubscriptionDto
            {
                Id = 99,
                State = "active",
                ProductHandle = "eshop-pro",
                ProductName = "Pro Plan",
                ProductPriceInCents = 29900
            });

        var result = await _service.SubscribeAsync(customer, "eshop-pro");

        Assert.Equal(99, result.Id);
        Assert.Equal("active", result.State);
        Assert.Equal(299.00m, result.Price);
        await _maxio.DidNotReceive().CreateSubscriptionAsync(Arg.Any<MaxioCreateSubscriptionDto>(), Arg.Any<CancellationToken>());
        await _maxio.DidNotReceive().CreateCustomerAsync(Arg.Any<MaxioCreateCustomerDto>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SubscribeAsync_CreatesCustomerAndSubscriptionWhenNoneExist()
    {
        var customer = new BillingCustomer("user-2", "c@d.com", "Cal", "Shopper");
        SetupProduct("basic-plan");
        _maxio.ReadCustomerByReferenceAsync(customer.Reference, Arg.Any<CancellationToken>()).Returns((MaxioCustomerDto?)null);
        _maxio.CreateCustomerAsync(Arg.Any<MaxioCreateCustomerDto>(), Arg.Any<CancellationToken>())
            .Returns(new MaxioCustomerDto { Id = 7, Reference = customer.Reference });
        _maxio.FindSubscriptionByReferenceAsync($"{customer.Reference}:basic-plan", Arg.Any<CancellationToken>())
            .Returns((MaxioSubscriptionDto?)null);
        _maxio.ListCustomerSubscriptionsAsync(7, Arg.Any<CancellationToken>()).Returns(new List<MaxioSubscriptionDto>());
        _maxio.CreateSubscriptionAsync(Arg.Any<MaxioCreateSubscriptionDto>(), Arg.Any<CancellationToken>())
            .Returns(new MaxioSubscriptionDto
            {
                Id = 15,
                State = "active",
                ProductHandle = "basic-plan",
                ProductName = "Basic Plan",
                ProductPriceInCents = 2900
            });

        var result = await _service.SubscribeAsync(customer, "basic-plan");

        Assert.Equal(15, result.Id);
        Assert.Equal("basic-plan", result.ProductHandle);
        await _maxio.Received(1).CreateCustomerAsync(
            Arg.Is<MaxioCreateCustomerDto>(dto => dto.Reference == customer.Reference && dto.Email == customer.Email),
            Arg.Any<CancellationToken>());
        await _maxio.Received(1).CreateSubscriptionAsync(
            Arg.Is<MaxioCreateSubscriptionDto>(dto =>
                dto.ProductHandle == "basic-plan"
                && dto.CustomerId == 7
                && dto.Reference == $"{customer.Reference}:basic-plan"
                && dto.PaymentCollectionMethod == "remittance"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SubscribeAsync_RejectsPlanOutsideConfiguredFamily()
    {
        var customer = new BillingCustomer("user-3", "e@f.com", "Ed", "Shopper");
        _maxio.ReadProductByHandleAsync("other-plan", Arg.Any<CancellationToken>()).Returns(new MaxioProductDto
        {
            Handle = "other-plan",
            Name = "Other",
            ProductFamilyHandle = "someone-else",
            PriceInCents = 1000,
            Interval = 1,
            IntervalUnit = "month"
        });

        await Assert.ThrowsAsync<SubscriptionPlanNotFoundException>(
            () => _service.SubscribeAsync(customer, "other-plan"));
    }

    [Fact]
    public async Task ListSubscriptionsAsync_ReturnsEmptyWhenCustomerDoesNotExist()
    {
        _maxio.ReadCustomerByReferenceAsync("missing", Arg.Any<CancellationToken>()).Returns((MaxioCustomerDto?)null);

        var result = await _service.ListSubscriptionsAsync("missing");

        Assert.Empty(result);
        await _maxio.DidNotReceive().ListCustomerSubscriptionsAsync(Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    private void SetupProduct(string handle)
    {
        _maxio.ReadProductByHandleAsync(handle, default).Returns(new MaxioProductDto
        {
            Handle = handle,
            Name = handle,
            ProductFamilyHandle = FamilyHandle,
            PriceInCents = 100,
            Interval = 1,
            IntervalUnit = "month"
        });
    }

    private sealed class StaticOptionsMonitor : IOptionsMonitor<MaxioOptions>
    {
        public StaticOptionsMonitor(MaxioOptions currentValue)
        {
            CurrentValue = currentValue;
        }

        public MaxioOptions CurrentValue { get; }

        public MaxioOptions Get(string? name) => CurrentValue;

        public IDisposable? OnChange(Action<MaxioOptions, string?> listener) => NullDisposable.Instance;

        private sealed class NullDisposable : IDisposable
        {
            public static readonly NullDisposable Instance = new();
            public void Dispose() { }
        }
    }
}
