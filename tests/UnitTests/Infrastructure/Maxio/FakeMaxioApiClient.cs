using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.Infrastructure.Maxio.Http;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Maxio;

/// <summary>
/// Hand-written in-memory fake of the Maxio transport so the orchestration service can be tested
/// without HTTP. Records how often the mutating calls happen so idempotency can be asserted.
/// </summary>
internal sealed class FakeMaxioApiClient : IMaxioApiClient
{
    public List<ProductWire> Products { get; } = new();
    public List<CustomerWire> Customers { get; } = new();
    public List<SubscriptionWire> Subscriptions { get; } = new();

    public int CreateCustomerCalls { get; private set; }
    public int CreateSubscriptionCalls { get; private set; }

    private long _nextCustomerId = 1000;
    private long _nextSubscriptionId = 5000;

    public Task<IReadOnlyList<ProductWire>> ListProductsForFamilyAsync(string familyHandle, CancellationToken cancellationToken)
        => Task.FromResult<IReadOnlyList<ProductWire>>(Products.ToList());

    public Task<CustomerWire?> FindCustomerByReferenceAsync(string reference, CancellationToken cancellationToken)
        => Task.FromResult(Customers.FirstOrDefault(c => c.Reference == reference));

    public Task<CustomerWire> CreateCustomerAsync(CreateCustomerWire customer, CancellationToken cancellationToken)
    {
        CreateCustomerCalls++;
        var created = new CustomerWire
        {
            Id = _nextCustomerId++,
            Reference = customer.Reference,
            Email = customer.Email,
            FirstName = customer.FirstName,
            LastName = customer.LastName,
        };
        Customers.Add(created);
        return Task.FromResult(created);
    }

    public Task<SubscriptionWire> CreateSubscriptionAsync(CreateSubscriptionWire subscription, CancellationToken cancellationToken)
    {
        CreateSubscriptionCalls++;
        var product = Products.FirstOrDefault(p => p.Handle == subscription.ProductHandle);
        var created = new SubscriptionWire
        {
            Id = _nextSubscriptionId++,
            State = "active",
            PaymentCollectionMethod = subscription.PaymentCollectionMethod,
            CurrentPeriodEndsAt = new System.DateTimeOffset(2026, 10, 10, 0, 0, 0, System.TimeSpan.Zero),
            ProductPriceInCents = product?.PriceInCents,
            Product = product,
            Customer = Customers.FirstOrDefault(c => c.Id == subscription.CustomerId),
        };
        Subscriptions.Add(created);
        return Task.FromResult(created);
    }

    public Task<IReadOnlyList<SubscriptionWire>> ListCustomerSubscriptionsAsync(long customerId, CancellationToken cancellationToken)
        => Task.FromResult<IReadOnlyList<SubscriptionWire>>(
            Subscriptions.Where(s => s.Customer?.Id == customerId).ToList());
}
