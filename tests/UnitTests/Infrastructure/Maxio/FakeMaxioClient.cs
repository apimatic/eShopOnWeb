using Microsoft.eShopWeb.Infrastructure.Identity;
using Microsoft.eShopWeb.Infrastructure.Maxio;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Maxio;

/// <summary>
/// Deterministic in-memory stand-in for IMaxioClient, scripted per test.
/// </summary>
internal sealed class FakeMaxioClient : IMaxioClient
{
    public List<MaxioProduct> Products { get; } = new();

    public MaxioCustomer? CustomerFoundByReference { get; set; }

    /// <summary>When set, sequential lookup results are served from this queue instead of <see cref="CustomerFoundByReference"/>.</summary>
    public Queue<MaxioCustomer?>? LookupResults { get; set; }

    /// <summary>Responses (MaxioCustomer or MaxioApiException) served by CreateCustomerAsync, in order.</summary>
    public Queue<object> CreateCustomerResponses { get; } = new();

    /// <summary>Responses (MaxioSubscription or MaxioApiException) served by CreateSubscriptionAsync, in order.</summary>
    public Queue<object> CreateSubscriptionResponses { get; } = new();

    public List<MaxioSubscription> CustomerSubscriptions { get; } = new();

    public List<string> CustomerLookups { get; } = new();

    /// <summary>Hook invoked (once per create call) before the scripted response is served.</summary>
    public Action? OnSubscriptionCreate { get; set; }

    public List<(string ProductHandle, int CustomerId, string Reference, string? PaymentCollectionMethod)> SubscriptionCreateCalls { get; } = new();

    public int CreateSubscriptionCallCount => SubscriptionCreateCalls.Count;

    public Task<MaxioCustomer?> FindCustomerByReferenceAsync(string reference, CancellationToken cancellationToken = default)
    {
        CustomerLookups.Add(reference);
        if (LookupResults is { Count: > 0 })
        {
            return Task.FromResult(LookupResults.Dequeue());
        }
        return Task.FromResult(CustomerFoundByReference);
    }

    public Task<MaxioCustomer> CreateCustomerAsync(MaxioCustomer customer, CancellationToken cancellationToken = default)
    {
        var response = Dequeue(CreateCustomerResponses);
        if (response is MaxioApiException exception)
        {
            throw exception;
        }
        return Task.FromResult((MaxioCustomer)response!);
    }

    public Task<IReadOnlyList<MaxioProduct>> ListProductsAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<MaxioProduct>>(Products);

    public Task<MaxioSubscription> CreateSubscriptionAsync(string productHandle, int customerId, string reference, string? paymentCollectionMethod = null, CancellationToken cancellationToken = default)
    {
        SubscriptionCreateCalls.Add((productHandle, customerId, reference, paymentCollectionMethod));
        OnSubscriptionCreate?.Invoke();
        var response = Dequeue(CreateSubscriptionResponses);
        if (response is MaxioApiException exception)
        {
            throw exception;
        }
        return Task.FromResult((MaxioSubscription)response!);
    }

    public Task<IReadOnlyList<MaxioSubscription>> ListCustomerSubscriptionsAsync(int customerId, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<MaxioSubscription>>(CustomerSubscriptions);

    private static object? Dequeue(Queue<object> queue) =>
        queue.Count > 0 ? queue.Dequeue() : throw new InvalidOperationException("FakeMaxioClient has no scripted response left.");
}

internal static class MaxioTestData
{
    public const string FamilyHandle = "eshop-subscribe";
    public const string UserEmail = "demouser@microsoft.com";
    public const string UserId = "abc-123";

    public static MaxioOptions Options => new()
    {
        ApiKey = "test-key",
        Subdomain = "test-site",
        ProductFamilyHandle = FamilyHandle
    };

    public static MaxioProduct Product(string handle, string name, long priceInCents, string? familyHandle = FamilyHandle) =>
        new()
        {
            Id = 1,
            Handle = handle,
            Name = name,
            PriceInCents = priceInCents,
            Interval = 1,
            IntervalUnit = "month",
            ProductFamily = familyHandle is null ? null : new MaxioProductFamily { Handle = familyHandle }
        };

    public static MaxioSubscription Subscription(int id, int customerId, string? reference = null) =>
        new()
        {
            Id = id,
            State = "active",
            ProductPriceInCents = 29900,
            NextAssessmentAt = new DateTimeOffset(2026, 11, 6, 12, 0, 0, TimeSpan.Zero),
            CurrentPeriodEndsAt = new DateTimeOffset(2026, 11, 6, 12, 0, 0, TimeSpan.Zero),
            CreatedAt = DateTimeOffset.UtcNow,
            Customer = new MaxioCustomer { Id = customerId },
            Reference = reference
        };

    public static MaxioApiException PaymentRequiredError => new(
        422,
        new List<string> { "No payment method was on file for the $299.00 balance" },
        "422");

    public static MaxioApiException ReferenceTakenError => new(
        422,
        new List<string> { "Reference: must be unique - that value has been taken." },
        "422");

    public static MaxioApiException CustomerReferenceTakenError => new(
        422,
        new List<string> { "customer reference: has already been taken" },
        "422");
}