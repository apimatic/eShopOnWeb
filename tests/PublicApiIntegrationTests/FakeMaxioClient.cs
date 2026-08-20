using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

namespace PublicApiIntegrationTests;

public sealed class FakeMaxioClient : IMaxioClient
{
    public const string ProductFamilyHandle = "test-family";
    private readonly object _sync = new();
    private MaxioCustomer? _customer;
    private readonly List<MaxioSubscription> _subscriptions = new();

    public int CustomerCreateCount { get; private set; }
    public int SubscriptionCreateCount { get; private set; }

    public void Reset()
    {
        lock (_sync)
        {
            _customer = null;
            _subscriptions.Clear();
            CustomerCreateCount = 0;
            SubscriptionCreateCount = 0;
        }
    }

    public Task<IReadOnlyList<MaxioProduct>> ListProductsAsync(
        string productFamilyHandle,
        CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<MaxioProduct>>(new[]
        {
            Product("basic-plan", "Basic Plan", 2900),
            Product("eshop-pro", "Pro Plan", 29900)
        });

    public Task<MaxioCustomer?> FindCustomerByReferenceAsync(
        string reference,
        CancellationToken cancellationToken)
    {
        lock (_sync)
        {
            return Task.FromResult(_customer is not null && _customer.Reference == reference
                ? _customer
                : null);
        }
    }

    public Task<MaxioCustomer> CreateCustomerAsync(
        MaxioCreateCustomer customer,
        CancellationToken cancellationToken)
    {
        lock (_sync)
        {
            _customer ??= new MaxioCustomer { Id = 71, Reference = customer.Reference };
            CustomerCreateCount++;
            return Task.FromResult(_customer);
        }
    }

    public Task<IReadOnlyList<MaxioSubscription>> ListCustomerSubscriptionsAsync(
        long customerId,
        CancellationToken cancellationToken)
    {
        lock (_sync)
        {
            return Task.FromResult<IReadOnlyList<MaxioSubscription>>(_subscriptions.ToArray());
        }
    }

    public async Task<MaxioSubscription> CreateSubscriptionAsync(
        MaxioCreateSubscription subscription,
        CancellationToken cancellationToken)
    {
        await Task.Delay(50, cancellationToken);
        lock (_sync)
        {
            var product = subscription.ProductHandle == "eshop-pro"
                ? Product("eshop-pro", "Pro Plan", 29900)
                : Product("basic-plan", "Basic Plan", 2900);
            var created = new MaxioSubscription
            {
                Id = 801 + _subscriptions.Count,
                State = "active",
                ProductPriceInCents = product.PriceInCents,
                CurrentPeriodEndsAt = DateTimeOffset.Parse("2030-02-01T00:00:00Z"),
                Currency = "USD",
                Reference = subscription.Reference,
                Customer = _customer!,
                Product = product
            };
            _subscriptions.Add(created);
            SubscriptionCreateCount++;
            return created;
        }
    }

    private static MaxioProduct Product(string handle, string name, long priceInCents) => new()
    {
        Id = priceInCents,
        Handle = handle,
        Name = name,
        Description = $"{name} description",
        PriceInCents = priceInCents,
        Interval = 1,
        IntervalUnit = "month",
        RequireCreditCard = false,
        ProductFamily = new MaxioProductFamily { Id = 1, Handle = ProductFamilyHandle }
    };
}
