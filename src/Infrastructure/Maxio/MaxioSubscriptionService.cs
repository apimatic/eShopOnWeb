using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity;
using Microsoft.eShopWeb.Infrastructure.Identity;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>Result of an idempotent subscribe operation.</summary>
public sealed record MaxioSubscribeResult(MaxioSubscription Subscription, bool Created);

/// <summary>
/// Orchestrates the eShopOnWeb subscription flows against Maxio Advanced Billing, which is the
/// billing system of record. A Maxio customer is keyed by the shopper's email address (as the
/// customer <c>reference</c>, which Maxio enforces as unique) and a subscription is keyed by a
/// deterministic reference of <c>{customerReference}:{productHandle}</c> (also unique while live),
/// which makes double-clicks and retries idempotent without any local database.
/// </summary>
public interface IMaxioSubscriptionService
{
    /// <summary>Lists the subscribable plans in the configured Maxio product family.</summary>
    Task<IReadOnlyList<MaxioProduct>> GetAvailablePlansAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Subscribes the shopper (identified by their email) to the given plan. Idempotent: when a live
    /// subscription for the same plan already exists it is returned instead of creating a duplicate.
    /// </summary>
    Task<MaxioSubscribeResult> SubscribeAsync(string userEmail, string productHandle, CancellationToken cancellationToken = default);

    /// <summary>Lists the shopper's current (non-canceled/non-expired) subscriptions in Maxio.</summary>
    Task<IReadOnlyList<MaxioSubscription>> GetMySubscriptionsAsync(string userEmail, CancellationToken cancellationToken = default);
}

public sealed class MaxioSubscriptionService : IMaxioSubscriptionService
{
    /// <summary>
    /// Payment collection method used when subscribing. Because the seeded plans do not require a
    /// payment method, subscriptions are created with the 'remittance' collection method (see
    /// <c>Collection-Method.yaml</c> in the Maxio spec), so no card/3-DS is captured.
    /// </summary>
    public const string PaymentCollectionMethod = "remittance";

    private static readonly string[] NonLiveStates =
    {
        "canceled",
        "expired",
        "failed_to_create"
    };

    private readonly IMaxioApiClient _maxioClient;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly MaxioOptions _options;

    public MaxioSubscriptionService(IMaxioApiClient maxioClient, UserManager<ApplicationUser> userManager, MaxioOptions options)
    {
        _maxioClient = maxioClient;
        _userManager = userManager;
        _options = options;
    }

    public async Task<IReadOnlyList<MaxioProduct>> GetAvailablePlansAsync(CancellationToken cancellationToken = default)
    {
        _options.Validate();

        MaxioProductFamily family = await FindConfiguredProductFamilyAsync(cancellationToken).ConfigureAwait(false);

        IReadOnlyList<MaxioProduct> products = await _maxioClient
            .ListProductsForProductFamilyAsync(family.Id, cancellationToken)
            .ConfigureAwait(false);

        return products
            .Where(p => p.ArchivedAt is null)
            .OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public async Task<MaxioSubscribeResult> SubscribeAsync(string userEmail, string productHandle, CancellationToken cancellationToken = default)
    {
        _options.Validate();

        if (string.IsNullOrWhiteSpace(userEmail))
        {
            throw new ArgumentException("A user identity is required to subscribe.", nameof(userEmail));
        }

        if (string.IsNullOrWhiteSpace(productHandle))
        {
            throw new ArgumentException("A product handle is required to subscribe.", nameof(productHandle));
        }

        ApplicationUser user = await GetUserOrThrowAsync(userEmail, cancellationToken).ConfigureAwait(false);
        string shopperEmail = user.Email ?? user.UserName ?? userEmail;

        // Ensure a Maxio customer exists for this shopper (idempotent, keyed by unique reference).
        MaxioCustomer customer = await EnsureCustomerAsync(shopperEmail, cancellationToken).ConfigureAwait(false);

        // A live subscription for the same plan is the replay of an earlier (or concurrent) request.
        MaxioSubscription? existing = await FindLiveSubscriptionAsync(customer.Id, productHandle, cancellationToken).ConfigureAwait(false);
        if (existing is not null)
        {
            return new MaxioSubscribeResult(existing, Created: false);
        }

        // Validate the requested plan belongs to the configured product family before creating anything.
        MaxioProduct product = await GetAvailableProductAsync(productHandle, cancellationToken).ConfigureAwait(false);

        string subscriptionReference = BuildSubscriptionReference(shopperEmail, product.Handle ?? productHandle);

        try
        {
            MaxioSubscription created = await _maxioClient.CreateSubscriptionAsync(
                new MaxioCreateSubscription
                {
                    ProductHandle = product.Handle ?? productHandle,
                    CustomerId = customer.Id,
                    Reference = subscriptionReference,
                    PaymentCollectionMethod = PaymentCollectionMethod
                },
                cancellationToken).ConfigureAwait(false);

            return new MaxioSubscribeResult(created, Created: true);
        }
        catch (MaxioApiException ex) when (ex.IsReferenceConflict)
        {
            // Another (concurrent) request won the race for the deterministic reference.
            MaxioSubscription? winner = await _maxioClient.FindSubscriptionByReferenceAsync(subscriptionReference, cancellationToken).ConfigureAwait(false);
            if (winner is not null && IsLive(winner.State))
            {
                return new MaxioSubscribeResult(winner, Created: false);
            }

            // The deterministic reference is reserved by a non-live subscription (for example a
            // previously canceled one). Fall back to a unique reference so re-subscribing still works.
            MaxioSubscription created = await _maxioClient.CreateSubscriptionAsync(
                new MaxioCreateSubscription
                {
                    ProductHandle = product.Handle ?? productHandle,
                    CustomerId = customer.Id,
                    Reference = $"{subscriptionReference}-{Guid.NewGuid():N}",
                    PaymentCollectionMethod = PaymentCollectionMethod
                },
                cancellationToken).ConfigureAwait(false);

            return new MaxioSubscribeResult(created, Created: true);
        }
    }

    public async Task<IReadOnlyList<MaxioSubscription>> GetMySubscriptionsAsync(string userEmail, CancellationToken cancellationToken = default)
    {
        _options.Validate();

        if (string.IsNullOrWhiteSpace(userEmail))
        {
            return Array.Empty<MaxioSubscription>();
        }

        ApplicationUser user = await GetUserOrThrowAsync(userEmail, cancellationToken).ConfigureAwait(false);
        string shopperEmail = user.Email ?? user.UserName ?? userEmail;

        MaxioCustomer? customer = await _maxioClient.FindCustomerByReferenceAsync(shopperEmail, cancellationToken).ConfigureAwait(false);
        if (customer is null)
        {
            return Array.Empty<MaxioSubscription>();
        }

        IReadOnlyList<MaxioSubscription> subscriptions = await _maxioClient
            .ListCustomerSubscriptionsAsync(customer.Id, cancellationToken)
            .ConfigureAwait(false);

        return subscriptions
            .Where(s => IsLive(s.State))
            .OrderByDescending(s => s.CreatedAt ?? DateTimeOffset.MinValue)
            .ToList();
    }

    private async Task<ApplicationUser> GetUserOrThrowAsync(string userEmail, CancellationToken cancellationToken)
    {
        ApplicationUser? user = await _userManager.FindByNameAsync(userEmail).ConfigureAwait(false);
        if (user is null)
        {
            throw new UserNotFoundException(userEmail);
        }

        return user;
    }

    private async Task<MaxioCustomer> EnsureCustomerAsync(string shopperEmail, CancellationToken cancellationToken)
    {
        MaxioCustomer? customer = await _maxioClient.FindCustomerByReferenceAsync(shopperEmail, cancellationToken).ConfigureAwait(false);
        if (customer is not null)
        {
            return customer;
        }

        try
        {
            return await _maxioClient.CreateCustomerAsync(
                new MaxioCreateCustomer
                {
                    FirstName = DeriveFirstName(shopperEmail),
                    LastName = "Customer",
                    Email = shopperEmail,
                    Reference = shopperEmail
                },
                cancellationToken).ConfigureAwait(false);
        }
        catch (MaxioApiException ex) when (ex.IsReferenceConflict)
        {
            // A concurrent request created the customer first; reconcile by reference.
            MaxioCustomer? winner = await _maxioClient.FindCustomerByReferenceAsync(shopperEmail, cancellationToken).ConfigureAwait(false);
            if (winner is not null)
            {
                return winner;
            }

            throw;
        }
    }

    private async Task<MaxioProduct> GetAvailableProductAsync(string productHandle, CancellationToken cancellationToken)
    {
        MaxioProduct? product = await _maxioClient.ReadProductByHandleAsync(productHandle, cancellationToken).ConfigureAwait(false);
        if (product is null || product.ArchivedAt is not null ||
            product.ProductFamily is null ||
            !string.Equals(product.ProductFamily.Handle, _options.ProductFamilyHandle, StringComparison.OrdinalIgnoreCase))
        {
            throw new SubscriptionPlanNotFoundException(productHandle);
        }

        return product;
    }

    private async Task<MaxioProductFamily> FindConfiguredProductFamilyAsync(CancellationToken cancellationToken)
    {
        IReadOnlyList<MaxioProductFamily> families = await _maxioClient.ListProductFamiliesAsync(cancellationToken).ConfigureAwait(false);
        MaxioProductFamily? family = families.FirstOrDefault(f =>
            string.Equals(f.Handle, _options.ProductFamilyHandle, StringComparison.OrdinalIgnoreCase));

        if (family is null)
        {
            throw new InvalidOperationException(
                $"The configured Maxio product family handle '{_options.ProductFamilyHandle}' was not found on the site.");
        }

        return family;
    }

    private async Task<MaxioSubscription?> FindLiveSubscriptionAsync(long customerId, string productHandle, CancellationToken cancellationToken)
    {
        IReadOnlyList<MaxioSubscription> subscriptions = await _maxioClient
            .ListCustomerSubscriptionsAsync(customerId, cancellationToken)
            .ConfigureAwait(false);

        return subscriptions.FirstOrDefault(s =>
            IsLive(s.State) &&
            string.Equals(s.Product?.Handle, productHandle, StringComparison.OrdinalIgnoreCase));
    }

    private static string BuildSubscriptionReference(string shopperEmail, string productHandle) => $"{shopperEmail}:{productHandle}";

    private static bool IsLive(string? state) =>
        !NonLiveStates.Any(s => string.Equals(s, state, StringComparison.OrdinalIgnoreCase));

    private static string DeriveFirstName(string email)
    {
        string localPart = email;
        int at = email.IndexOf('@');
        if (at > 0)
        {
            localPart = email.Substring(0, at);
        }

        return localPart.Length > 60 ? localPart.Substring(0, 60) : localPart;
    }
}
