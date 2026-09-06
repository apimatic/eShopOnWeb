using System.Net;
using Microsoft.eShopWeb.Infrastructure.Billing.Maxio;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Billing.Maxio;

/// <summary>
/// In-memory stand-in for the Advanced Billing API that reproduces the behaviour the integration
/// depends on: 404 on a lookup miss, and 422 "reference must be unique" when an application-supplied
/// reference is already taken.
/// </summary>
internal sealed class FakeMaxioApiClient : IMaxioApiClient
{
    private readonly object _gate = new();
    private long _nextId = 1000;

    public List<MaxioProduct> Products { get; } = new();

    public List<MaxioCustomer> Customers { get; } = new();

    public List<MaxioSubscription> Subscriptions { get; } = new();

    public string SiteCurrency { get; set; } = "USD";

    public int CreateCustomerCalls { get; private set; }

    public int CreateSubscriptionCalls { get; private set; }

    /// <summary>Invoked at the start of every create-subscription call, to interleave a competing caller.</summary>
    public Func<Task>? BeforeCreateSubscription { get; set; }

    public Task<MaxioSite> GetSiteAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(new MaxioSite { Id = 1, Subdomain = "test-site", Currency = SiteCurrency, Test = true });

    public Task<IReadOnlyList<MaxioProduct>> ListProductsForFamilyAsync(
        string productFamilyHandle,
        CancellationToken cancellationToken = default)
    {
        IReadOnlyList<MaxioProduct> products = Products
            .Where(p => p.ProductFamily?.Handle == productFamilyHandle)
            .ToList();

        return Task.FromResult(products);
    }

    public Task<MaxioCustomer?> FindCustomerByReferenceAsync(
        string reference,
        CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            return Task.FromResult(Customers.FirstOrDefault(c => c.Reference == reference));
        }
    }

    public Task<MaxioCustomer> CreateCustomerAsync(
        MaxioCreateCustomer customer,
        CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            CreateCustomerCalls++;

            if (Customers.Any(c => c.Reference == customer.Reference))
            {
                throw DuplicateReference("/customers.json");
            }

            var created = new MaxioCustomer
            {
                Id = Interlocked.Increment(ref _nextId),
                Reference = customer.Reference,
                Email = customer.Email,
                FirstName = customer.FirstName,
                LastName = customer.LastName
            };

            Customers.Add(created);
            return Task.FromResult(created);
        }
    }

    public Task<MaxioSubscription?> FindSubscriptionByReferenceAsync(
        string reference,
        CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            return Task.FromResult(Subscriptions.FirstOrDefault(s => s.Reference == reference));
        }
    }

    public async Task<MaxioSubscription> CreateSubscriptionAsync(
        MaxioCreateSubscription subscription,
        CancellationToken cancellationToken = default)
    {
        if (BeforeCreateSubscription is not null)
        {
            await BeforeCreateSubscription();
        }

        lock (_gate)
        {
            CreateSubscriptionCalls++;

            if (Subscriptions.Any(s => s.Reference == subscription.Reference))
            {
                throw DuplicateReference("/subscriptions.json");
            }

            var customer = Customers.First(c => c.Reference == subscription.CustomerReference);
            var product = Products.First(p => p.Handle == subscription.ProductHandle);

            var created = new MaxioSubscription
            {
                Id = Interlocked.Increment(ref _nextId),
                State = "active",
                Reference = subscription.Reference,
                Currency = SiteCurrency,
                ProductPriceInCents = product.PriceInCents,
                PaymentCollectionMethod = subscription.PaymentCollectionMethod,
                CurrentPeriodStartsAt = DateTimeOffset.UtcNow,
                CurrentPeriodEndsAt = DateTimeOffset.UtcNow.AddMonths(1),
                NextAssessmentAt = DateTimeOffset.UtcNow.AddMonths(1),
                ActivatedAt = DateTimeOffset.UtcNow,
                Product = product,
                Customer = customer
            };

            Subscriptions.Add(created);
            return created;
        }
    }

    public Task<IReadOnlyList<MaxioSubscription>> ListSubscriptionsForCustomerAsync(
        long customerId,
        CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            IReadOnlyList<MaxioSubscription> subscriptions = Subscriptions
                .Where(s => s.Customer?.Id == customerId)
                .ToList();

            return Task.FromResult(subscriptions);
        }
    }

    private static MaxioApiException DuplicateReference(string path) => new(
        HttpStatusCode.UnprocessableEntity,
        "POST",
        path,
        new[] { "Reference: must be unique - that value has been taken." },
        requestId: "test-request-id",
        rawBody: null);
}
