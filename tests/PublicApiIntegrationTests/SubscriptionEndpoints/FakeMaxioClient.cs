using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.Infrastructure.Maxio;
using Microsoft.eShopWeb.Infrastructure.Maxio.Dtos;

namespace PublicApiIntegrationTests.SubscriptionEndpoints;

/// <summary>
/// In-memory fake of <see cref="IMaxioClient"/> used to exercise the subscription
/// endpoints without hitting the Maxio sandbox.
/// </summary>
public class FakeMaxioClient : IMaxioClient
{
    public List<MaxioProductDto> Products { get; } = new();
    public List<MaxioSubscriptionDto> Subscriptions { get; } = new();
    public MaxioCustomerDto? Customer { get; set; }
    public int NextSubscriptionId { get; set; } = 1;
    public int NextCustomerId { get; set; } = 1;
    public int CreateSubscriptionCalls { get; private set; }
    public int CreateCustomerCalls { get; private set; }

    public Task<MaxioCustomerDto?> GetCustomerByReferenceAsync(string reference, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(Customer);
    }

    public Task<MaxioCustomerDto> CreateCustomerAsync(CreateCustomerRequest request, CancellationToken cancellationToken = default)
    {
        CreateCustomerCalls++;
        Customer = new MaxioCustomerDto
        {
            Id = NextCustomerId++,
            FirstName = request.Customer.FirstName,
            LastName = request.Customer.LastName,
            Email = request.Customer.Email,
            Reference = request.Customer.Reference
        };
        return Task.FromResult(Customer);
    }

    public Task<IReadOnlyList<MaxioProductDto>> ListProductsAsync(string productFamilyHandle, CancellationToken cancellationToken = default)
    {
        return Task.FromResult<IReadOnlyList<MaxioProductDto>>(Products);
    }

    public Task<MaxioProductDto?> GetProductByHandleAsync(string handle, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(Products.FirstOrDefault(p => p.Handle == handle));
    }

    public Task<MaxioSubscriptionDto> CreateSubscriptionAsync(CreateSubscriptionRequest request, CancellationToken cancellationToken = default)
    {
        CreateSubscriptionCalls++;
        var product = Products.First(p => p.Handle == request.Subscription.ProductHandle);
        var subscription = new MaxioSubscriptionDto
        {
            Id = NextSubscriptionId++,
            State = "active",
            ProductPriceInCents = product.PriceInCents,
            CurrentPeriodEndsAt = DateTimeOffset.UtcNow.AddMonths(1),
            ActivatedAt = DateTimeOffset.UtcNow,
            PaymentCollectionMethod = "remittance",
            Product = product,
            Customer = Customer
        };
        Subscriptions.Add(subscription);
        return Task.FromResult(subscription);
    }

    public Task<MaxioSubscriptionDto> GetSubscriptionAsync(int subscriptionId, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(Subscriptions.First(s => s.Id == subscriptionId));
    }

    public Task<IReadOnlyList<MaxioSubscriptionDto>> ListCustomerSubscriptionsAsync(int customerId, CancellationToken cancellationToken = default)
    {
        return Task.FromResult<IReadOnlyList<MaxioSubscriptionDto>>(Subscriptions);
    }
}
