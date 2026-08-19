using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.Billing;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

public sealed class MaxioSubscriptionBillingService : ISubscriptionBillingService
{
    private static readonly HashSet<string> LiveStates = new(StringComparer.OrdinalIgnoreCase)
    {
        "active", "assessing", "pending", "trialing", "paused", "past_due", "soft_failure", "unpaid"
    };

    private static readonly ConcurrentDictionary<string, SemaphoreSlim> CustomerGates = new(StringComparer.Ordinal);

    private readonly MaxioApiClient _api;
    private readonly MaxioSettings _settings;
    private readonly ILogger<MaxioSubscriptionBillingService> _logger;

    public MaxioSubscriptionBillingService(
        HttpClient httpClient,
        IOptions<MaxioSettings> options,
        ILogger<MaxioSubscriptionBillingService> logger)
        : this(new MaxioApiClient(httpClient), options, logger)
    {
    }

    internal MaxioSubscriptionBillingService(
        MaxioApiClient api,
        IOptions<MaxioSettings> options,
        ILogger<MaxioSubscriptionBillingService> logger)
    {
        _api = api;
        _settings = options.Value;
        _logger = logger;
    }

    public async Task<IReadOnlyList<SubscriptionPlan>> ListPlansAsync(CancellationToken cancellationToken = default)
    {
        EnsureProductFamilyConfigured();
        var products = await _api.ListProductsForFamilyAsync(_settings.ProductFamilyHandle, cancellationToken);
        return products
            .Where(product => product.ArchivedAt is null && !string.IsNullOrWhiteSpace(product.Handle))
            .Select(ToPlan)
            .ToList();
    }

    public async Task<SubscriptionStatus> SubscribeAsync(
        ShopperIdentity shopper,
        string productHandle,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(shopper);
        if (string.IsNullOrWhiteSpace(shopper.UserId))
        {
            throw new ArgumentException("Shopper user id is required.", nameof(shopper));
        }

        if (string.IsNullOrWhiteSpace(productHandle))
        {
            throw new ArgumentException("A product handle is required.", nameof(productHandle));
        }

        EnsureProductFamilyConfigured();

        var handle = productHandle.Trim();
        var plans = await ListPlansAsync(cancellationToken);
        if (plans.All(plan => !string.Equals(plan.Handle, handle, StringComparison.OrdinalIgnoreCase)))
        {
            throw new SubscriptionPlanNotFoundException(handle);
        }

        var gate = CustomerGates.GetOrAdd(shopper.UserId, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            var customer = await EnsureCustomerAsync(shopper, cancellationToken);
            var subscriptionReference = BuildSubscriptionReference(shopper.UserId, handle);

            var existingByReference = await _api.LookupSubscriptionByReferenceAsync(subscriptionReference, cancellationToken);
            if (existingByReference is not null && IsLive(existingByReference))
            {
                _logger.LogInformation(
                    "Returning existing Maxio subscription {SubscriptionId} for shopper {ShopperId} on plan {ProductHandle}.",
                    existingByReference.Id, shopper.UserId, handle);
                return ToStatus(existingByReference);
            }

            var customerSubscriptions = await _api.ListCustomerSubscriptionsAsync(customer.Id!.Value, cancellationToken);
            var existingLive = customerSubscriptions.FirstOrDefault(subscription =>
                IsLive(subscription) &&
                string.Equals(subscription.Product?.Handle, handle, StringComparison.OrdinalIgnoreCase));
            if (existingLive is not null)
            {
                _logger.LogInformation(
                    "Returning existing live Maxio subscription {SubscriptionId} for shopper {ShopperId} on plan {ProductHandle}.",
                    existingLive.Id, shopper.UserId, handle);
                return ToStatus(existingLive);
            }

            try
            {
                var created = await _api.CreateSubscriptionAsync(new MaxioCreateSubscriptionRequest
                {
                    Subscription = new MaxioCreateSubscription
                    {
                        ProductHandle = handle,
                        CustomerId = customer.Id.Value,
                        Reference = subscriptionReference
                    }
                }, cancellationToken);

                _logger.LogInformation(
                    "Created Maxio subscription {SubscriptionId} for shopper {ShopperId} on plan {ProductHandle}.",
                    created.Id, shopper.UserId, handle);
                return ToStatus(created);
            }
            catch (MaxioApiException ex) when (ex.StatusCode == HttpStatusCode.UnprocessableEntity)
            {
                var raced = await _api.LookupSubscriptionByReferenceAsync(subscriptionReference, cancellationToken);
                if (raced is not null)
                {
                    return ToStatus(raced);
                }

                throw;
            }
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<IReadOnlyList<SubscriptionStatus>> ListSubscriptionsAsync(
        ShopperIdentity shopper,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(shopper);
        if (string.IsNullOrWhiteSpace(shopper.UserId))
        {
            throw new ArgumentException("Shopper user id is required.", nameof(shopper));
        }

        var customer = await _api.LookupCustomerByReferenceAsync(shopper.UserId, cancellationToken);
        if (customer?.Id is null)
        {
            return Array.Empty<SubscriptionStatus>();
        }

        var subscriptions = await _api.ListCustomerSubscriptionsAsync(customer.Id.Value, cancellationToken);
        return subscriptions.Select(ToStatus).ToList();
    }

    internal static string BuildSubscriptionReference(string userId, string productHandle)
        => $"{userId}:{productHandle}";

    private async Task<MaxioCustomer> EnsureCustomerAsync(ShopperIdentity shopper, CancellationToken cancellationToken)
    {
        var existing = await _api.LookupCustomerByReferenceAsync(shopper.UserId, cancellationToken);
        if (existing?.Id is not null)
        {
            return existing;
        }

        var (firstName, lastName) = SplitName(shopper);
        try
        {
            var created = await _api.CreateCustomerAsync(new MaxioCreateCustomerRequest
            {
                Customer = new MaxioCreateCustomer
                {
                    FirstName = firstName,
                    LastName = lastName,
                    Email = shopper.Email,
                    Reference = shopper.UserId
                }
            }, cancellationToken);

            _logger.LogInformation("Created Maxio customer {CustomerId} for shopper {ShopperId}.", created.Id, shopper.UserId);
            return created;
        }
        catch (MaxioApiException ex) when (ex.StatusCode == HttpStatusCode.UnprocessableEntity)
        {
            var raced = await _api.LookupCustomerByReferenceAsync(shopper.UserId, cancellationToken);
            if (raced?.Id is not null)
            {
                return raced;
            }

            throw;
        }
    }

    private void EnsureProductFamilyConfigured()
    {
        if (string.IsNullOrWhiteSpace(_settings.ProductFamilyHandle))
        {
            throw new MaxioConfigurationException(
                "Maxio:ProductFamilyHandle is not configured. Set MAXIO_DEFAULT_PRODUCT_FAMILY or the Maxio:ProductFamilyHandle user-secret.");
        }
    }

    private static bool IsLive(MaxioSubscription subscription)
        => !string.IsNullOrWhiteSpace(subscription.State) && LiveStates.Contains(subscription.State);

    private static SubscriptionPlan ToPlan(MaxioProduct product)
        => new(
            product.Id ?? 0,
            product.Handle!,
            product.Name ?? product.Handle!,
            product.Description,
            product.PriceInCents ?? 0,
            product.Interval ?? 1,
            product.IntervalUnit ?? "month");

    private static SubscriptionStatus ToStatus(MaxioSubscription subscription)
        => new(
            subscription.Id ?? 0,
            subscription.State ?? "unknown",
            subscription.Product?.Handle ?? string.Empty,
            subscription.Product?.Name ?? subscription.Product?.Handle ?? string.Empty,
            subscription.ProductPriceInCents ?? subscription.Product?.PriceInCents ?? 0,
            subscription.NextAssessmentAt);

    internal static (string FirstName, string LastName) SplitName(ShopperIdentity shopper)
    {
        var source = !string.IsNullOrWhiteSpace(shopper.UserName)
            ? shopper.UserName
            : shopper.Email;

        var local = source.Contains('@', StringComparison.Ordinal)
            ? source[..source.IndexOf('@')]
            : source;

        var parts = local.Split(new[] { '.', '_', '-', ' ' }, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
        {
            return ("Shopper", "Subscriber");
        }

        var first = Capitalize(parts[0]);
        var last = parts.Length > 1 ? Capitalize(parts[^1]) : "Subscriber";
        return (first, last);
    }

    private static string Capitalize(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return value;
        }

        if (value.Length == 1)
        {
            return value.ToUpperInvariant();
        }

        return char.ToUpperInvariant(value[0]) + value[1..];
    }
}
