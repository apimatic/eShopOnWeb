using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Billing.Models;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Services;
using NSubstitute;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Services.SubscriptionServiceTests;

public class GetCustomerSubscriptions
{
    private const string USER = "demouser@microsoft.com";

    private readonly IMaxioBillingGateway _gateway = Substitute.For<IMaxioBillingGateway>();
    private readonly IAppLogger<SubscriptionService> _logger = Substitute.For<IAppLogger<SubscriptionService>>();

    [Fact]
    public async Task ReturnsEmptyListWhenUserHasNoMaxioCustomerYet()
    {
        _gateway.FindCustomerByReferenceAsync(USER, Arg.Any<CancellationToken>()).Returns((BillingCustomer?)null);

        var service = new SubscriptionService(_gateway, _logger);
        var subscriptions = await service.GetCustomerSubscriptionsAsync(USER);

        Assert.Empty(subscriptions);
        await _gateway.DidNotReceive().ListSubscriptionsForCustomerAsync(Arg.Any<long>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ListsSubscriptionsOfTheCustomerNewestFirst()
    {
        var customer = new BillingCustomer
        {
            Id = 42,
            Reference = USER,
            Email = USER,
            FirstName = "demouser",
            LastName = "eShopOnWeb",
            CreatedAt = DateTimeOffset.UtcNow
        };

        var older = new BillingSubscription
        {
            Id = 1,
            State = "canceled",
            CustomerId = 42,
            PlanHandle = "basic-plan",
            PlanName = "Basic Plan",
            PriceInCents = 2900,
            CreatedAt = DateTimeOffset.UtcNow.AddMonths(-2)
        };
        var newer = new BillingSubscription
        {
            Id = 2,
            State = "active",
            CustomerId = 42,
            PlanHandle = "eshop-pro",
            PlanName = "Pro Plan",
            PriceInCents = 29900,
            NextBillingDate = DateTimeOffset.UtcNow.AddDays(30),
            CreatedAt = DateTimeOffset.UtcNow.AddMonths(-1)
        };

        _gateway.FindCustomerByReferenceAsync(USER, Arg.Any<CancellationToken>()).Returns(customer);
        _gateway.ListSubscriptionsForCustomerAsync(42, Arg.Any<CancellationToken>())
            .Returns(new List<BillingSubscription> { older, newer });

        var service = new SubscriptionService(_gateway, _logger);
        var subscriptions = await service.GetCustomerSubscriptionsAsync(USER);

        Assert.Equal(2, subscriptions.Count);
        Assert.Equal(2, subscriptions[0].Id);
        Assert.Equal(1, subscriptions[1].Id);
    }
}
