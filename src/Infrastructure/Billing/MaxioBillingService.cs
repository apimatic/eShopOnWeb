using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Billing;

public sealed class MaxioBillingService : ISubscriptionBillingService
{
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> UserLocks = new(StringComparer.Ordinal);

    private static readonly HashSet<string> LiveStates = new(StringComparer.OrdinalIgnoreCase)
    {
        "active",
        "trialing",
        "past_due",
        "unpaid",
        "on_hold",
        "soft_failure",
        "pending",
        "paused",
        "awaiting_signup",
        "assessing"
    };

    private readonly MaxioApiClient _api;
    private readonly MaxioOptions _options;
    private readonly ILogger<MaxioBillingService> _logger;

    public MaxioBillingService(MaxioApiClient api, IOptions<MaxioOptions> options, ILogger<MaxioBillingService> logger)
    {
        _api = api;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<IReadOnlyList<SubscriptionPlan>> ListPlansAsync(CancellationToken cancellationToken = default)
    {
        EnsureConfigured();
        var products = await _api.ListProductsForFamilyAsync(_options.ProductFamilyHandle, cancellationToken);
        return products
            .Where(p => p.ArchivedAt is null && !string.IsNullOrWhiteSpace(p.Handle))
            .Select(MapPlan)
            .ToList();
    }

    public async Task<SubscribeResult> SubscribeAsync(SubscribeCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        EnsureConfigured();

        if (string.IsNullOrWhiteSpace(command.UserId))
        {
            throw new SubscriptionBillingException("A shopper identity is required to subscribe.", 401);
        }

        if (string.IsNullOrWhiteSpace(command.ProductHandle))
        {
            throw new SubscriptionBillingException("productHandle is required.", 400);
        }

        var plans = await ListPlansAsync(cancellationToken);
        if (!plans.Any(p => string.Equals(p.Handle, command.ProductHandle, StringComparison.OrdinalIgnoreCase)))
        {
            throw new SubscriptionPlanNotFoundException(command.ProductHandle);
        }

        var gate = UserLocks.GetOrAdd(command.UserId, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            return await SubscribeLockedAsync(command, cancellationToken);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<IReadOnlyList<CustomerSubscription>> ListSubscriptionsForCustomerAsync(
        string customerReference,
        CancellationToken cancellationToken = default)
    {
        EnsureConfigured();

        if (string.IsNullOrWhiteSpace(customerReference))
        {
            return Array.Empty<CustomerSubscription>();
        }

        var customer = await _api.LookupCustomerByReferenceAsync(customerReference, cancellationToken);
        if (customer is null)
        {
            return Array.Empty<CustomerSubscription>();
        }

        var subscriptions = await _api.ListCustomerSubscriptionsAsync(customer.Id, cancellationToken);
        return subscriptions
            .Where(s => BelongsToConfiguredFamily(s))
            .Select(MapSubscription)
            .ToList();
    }

    private async Task<SubscribeResult> SubscribeLockedAsync(SubscribeCommand command, CancellationToken cancellationToken)
    {
        var customer = await EnsureCustomerAsync(command, cancellationToken);
        var existing = await FindLiveSubscriptionAsync(customer.Id, command.ProductHandle, cancellationToken);
        if (existing is not null)
        {
            _logger.LogInformation(
                "Returning existing Maxio subscription {SubscriptionId} for user {UserId} plan {Plan}",
                existing.Id, command.UserId, command.ProductHandle);
            return new SubscribeResult { Subscription = MapSubscription(existing), Created = false };
        }

        var reference = BuildSubscriptionReference(command.UserId, command.ProductHandle);
        var byReference = await _api.FindSubscriptionByReferenceAsync(reference, cancellationToken);
        if (byReference is not null && IsLive(byReference.State))
        {
            return new SubscribeResult { Subscription = MapSubscription(byReference), Created = false };
        }

        if (byReference is not null)
        {
            reference = $"{reference}:{Guid.NewGuid():N}";
        }

        _logger.LogInformation(
            "Creating Maxio subscription for user {UserId} plan {Plan}",
            command.UserId, command.ProductHandle);

        MaxioSubscription created;
        try
        {
            created = await _api.CreateSubscriptionAsync(new MaxioCreateSubscription
            {
                ProductHandle = command.ProductHandle,
                CustomerId = customer.Id,
                Reference = reference,
                PaymentCollectionMethod = "remittance"
            }, cancellationToken);
        }
        catch (SubscriptionBillingException)
        {
            var raced = await FindLiveSubscriptionAsync(customer.Id, command.ProductHandle, cancellationToken);
            if (raced is not null)
            {
                return new SubscribeResult { Subscription = MapSubscription(raced), Created = false };
            }

            throw;
        }

        return new SubscribeResult { Subscription = MapSubscription(created), Created = true };
    }

    private async Task<MaxioCustomer> EnsureCustomerAsync(SubscribeCommand command, CancellationToken cancellationToken)
    {
        var existing = await _api.LookupCustomerByReferenceAsync(command.UserId, cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        _logger.LogInformation("Creating Maxio customer for user {UserId}", command.UserId);

        try
        {
            return await _api.CreateCustomerAsync(new MaxioCustomerAttributes
            {
                FirstName = command.FirstName,
                LastName = command.LastName,
                Email = command.Email,
                Reference = command.UserId,
                Organization = "eShopOnWeb"
            }, cancellationToken);
        }
        catch (SubscriptionBillingException)
        {
            var raced = await _api.LookupCustomerByReferenceAsync(command.UserId, cancellationToken);
            if (raced is not null)
            {
                return raced;
            }

            throw;
        }
    }

    private async Task<MaxioSubscription?> FindLiveSubscriptionAsync(
        int customerId,
        string productHandle,
        CancellationToken cancellationToken)
    {
        var subscriptions = await _api.ListCustomerSubscriptionsAsync(customerId, cancellationToken);
        return subscriptions.FirstOrDefault(s =>
            string.Equals(s.Product?.Handle, productHandle, StringComparison.OrdinalIgnoreCase) &&
            IsLive(s.State));
    }

    private bool BelongsToConfiguredFamily(MaxioSubscription subscription)
    {
        var familyHandle = subscription.Product?.ProductFamily?.Handle;
        if (string.IsNullOrWhiteSpace(familyHandle))
        {
            return true;
        }

        return string.Equals(familyHandle, _options.ProductFamilyHandle, StringComparison.OrdinalIgnoreCase);
    }

    private void EnsureConfigured()
    {
        if (string.IsNullOrWhiteSpace(_options.ApiKey))
        {
            throw new SubscriptionBillingException(
                "Maxio:ApiKey is not configured. Set MAXIO_API_KEY (or the Maxio:ApiKey user-secret).", 503);
        }

        if (string.IsNullOrWhiteSpace(_options.ProductFamilyHandle))
        {
            throw new SubscriptionBillingException(
                "Maxio:ProductFamilyHandle is not configured. Set MAXIO_DEFAULT_PRODUCT_FAMILY.", 503);
        }

        if (string.IsNullOrWhiteSpace(_options.BaseUrl) && string.IsNullOrWhiteSpace(_options.Subdomain))
        {
            throw new SubscriptionBillingException(
                "Maxio:Subdomain is not configured. Set MAXIO_SITE_SUBDOMAIN or Maxio:BaseUrl.", 503);
        }
    }

    private SubscriptionPlan MapPlan(MaxioProduct product)
    {
        return new SubscriptionPlan
        {
            Id = product.Id,
            Handle = product.Handle ?? string.Empty,
            Name = product.Name ?? product.Handle ?? string.Empty,
            Description = product.Description,
            PriceInCents = product.PriceInCents,
            Price = CentsToAmount(product.PriceInCents),
            Interval = product.Interval,
            IntervalUnit = product.IntervalUnit ?? string.Empty,
            ProductFamilyHandle = product.ProductFamily?.Handle ?? _options.ProductFamilyHandle
        };
    }

    private static CustomerSubscription MapSubscription(MaxioSubscription subscription)
    {
        var priceInCents = subscription.ProductPriceInCents ?? subscription.Product?.PriceInCents ?? 0;
        return new CustomerSubscription
        {
            Id = subscription.Id,
            Reference = subscription.Reference,
            State = subscription.State ?? string.Empty,
            ProductHandle = subscription.Product?.Handle ?? string.Empty,
            ProductName = subscription.Product?.Name ?? subscription.Product?.Handle ?? string.Empty,
            PriceInCents = priceInCents,
            Price = CentsToAmount(priceInCents),
            NextBillingDate = subscription.NextAssessmentAt,
            CustomerId = subscription.Customer?.Id
        };
    }

    private static decimal CentsToAmount(int cents) => cents / 100m;

    private static bool IsLive(string? state) =>
        !string.IsNullOrWhiteSpace(state) && LiveStates.Contains(state);

    internal static string BuildSubscriptionReference(string userId, string productHandle) =>
        $"{userId}:{productHandle}";
}
