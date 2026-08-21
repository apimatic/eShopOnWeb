using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.Infrastructure.Identity;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

public interface ISubscriptionBillingService
{
    Task<IReadOnlyList<SubscriptionPlanDto>> ListPlansAsync(CancellationToken cancellationToken);
    Task<SubscribeOutcome> SubscribeAsync(ApplicationUser user, string productHandle, CancellationToken cancellationToken);
    Task<IReadOnlyList<SubscriptionDto>> ListSubscriptionsAsync(ApplicationUser user, CancellationToken cancellationToken);
}

public sealed class SubscriptionBillingService : ISubscriptionBillingService
{
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> SubscriptionLocks = new(StringComparer.Ordinal);
    private readonly IMaxioBillingClient _maxio;
    private readonly MaxioOptions _options;

    public SubscriptionBillingService(IMaxioBillingClient maxio, IOptions<MaxioOptions> options)
    {
        _maxio = maxio;
        _options = options.Value;
    }

    public async Task<IReadOnlyList<SubscriptionPlanDto>> ListPlansAsync(CancellationToken cancellationToken)
    {
        var products = await _maxio.ListProductsAsync(cancellationToken);
        return products
            .Where(product => product.ArchivedAt == null && !string.IsNullOrWhiteSpace(product.Handle))
            .OrderBy(product => product.PriceInCents)
            .Select(MapPlan)
            .ToList();
    }

    public async Task<SubscribeOutcome> SubscribeAsync(
        ApplicationUser user,
        string productHandle,
        CancellationToken cancellationToken)
    {
        var normalizedHandle = productHandle.Trim();
        if (normalizedHandle.Length == 0)
        {
            throw new SubscriptionRequestException("A productHandle is required.");
        }

        var subscriptionReference = CreateSubscriptionReference(user.Id, normalizedHandle);
        var gate = SubscriptionLocks.GetOrAdd(subscriptionReference, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            var existing = await _maxio.FindSubscriptionAsync(subscriptionReference, cancellationToken);
            if (existing != null)
            {
                return new SubscribeOutcome { Created = false, Subscription = MapSubscription(existing) };
            }

            var products = await _maxio.ListProductsAsync(cancellationToken);
            var product = products.SingleOrDefault(item =>
                item.ArchivedAt == null && string.Equals(item.Handle, normalizedHandle, StringComparison.Ordinal));
            if (product == null)
            {
                throw new SubscriptionPlanNotFoundException(normalizedHandle);
            }

            if (product.RequireCreditCard)
            {
                throw new SubscriptionRequestException("The selected plan requires a payment method, which this endpoint does not collect.");
            }

            var customer = await EnsureCustomerAsync(user, cancellationToken);
            var createRequest = new CreateMaxioSubscription
            {
                ProductHandle = product.Handle,
                CustomerId = customer.Id,
                Reference = subscriptionReference,
                UniquenessToken = CreateUniquenessToken($"subscription-v2:{subscriptionReference}")
            };

            try
            {
                var created = await _maxio.CreateSubscriptionAsync(createRequest, cancellationToken);
                return new SubscribeOutcome { Created = true, Subscription = MapSubscription(created) };
            }
            catch (MaxioApiException)
            {
                var reconciled = await FindSubscriptionWithRetryAsync(subscriptionReference, cancellationToken);
                if (reconciled != null)
                {
                    return new SubscribeOutcome { Created = false, Subscription = MapSubscription(reconciled) };
                }

                throw;
            }
        }
        finally
        {
            gate.Release();
            SubscriptionLocks.TryRemove(subscriptionReference, out _);
        }
    }

    public async Task<IReadOnlyList<SubscriptionDto>> ListSubscriptionsAsync(
        ApplicationUser user,
        CancellationToken cancellationToken)
    {
        var customer = await _maxio.FindCustomerAsync(CreateCustomerReference(user.Id), cancellationToken);
        if (customer == null)
        {
            return Array.Empty<SubscriptionDto>();
        }

        var subscriptions = await _maxio.ListCustomerSubscriptionsAsync(customer.Id, cancellationToken);
        return subscriptions
            .Where(subscription => string.Equals(
                subscription.Product?.ProductFamilyHandle,
                _options.ProductFamilyHandle,
                StringComparison.Ordinal))
            .OrderBy(subscription => subscription.Id)
            .Select(MapSubscription)
            .ToList();
    }

    private async Task<MaxioCustomer> EnsureCustomerAsync(ApplicationUser user, CancellationToken cancellationToken)
    {
        var reference = CreateCustomerReference(user.Id);
        var existing = await _maxio.FindCustomerAsync(reference, cancellationToken);
        if (existing != null)
        {
            return existing;
        }

        var email = user.Email ?? user.UserName;
        if (string.IsNullOrWhiteSpace(email))
        {
            throw new SubscriptionRequestException("The signed-in account must have an email address before subscribing.");
        }

        var localPart = email.Split('@', 2)[0];
        var request = new CreateMaxioCustomer
        {
            FirstName = string.IsNullOrWhiteSpace(localPart) ? "eShop" : localPart,
            LastName = "Customer",
            Email = email,
            Reference = reference,
            UniquenessToken = CreateUniquenessToken($"customer:{reference}")
        };

        try
        {
            return await _maxio.CreateCustomerAsync(request, cancellationToken);
        }
        catch (MaxioApiException)
        {
            var reconciled = await _maxio.FindCustomerAsync(reference, cancellationToken);
            if (reconciled != null)
            {
                return reconciled;
            }

            throw;
        }
    }

    private async Task<MaxioSubscription?> FindSubscriptionWithRetryAsync(
        string reference,
        CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < 3; attempt++)
        {
            var subscription = await _maxio.FindSubscriptionAsync(reference, cancellationToken);
            if (subscription != null)
            {
                return subscription;
            }

            if (attempt < 2)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(100 * (attempt + 1)), cancellationToken);
            }
        }

        return null;
    }

    private static SubscriptionPlanDto MapPlan(MaxioProduct product) => new()
    {
        Id = product.Id,
        Handle = product.Handle,
        Name = product.Name,
        Description = product.Description,
        PriceInCents = product.PriceInCents,
        Interval = product.Interval,
        IntervalUnit = product.IntervalUnit,
        RequiresPaymentMethod = product.RequireCreditCard
    };

    private static SubscriptionDto MapSubscription(MaxioSubscription subscription) => new()
    {
        Id = subscription.Id,
        ProductHandle = subscription.Product?.Handle ?? string.Empty,
        ProductName = subscription.Product?.Name ?? string.Empty,
        PriceInCents = subscription.ProductPriceInCents,
        Interval = subscription.Product?.Interval ?? 0,
        IntervalUnit = subscription.Product?.IntervalUnit ?? string.Empty,
        State = subscription.State,
        NextBillingAt = subscription.NextAssessmentAt ?? subscription.CurrentPeriodEndsAt,
        CurrentPeriodEndsAt = subscription.CurrentPeriodEndsAt,
        NextAssessmentAt = subscription.NextAssessmentAt
    };

    private static string CreateCustomerReference(string userId) => $"eshop-user:{userId}";

    private static string CreateSubscriptionReference(string userId, string productHandle)
    {
        var handleHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(productHandle))).ToLowerInvariant()[..16];
        return $"eshop-sub:{userId}:{handleHash}";
    }

    private static string CreateUniquenessToken(string value)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return new Guid(hash.AsSpan(0, 16)).ToString();
    }
}

public sealed class SubscriptionRequestException : Exception
{
    public SubscriptionRequestException(string message) : base(message)
    {
    }
}

public sealed class SubscriptionPlanNotFoundException : Exception
{
    public SubscriptionPlanNotFoundException(string productHandle)
        : base($"No available subscription plan has handle '{productHandle}'.")
    {
    }
}
