using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.Infrastructure.Maxio;
using Microsoft.eShopWeb.Infrastructure.Maxio.Contracts;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.MaxioBilling;

public class MaxioBillingGatewayTests
{
    private const string UserKey = "demouser@microsoft.com";

    private readonly IMaxioApiClient _client = Substitute.For<IMaxioApiClient>();

    public MaxioBillingGatewayTests()
    {
        _client.ReadSiteAsync(Arg.Any<CancellationToken>())
            .Returns(new MaxioSiteResponse { Site = new MaxioSite { Currency = "USD", RelationshipInvoicingEnabled = true } });
    }

    [Fact]
    public async Task AddressesTheProductFamilyByHandleAndDropsArchivedPlans()
    {
        _client.ListProductsForProductFamilyAsync(Arg.Any<string>(), Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns((IReadOnlyList<MaxioProductResponse>)new[]
            {
                Product("eshop-pro", 29900),
                Product("legacy", 100, archivedAt: DateTimeOffset.UnixEpoch),
                Product("basic-plan", 2900)
            });

        var plans = await CreateGateway().ListPlansAsync();

        await _client.Received(1).ListProductsForProductFamilyAsync("handle:eshop-subscribe", false, Arg.Any<CancellationToken>());
        Assert.Equal(new[] { "basic-plan", "eshop-pro" }, plans.Select(p => p.Handle));
        Assert.All(plans, p => Assert.Equal("USD", p.Currency));
        Assert.Equal(299m, plans.Single(p => p.Handle == "eshop-pro").Price);
    }

    [Fact]
    public async Task LooksUpTheCustomerByAReferenceDerivedFromTheUser()
    {
        _client.ReadCustomerByReferenceAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new MaxioCustomerResponse { Customer = new MaxioCustomer { Id = 7, Reference = $"eshop:{UserKey}" } });

        var customer = await CreateGateway().FindCustomerAsync(UserKey);

        await _client.Received(1).ReadCustomerByReferenceAsync($"eshop:{UserKey}", Arg.Any<CancellationToken>());
        Assert.Equal(7, customer?.Id);
    }

    [Fact]
    public async Task ReusesAnExistingCustomerRatherThanCreatingASecondOne()
    {
        _client.ReadCustomerByReferenceAsync($"eshop:{UserKey}", Arg.Any<CancellationToken>())
            .Returns(new MaxioCustomerResponse { Customer = new MaxioCustomer { Id = 7 } });

        var customer = await CreateGateway().EnsureCustomerAsync(new SubscriberProfile(UserKey, UserKey));

        Assert.Equal(7, customer.Id);
        await _client.DidNotReceiveWithAnyArgs().CreateCustomerAsync(default!);
    }

    [Fact]
    public async Task CreatesTheCustomerWithTheDerivedReferenceWhenThereIsNone()
    {
        _client.ReadCustomerByReferenceAsync($"eshop:{UserKey}", Arg.Any<CancellationToken>())
            .Returns((MaxioCustomerResponse?)null);
        _client.CreateCustomerAsync(Arg.Any<MaxioCreateCustomerRequest>(), Arg.Any<CancellationToken>())
            .Returns(new MaxioCustomerResponse { Customer = new MaxioCustomer { Id = 8 } });

        var customer = await CreateGateway().EnsureCustomerAsync(new SubscriberProfile(UserKey, UserKey));

        Assert.Equal(8, customer.Id);
        var sent = (MaxioCreateCustomerRequest)_client.ReceivedCalls()
            .Single(c => c.GetMethodInfo().Name == nameof(IMaxioApiClient.CreateCustomerAsync))
            .GetArguments()[0]!;
        Assert.Equal($"eshop:{UserKey}", sent.Customer.Reference);
        Assert.Equal(UserKey, sent.Customer.Email);
        Assert.Equal("Demouser", sent.Customer.FirstName);
        Assert.False(string.IsNullOrWhiteSpace(sent.Customer.LastName));
    }

    [Fact]
    public async Task RecoversWhenAConcurrentRequestAlreadyCreatedTheCustomer()
    {
        _client.ReadCustomerByReferenceAsync($"eshop:{UserKey}", Arg.Any<CancellationToken>())
            .Returns(
                _ => Task.FromResult<MaxioCustomerResponse?>(null),
                _ => Task.FromResult<MaxioCustomerResponse?>(new MaxioCustomerResponse { Customer = new MaxioCustomer { Id = 9 } }));
        _client.CreateCustomerAsync(Arg.Any<MaxioCreateCustomerRequest>(), Arg.Any<CancellationToken>())
            .Returns<MaxioCustomerResponse>(_ => throw new MaxioApiException("createCustomer", 422, new[] { "Reference: must be unique." }));

        var customer = await CreateGateway().EnsureCustomerAsync(new SubscriberProfile(UserKey, UserKey));

        Assert.Equal(9, customer.Id);
    }

    [Fact]
    public async Task StillFailsWhenTheCustomerCreateWasRejectedForARealReason()
    {
        _client.ReadCustomerByReferenceAsync($"eshop:{UserKey}", Arg.Any<CancellationToken>())
            .Returns((MaxioCustomerResponse?)null);
        _client.CreateCustomerAsync(Arg.Any<MaxioCreateCustomerRequest>(), Arg.Any<CancellationToken>())
            .Returns<MaxioCustomerResponse>(_ => throw new MaxioApiException("createCustomer", 422, new[] { "Email address: cannot be blank." }));

        await Assert.ThrowsAsync<MaxioApiException>(() =>
            CreateGateway().EnsureCustomerAsync(new SubscriberProfile(UserKey, UserKey)));
    }

    [Fact]
    public async Task LooksUpASubscriptionByTheDerivedReference()
    {
        _client.FindSubscriptionAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns((MaxioSubscriptionResponse?)null);

        await CreateGateway().FindSubscriptionAsync(UserKey, "eshop-pro");

        await _client.Received(1).FindSubscriptionAsync($"eshop:sub:{UserKey}:eshop-pro", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task InvoicesTheSignupOnARelationshipInvoicingSite()
    {
        var sent = await CaptureCreateSubscription(relationshipInvoicingEnabled: true);

        Assert.Equal("remittance", sent.Subscription.PaymentCollectionMethod);
        Assert.Equal("eshop-pro", sent.Subscription.ProductHandle);
        Assert.Equal(42, sent.Subscription.CustomerId);
        Assert.Equal($"eshop:sub:{UserKey}:eshop-pro", sent.Subscription.Reference);
    }

    [Fact]
    public async Task InvoicesTheSignupOnALegacyStatementsSite()
    {
        var sent = await CaptureCreateSubscription(relationshipInvoicingEnabled: false);

        Assert.Equal("invoice", sent.Subscription.PaymentCollectionMethod);
    }

    [Fact]
    public async Task LetsAnOperatorPinTheCollectionMethod()
    {
        var sent = await CaptureCreateSubscription(relationshipInvoicingEnabled: true, collectionMethodOverride: "automatic");

        Assert.Equal("automatic", sent.Subscription.PaymentCollectionMethod);
    }

    [Fact]
    public async Task RefusesToCallTheProviderWhenItIsNotConfigured()
    {
        var gateway = new MaxioBillingGateway(
            _client,
            new MaxioSiteCache(NullLogger<MaxioSiteCache>.Instance),
            Options.Create(new MaxioOptions()),
            NullLogger<MaxioBillingGateway>.Instance);

        var exception = await Assert.ThrowsAsync<BillingConfigurationException>(() => gateway.ListPlansAsync());

        Assert.Contains("ApiKey", exception.Message);
        await _client.DidNotReceiveWithAnyArgs().ListProductsForProductFamilyAsync(default!);
    }

    private async Task<MaxioCreateSubscriptionRequest> CaptureCreateSubscription(bool relationshipInvoicingEnabled, string? collectionMethodOverride = null)
    {
        _client.ReadSiteAsync(Arg.Any<CancellationToken>())
            .Returns(new MaxioSiteResponse { Site = new MaxioSite { Currency = "USD", RelationshipInvoicingEnabled = relationshipInvoicingEnabled } });
        _client.CreateSubscriptionAsync(Arg.Any<MaxioCreateSubscriptionRequest>(), Arg.Any<CancellationToken>())
            .Returns(new MaxioSubscriptionResponse { Subscription = new MaxioSubscription { Id = 1, State = "active" } });

        await CreateGateway(collectionMethodOverride).CreateSubscriptionAsync(42, "eshop-pro", UserKey, "eshop-pro");

        return (MaxioCreateSubscriptionRequest)_client.ReceivedCalls()
            .Single(c => c.GetMethodInfo().Name == nameof(IMaxioApiClient.CreateSubscriptionAsync))
            .GetArguments()[0]!;
    }

    private MaxioBillingGateway CreateGateway(string? collectionMethodOverride = null) => new(
        _client,
        new MaxioSiteCache(NullLogger<MaxioSiteCache>.Instance),
        Options.Create(new MaxioOptions
        {
            ApiKey = "key",
            Subdomain = "acme",
            ProductFamilyHandle = "eshop-subscribe",
            PaymentCollectionMethod = collectionMethodOverride
        }),
        NullLogger<MaxioBillingGateway>.Instance);

    private static MaxioProductResponse Product(string handle, long priceInCents, DateTimeOffset? archivedAt = null) => new()
    {
        Product = new MaxioProduct
        {
            Id = handle.GetHashCode() & 0x7fffffff,
            Handle = handle,
            Name = handle,
            PriceInCents = priceInCents,
            Interval = 1,
            IntervalUnit = "month",
            ArchivedAt = archivedAt,
            ProductFamily = new MaxioProductFamily { Handle = "eshop-subscribe" }
        }
    };
}
