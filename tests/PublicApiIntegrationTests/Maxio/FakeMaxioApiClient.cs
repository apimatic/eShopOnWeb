using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.PublicApi.Maxio;
using Microsoft.eShopWeb.PublicApi.Maxio.Models;

namespace PublicApiIntegrationTests.Maxio;

/// <summary>
/// In-memory stand-in for the Maxio API used to unit test <see cref="MaxioSubscriptionService"/>'s
/// orchestration logic (ensure-customer, idempotent subscribe) without any HTTP calls.
/// </summary>
public class FakeMaxioApiClient : IMaxioApiClient
{
    public List<MaxioProduct> Products { get; } = new();
    public List<MaxioCustomer> Customers { get; } = new();
    public List<MaxioSubscription> Subscriptions { get; } = new();

    public int CreateCustomerCallCount { get; private set; }
    public int CreateSubscriptionCallCount { get; private set; }

    private long _nextCustomerId = 1;
    private long _nextSubscriptionId = 1;

    public Task<MaxioCustomer?> LookupCustomerByReferenceAsync(string reference, CancellationToken cancellationToken = default) =>
        Task.FromResult(Customers.FirstOrDefault(c => c.Reference == reference));

    public Task<MaxioCustomer> CreateCustomerAsync(CreateMaxioCustomerRequest request, CancellationToken cancellationToken = default)
    {
        CreateCustomerCallCount++;
        var customer = new MaxioCustomer
        {
            Id = _nextCustomerId++,
            FirstName = request.FirstName,
            LastName = request.LastName,
            Email = request.Email,
            Reference = request.Reference
        };
        Customers.Add(customer);
        return Task.FromResult(customer);
    }

    public Task<IReadOnlyList<MaxioProduct>> ListProductsForFamilyAsync(string productFamilyHandle, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<MaxioProduct>>(Products);

    public Task<IReadOnlyList<MaxioSubscription>> ListCustomerSubscriptionsAsync(long customerId, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<MaxioSubscription>>(Subscriptions.Where(s => s.Customer?.Id == customerId).ToList());

    public Task<MaxioSubscription> CreateSubscriptionAsync(CreateMaxioSubscriptionRequest request, CancellationToken cancellationToken = default)
    {
        CreateSubscriptionCallCount++;
        var customer = Customers.First(c => c.Reference == request.CustomerReference);
        var product = Products.First(p => p.Handle == request.ProductHandle);
        var subscription = new MaxioSubscription
        {
            Id = _nextSubscriptionId++,
            State = "active",
            Customer = customer,
            Product = product
        };
        Subscriptions.Add(subscription);
        return Task.FromResult(subscription);
    }
}
