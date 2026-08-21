using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class SubscriptionBillingService : ISubscriptionBillingService
{
    private static readonly HashSet<string> LiveStates = new(StringComparer.OrdinalIgnoreCase)
    {
        "pending",
        "trialing",
        "assessing",
        "active",
        "past_due",
        "soft_failure",
        "unpaid",
        "awaiting_signup"
    };

    private readonly IMaxioBillingGateway _maxio;
    private readonly MaxioSettings _settings;
    private readonly IAppLogger<SubscriptionBillingService> _logger;
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _gates = new();

    public SubscriptionBillingService(
        IMaxioBillingGateway maxio,
        MaxioSettings settings,
        IAppLogger<SubscriptionBillingService> logger)
    {
        _maxio = maxio;
        _settings = settings;
        _logger = logger;
    }

    public async Task<IReadOnlyList<SubscriptionPlan>> ListPlansAsync(CancellationToken cancellationToken)
    {
        EnsureConfigured();
        var familyHandle = _settings.ProductFamilyHandle;
        var plans = await _maxio.ListProductsForFamilyAsync(familyHandle, cancellationToken);
        return plans
            .Where(p => !string.IsNullOrWhiteSpace(p.Handle))
            .OrderBy(p => p.PriceInCents)
            .ToList();
    }

    public async Task<SubscribeResult> SubscribeAsync(ShopperIdentity shopper, string productHandle, CancellationToken cancellationToken)
    {
        EnsureConfigured();
        GuardShopper(shopper);

        if (string.IsNullOrWhiteSpace(productHandle))
        {
            throw new BillingValidationException("A productHandle is required to subscribe.");
        }

        productHandle = productHandle.Trim();

        var gate = _gates.GetOrAdd($"{shopper.UserId}:{productHandle}", _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            return await SubscribeCoreAsync(shopper, productHandle, cancellationToken);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<IReadOnlyList<CustomerSubscription>> ListMySubscriptionsAsync(ShopperIdentity shopper, CancellationToken cancellationToken)
    {
        EnsureConfigured();
        GuardShopper(shopper);

        var customer = await _maxio.FindCustomerByReferenceAsync(shopper.UserId, cancellationToken);
        if (customer is null)
        {
            return Array.Empty<CustomerSubscription>();
        }

        return await _maxio.ListCustomerSubscriptionsAsync(customer.Id, cancellationToken);
    }

    private async Task<SubscribeResult> SubscribeCoreAsync(ShopperIdentity shopper, string productHandle, CancellationToken cancellationToken)
    {
        await EnsurePlanInConfiguredFamilyAsync(productHandle, cancellationToken);

        var customer = await EnsureCustomerAsync(shopper, cancellationToken);
        var subscriptionReference = BuildSubscriptionReference(shopper.UserId, productHandle);

        var existing = await FindLiveSubscriptionAsync(customer.Id, productHandle, subscriptionReference, cancellationToken);
        if (existing is not null)
        {
            _logger.LogInformation("Returning existing Maxio subscription {SubscriptionId} for user {UserId} plan {ProductHandle}.",
                existing.Id, shopper.UserId, productHandle);
            return new SubscribeResult(existing, Created: false);
        }

        try
        {
            var created = await _maxio.CreateSubscriptionAsync(
                new NewMaxioSubscription(customer.Id, productHandle, subscriptionReference),
                BuildUniquenessToken("subscription", shopper.UserId, productHandle),
                cancellationToken);

            _logger.LogInformation("Created Maxio subscription {SubscriptionId} for user {UserId} plan {ProductHandle}.",
                created.Id, shopper.UserId, productHandle);
            return new SubscribeResult(created, Created: true);
        }
        catch (BillingConflictException)
        {
            _logger.LogWarning("Maxio uniqueness conflict while subscribing user {UserId} to {ProductHandle}; resolving existing subscription.",
                shopper.UserId, productHandle);
            var resolved = await FindLiveSubscriptionAsync(customer.Id, productHandle, subscriptionReference, cancellationToken);
            if (resolved is not null)
            {
                return new SubscribeResult(resolved, Created: false);
            }

            // A prior 422 can consume uniqueness_token without creating a subscription.
            var retried = await _maxio.CreateSubscriptionAsync(
                new NewMaxioSubscription(customer.Id, productHandle, subscriptionReference),
                Guid.NewGuid().ToString(),
                cancellationToken);

            _logger.LogInformation("Created Maxio subscription {SubscriptionId} for user {UserId} plan {ProductHandle} after uniqueness retry.",
                retried.Id, shopper.UserId, productHandle);
            return new SubscribeResult(retried, Created: true);
        }
        catch (BillingValidationException ex) when (LooksLikeDuplicate(ex))
        {
            _logger.LogWarning("Maxio rejected duplicate subscription for user {UserId} plan {ProductHandle}; resolving existing subscription.",
                shopper.UserId, productHandle);
            return await ResolveAfterDuplicateAsync(customer.Id, productHandle, subscriptionReference, cancellationToken);
        }
    }

    private async Task EnsurePlanInConfiguredFamilyAsync(string productHandle, CancellationToken cancellationToken)
    {
        var product = await _maxio.GetProductByHandleAsync(productHandle, cancellationToken);
        if (product is null ||
            !string.Equals(product.ProductFamilyHandle, _settings.ProductFamilyHandle, StringComparison.OrdinalIgnoreCase))
        {
            throw new UnknownSubscriptionPlanException(productHandle);
        }
    }

    private async Task<MaxioCustomer> EnsureCustomerAsync(ShopperIdentity shopper, CancellationToken cancellationToken)
    {
        var existing = await _maxio.FindCustomerByReferenceAsync(shopper.UserId, cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        var (firstName, lastName) = SplitName(shopper);
        try
        {
            var created = await _maxio.CreateCustomerAsync(
                new NewMaxioCustomer(shopper.UserId, shopper.Email, firstName, lastName),
                BuildUniquenessToken("customer", shopper.UserId, string.Empty),
                cancellationToken);

            _logger.LogInformation("Created Maxio customer {CustomerId} for user {UserId}.", created.Id, shopper.UserId);
            return created;
        }
        catch (BillingConflictException)
        {
            return await RequireCustomerByReferenceAsync(shopper.UserId, cancellationToken);
        }
        catch (BillingValidationException ex) when (LooksLikeDuplicate(ex))
        {
            return await RequireCustomerByReferenceAsync(shopper.UserId, cancellationToken);
        }
    }

    private async Task<MaxioCustomer> RequireCustomerByReferenceAsync(string userId, CancellationToken cancellationToken)
    {
        var customer = await _maxio.FindCustomerByReferenceAsync(userId, cancellationToken);
        if (customer is null)
        {
            throw new BillingIntegrationException(
                "Maxio reported a duplicate customer but lookup by reference returned nothing.",
                System.Net.HttpStatusCode.BadGateway);
        }

        return customer;
    }

    private async Task<CustomerSubscription?> FindLiveSubscriptionAsync(
        int customerId,
        string productHandle,
        string subscriptionReference,
        CancellationToken cancellationToken)
    {
        var byReference = await _maxio.FindSubscriptionByReferenceAsync(subscriptionReference, cancellationToken);
        if (byReference is not null && IsLive(byReference) && MatchesPlan(byReference, productHandle))
        {
            return byReference;
        }

        var subscriptions = await _maxio.ListCustomerSubscriptionsAsync(customerId, cancellationToken);
        return subscriptions.FirstOrDefault(s => IsLive(s) && MatchesPlan(s, productHandle));
    }

    private async Task<SubscribeResult> ResolveAfterDuplicateAsync(
        int customerId,
        string productHandle,
        string subscriptionReference,
        CancellationToken cancellationToken)
    {
        var existing = await FindLiveSubscriptionAsync(customerId, productHandle, subscriptionReference, cancellationToken);
        if (existing is not null)
        {
            return new SubscribeResult(existing, Created: false);
        }

        var byReference = await _maxio.FindSubscriptionByReferenceAsync(subscriptionReference, cancellationToken);
        if (byReference is not null)
        {
            return new SubscribeResult(byReference, Created: false);
        }

        throw new BillingIntegrationException(
            "Maxio reported a duplicate subscription but the existing record could not be loaded.",
            System.Net.HttpStatusCode.BadGateway);
    }

    private void EnsureConfigured()
    {
        if (string.IsNullOrWhiteSpace(_settings.ApiKey) ||
            (string.IsNullOrWhiteSpace(_settings.Subdomain) && string.IsNullOrWhiteSpace(_settings.BaseUrl)) ||
            string.IsNullOrWhiteSpace(_settings.ProductFamilyHandle))
        {
            throw new BillingIntegrationException(
                "Maxio billing is not configured. Set Maxio:ApiKey, Maxio:Subdomain (or Maxio:BaseUrl), and Maxio:ProductFamilyHandle.",
                System.Net.HttpStatusCode.ServiceUnavailable);
        }
    }

    private static void GuardShopper(ShopperIdentity shopper)
    {
        if (string.IsNullOrWhiteSpace(shopper.UserId))
        {
            throw new BillingValidationException("The authenticated user is missing an identifier.");
        }

        if (string.IsNullOrWhiteSpace(shopper.Email))
        {
            throw new BillingValidationException("The authenticated user is missing an email address required to create a Maxio customer.");
        }
    }

    private static bool IsLive(CustomerSubscription subscription) =>
        !string.IsNullOrWhiteSpace(subscription.State) && LiveStates.Contains(subscription.State);

    private static bool MatchesPlan(CustomerSubscription subscription, string productHandle) =>
        string.Equals(subscription.ProductHandle, productHandle, StringComparison.OrdinalIgnoreCase);

    private static bool LooksLikeDuplicate(BillingValidationException exception)
    {
        foreach (var error in exception.Errors.Append(exception.Message))
        {
            if (error.Contains("duplicate", StringComparison.OrdinalIgnoreCase) ||
                error.Contains("already been taken", StringComparison.OrdinalIgnoreCase) ||
                error.Contains("must be unique", StringComparison.OrdinalIgnoreCase) ||
                error.Contains("uniqueness", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    internal static (string FirstName, string LastName) SplitName(ShopperIdentity shopper)
    {
        var source = shopper.UserName ?? shopper.Email;
        var local = source.Contains('@', StringComparison.Ordinal)
            ? source[..source.IndexOf('@')]
            : source;

        local = string.IsNullOrWhiteSpace(local) ? "Shopper" : local.Trim();
        if (local.Length > 0)
        {
            local = char.ToUpperInvariant(local[0]) + local[1..];
        }

        return (local, "Shopper");
    }

    internal static string BuildSubscriptionReference(string userId, string productHandle) =>
        $"eshop:{userId}:{productHandle}";

    internal static string BuildUniquenessToken(string kind, string userId, string productHandle)
    {
        var input = $"eshop-on-web:{kind}:{userId}:{productHandle}";
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return new Guid(hash.AsSpan(0, 16)).ToString();
    }
}
