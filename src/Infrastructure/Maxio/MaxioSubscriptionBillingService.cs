using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Models.Subscriptions;
using Microsoft.eShopWeb.Infrastructure.Maxio.Models;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Subscription billing backed by Maxio Advanced Billing, which is the system of record: eShopOnWeb
/// stores no plan or subscription state of its own and instead identifies each shopper in Maxio by
/// a reference derived from their eShopOnWeb user name.
/// </summary>
public class MaxioSubscriptionBillingService : ISubscriptionBillingService
{
    private const string PlansCacheKey = "Maxio:Plans";
    private const string SiteCurrencyCacheKey = "Maxio:SiteCurrency";
    private static readonly TimeSpan SiteCurrencyCacheDuration = TimeSpan.FromHours(1);

    private readonly IMaxioApiClient _client;
    private readonly MaxioSettings _settings;
    private readonly IMemoryCache _cache;
    private readonly SubscriberLockProvider _locks;
    private readonly ILogger<MaxioSubscriptionBillingService> _logger;

    public MaxioSubscriptionBillingService(
        IMaxioApiClient client,
        IOptions<MaxioSettings> settings,
        IMemoryCache cache,
        SubscriberLockProvider locks,
        ILogger<MaxioSubscriptionBillingService> logger)
    {
        _client = client;
        _settings = settings.Value;
        _cache = cache;
        _locks = locks;
        _logger = logger;
    }

    public async Task<IReadOnlyList<SubscriptionPlan>> ListPlansAsync(CancellationToken cancellationToken = default)
    {
        EnsureConfigured();

        if (_settings.PlanCacheSeconds > 0 &&
            _cache.TryGetValue(PlansCacheKey, out IReadOnlyList<SubscriptionPlan>? cached) &&
            cached is not null)
        {
            return cached;
        }

        var familyHandle = _settings.ProductFamilyHandle!;
        var currency = await GetSiteCurrencyAsync(cancellationToken);

        // The spec allows a product family to be addressed by id or by its handle prefixed with
        // "handle:". Handles are stable across catalog re-seeds where ids are not, so eShopOnWeb
        // always addresses the family by handle.
        var products = await ExecuteAsync(
            () => _client.ListProductsForProductFamilyAsync($"handle:{familyHandle}", false, cancellationToken),
            "list subscription plans");

        var plans = products
            .Where(product => product.ArchivedAt is null && !string.IsNullOrWhiteSpace(product.Handle))
            .Select(product => MapPlan(product, currency))
            .OrderBy(plan => plan.PriceInCents)
            .ThenBy(plan => plan.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        _logger.LogInformation(
            "Loaded {PlanCount} subscription plan(s) from Maxio product family {ProductFamilyHandle}.",
            plans.Count, familyHandle);

        if (_settings.PlanCacheSeconds > 0)
        {
            _cache.Set(PlansCacheKey, (IReadOnlyList<SubscriptionPlan>)plans,
                TimeSpan.FromSeconds(_settings.PlanCacheSeconds));
        }

        return plans;
    }

    public async Task<SubscribeResult> SubscribeAsync(SubscribeCommand command, CancellationToken cancellationToken = default)
    {
        EnsureConfigured();

        if (string.IsNullOrWhiteSpace(command.UserName))
        {
            throw new BillingValidationException(new[] { "The caller could not be identified." });
        }

        var requestedHandle = string.IsNullOrWhiteSpace(command.PlanHandle)
            ? _settings.DefaultPlanHandle
            : command.PlanHandle;

        if (string.IsNullOrWhiteSpace(requestedHandle))
        {
            throw new BillingValidationException(new[]
            {
                "A plan handle is required. Supply one in the request, or configure " +
                $"{MaxioSettings.ConfigurationSectionName}:{nameof(MaxioSettings.DefaultPlanHandle)}."
            });
        }

        // Validate the plan against the live catalog so an unknown handle is a 404 from eShopOnWeb
        // rather than an opaque rejection from Maxio.
        var plans = await ListPlansAsync(cancellationToken);
        var plan = plans.FirstOrDefault(p => string.Equals(p.Handle, requestedHandle, StringComparison.OrdinalIgnoreCase))
            ?? throw new SubscriptionPlanNotFoundException(requestedHandle!);

        var customerReference = BuildCustomerReference(command.UserName);

        using var _ = await _locks.AcquireAsync(customerReference, cancellationToken);

        var customer = await EnsureCustomerAsync(command, customerReference, cancellationToken);

        var existing = await FindLiveSubscriptionAsync(customer.Id, plan.Handle, cancellationToken);
        if (existing is not null)
        {
            _logger.LogInformation(
                "Shopper {CustomerReference} already holds subscription {SubscriptionId} ({State}) for plan {PlanHandle}; returning it unchanged.",
                customerReference, existing.Id, existing.State, plan.Handle);

            return new SubscribeResult(MapSubscription(existing, customerReference), created: false);
        }

        var subscriptionReference = await BuildSubscriptionReferenceAsync(command.UserName, plan.Handle, cancellationToken);

        var request = new MaxioCreateSubscriptionRequest
        {
            Subscription = new MaxioCreateSubscription
            {
                ProductHandle = plan.Handle,
                CustomerId = customer.Id,
                Reference = subscriptionReference,
                PaymentCollectionMethod = _settings.PaymentCollectionMethod
            }
        };

        MaxioSubscription created;
        try
        {
            created = await _client.CreateSubscriptionAsync(request, cancellationToken);
        }
        catch (MaxioApiException ex) when (ex.IsValidationFailure)
        {
            // Another instance may have enrolled this shopper between the check above and this
            // create; reconcile before surfacing the rejection.
            var raced = await FindLiveSubscriptionAsync(customer.Id, plan.Handle, cancellationToken);
            if (raced is not null)
            {
                _logger.LogInformation(
                    "Create subscription for {CustomerReference} was rejected but subscription {SubscriptionId} now exists; treating the request as already satisfied.",
                    customerReference, raced.Id);

                return new SubscribeResult(MapSubscription(raced, customerReference), created: false);
            }

            _logger.LogWarning(ex, "Maxio rejected the subscription for {CustomerReference} on plan {PlanHandle}.", customerReference, plan.Handle);
            throw new BillingValidationException(ex.Errors);
        }
        catch (Exception ex) when (ex is MaxioApiException or MaxioTransportException)
        {
            throw Translate(ex, "create the subscription");
        }

        _logger.LogInformation(
            "Enrolled shopper {CustomerReference} on plan {PlanHandle} as subscription {SubscriptionId} ({State}).",
            customerReference, plan.Handle, created.Id, created.State);

        return new SubscribeResult(MapSubscription(created, customerReference), created: true);
    }

    public async Task<IReadOnlyList<CustomerSubscription>> ListSubscriptionsAsync(string userName, CancellationToken cancellationToken = default)
    {
        EnsureConfigured();

        if (string.IsNullOrWhiteSpace(userName))
        {
            throw new BillingValidationException(new[] { "The caller could not be identified." });
        }

        var customerReference = BuildCustomerReference(userName);
        var customer = await ExecuteAsync(
            () => _client.ReadCustomerByReferenceAsync(customerReference, cancellationToken),
            "look up the billing customer");

        if (customer is null)
        {
            _logger.LogInformation("Shopper {CustomerReference} has no Maxio customer yet; reporting no subscriptions.", customerReference);
            return Array.Empty<CustomerSubscription>();
        }

        var subscriptions = await ExecuteAsync(
            () => _client.ListCustomerSubscriptionsAsync(customer.Id, cancellationToken),
            "list the subscriptions of the billing customer");

        return subscriptions
            .OrderByDescending(subscription => subscription.CreatedAt ?? DateTimeOffset.MinValue)
            .ThenByDescending(subscription => subscription.Id)
            .Select(subscription => MapSubscription(subscription, customerReference))
            .ToList();
    }

    /// <summary>
    /// Returns the Maxio customer for this shopper, creating it when it does not exist yet.
    /// Idempotent: the reference is derived from the shopper, and a create that loses a race is
    /// resolved by re-reading the customer rather than by creating a second one.
    /// </summary>
    private async Task<MaxioCustomer> EnsureCustomerAsync(
        SubscribeCommand command,
        string customerReference,
        CancellationToken cancellationToken)
    {
        var existing = await ExecuteAsync(
            () => _client.ReadCustomerByReferenceAsync(customerReference, cancellationToken),
            "look up the billing customer");

        if (existing is not null)
        {
            return existing;
        }

        var email = command.UserName.Trim();
        if (!email.Contains('@', StringComparison.Ordinal))
        {
            throw new BillingValidationException(new[]
            {
                "The signed-in user does not have an email address, which Maxio requires to create a customer."
            });
        }

        var (firstName, lastName) = ResolveName(command, email);

        var request = new MaxioCreateCustomerRequest
        {
            Customer = new MaxioCreateCustomer
            {
                FirstName = firstName,
                LastName = lastName,
                Email = email,
                Reference = customerReference,
                Organization = string.IsNullOrWhiteSpace(command.Organization) ? null : command.Organization.Trim()
            }
        };

        try
        {
            var created = await _client.CreateCustomerAsync(request, cancellationToken);
            _logger.LogInformation(
                "Created Maxio customer {CustomerId} for shopper {CustomerReference}.", created.Id, customerReference);
            return created;
        }
        catch (MaxioApiException ex) when (ex.IsValidationFailure)
        {
            // The most likely rejection is a reference that has just been taken by a concurrent
            // request - in that case the customer we wanted now exists.
            var raced = await ExecuteAsync(
                () => _client.ReadCustomerByReferenceAsync(customerReference, cancellationToken),
                "look up the billing customer");

            if (raced is not null)
            {
                _logger.LogInformation(
                    "Create customer for {CustomerReference} was rejected but the customer now exists as {CustomerId}; reusing it.",
                    customerReference, raced.Id);
                return raced;
            }

            _logger.LogWarning(ex, "Maxio rejected the customer for {CustomerReference}.", customerReference);
            throw new BillingValidationException(ex.Errors);
        }
        catch (Exception ex) when (ex is MaxioApiException or MaxioTransportException)
        {
            throw Translate(ex, "create the billing customer");
        }
    }

    private async Task<MaxioSubscription?> FindLiveSubscriptionAsync(int customerId, string planHandle, CancellationToken cancellationToken)
    {
        var subscriptions = await ExecuteAsync(
            () => _client.ListCustomerSubscriptionsAsync(customerId, cancellationToken),
            "list the subscriptions of the billing customer");

        return subscriptions
            .Where(subscription => SubscriptionStates.IsLive(subscription.State))
            .Where(subscription => string.Equals(subscription.Product?.Handle, planHandle, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(subscription => subscription.CreatedAt ?? DateTimeOffset.MinValue)
            .ThenByDescending(subscription => subscription.Id)
            .FirstOrDefault();
    }

    /// <summary>
    /// Builds the reference eShopOnWeb writes onto the subscription. It is derived from the shopper
    /// and the plan so it is recognisable, and is suffixed only when that natural reference is
    /// already taken by an earlier, ended subscription - Maxio requires references to be unique.
    /// </summary>
    private async Task<string> BuildSubscriptionReferenceAsync(string userName, string planHandle, CancellationToken cancellationToken)
    {
        var reference = $"{_settings.ReferencePrefix}:{Normalize(userName)}:{planHandle.ToLowerInvariant()}";

        var alreadyUsed = await ExecuteAsync(
            () => _client.FindSubscriptionByReferenceAsync(reference, cancellationToken),
            "look up an existing subscription reference");

        if (alreadyUsed is null)
        {
            return reference;
        }

        return $"{reference}:{DateTime.UtcNow.ToString("yyyyMMddHHmmssfff", CultureInfo.InvariantCulture)}";
    }

    /// <summary>
    /// The stable identity of an eShopOnWeb shopper inside Maxio. Derived from the user name, which
    /// survives restarts of the in-memory identity store, unlike generated user ids.
    /// </summary>
    private string BuildCustomerReference(string userName) =>
        $"{_settings.ReferencePrefix}:{Normalize(userName)}";

    private static string Normalize(string value) => value.Trim().ToLowerInvariant();

    /// <summary>
    /// Maxio requires a first and last name on a customer. Callers may supply them; otherwise they
    /// are derived from the email local part (for example <c>jane.doe@example.com</c> becomes
    /// <c>Jane Doe</c>).
    /// </summary>
    private static (string FirstName, string LastName) ResolveName(SubscribeCommand command, string email)
    {
        var firstName = command.FirstName?.Trim();
        var lastName = command.LastName?.Trim();

        if (!string.IsNullOrEmpty(firstName) && !string.IsNullOrEmpty(lastName))
        {
            return (firstName, lastName);
        }

        var localPart = email.Split('@')[0];
        var parts = localPart
            .Split(new[] { '.', '_', '-', '+' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(Capitalize)
            .ToArray();

        var derivedFirst = parts.Length > 0 ? parts[0] : "eShopOnWeb";
        var derivedLast = parts.Length > 1 ? string.Join(' ', parts.Skip(1)) : "Shopper";

        return (
            string.IsNullOrEmpty(firstName) ? derivedFirst : firstName,
            string.IsNullOrEmpty(lastName) ? derivedLast : lastName);
    }

    private static string Capitalize(string value) =>
        value.Length <= 1 ? value.ToUpperInvariant() : char.ToUpperInvariant(value[0]) + value[1..];

    private SubscriptionPlan MapPlan(MaxioProduct product, string? currency) => new()
    {
        Id = product.Id,
        Handle = product.Handle!,
        Name = product.Name ?? product.Handle!,
        Description = product.Description,
        PriceInCents = product.PriceInCents,
        Currency = currency ?? string.Empty,
        Interval = product.Interval,
        IntervalUnit = product.IntervalUnit ?? string.Empty,
        RequiresPaymentMethod = product.RequireCreditCard,
        TrialInterval = product.TrialInterval,
        TrialIntervalUnit = product.TrialIntervalUnit,
        TrialPriceInCents = product.TrialPriceInCents,
        ProductFamilyHandle = product.ProductFamily?.Handle ?? _settings.ProductFamilyHandle,
        PricePointName = product.ProductPricePointName
    };

    private static CustomerSubscription MapSubscription(MaxioSubscription subscription, string customerReference) => new()
    {
        Id = subscription.Id,
        Reference = subscription.Reference,
        State = subscription.State ?? string.Empty,
        PlanHandle = subscription.Product?.Handle ?? string.Empty,
        PlanName = subscription.Product?.Name ?? string.Empty,
        PriceInCents = subscription.ProductPriceInCents != 0
            ? subscription.ProductPriceInCents
            : subscription.Product?.PriceInCents ?? 0,
        Currency = subscription.Currency ?? string.Empty,
        Interval = subscription.Product?.Interval ?? 0,
        IntervalUnit = subscription.Product?.IntervalUnit ?? string.Empty,
        NextBillingAt = subscription.NextAssessmentAt,
        CurrentPeriodStartedAt = subscription.CurrentPeriodStartedAt,
        CurrentPeriodEndsAt = subscription.CurrentPeriodEndsAt,
        ActivatedAt = subscription.ActivatedAt,
        CanceledAt = subscription.CanceledAt,
        TrialEndedAt = subscription.TrialEndedAt,
        BalanceInCents = subscription.BalanceInCents,
        PaymentCollectionMethod = subscription.PaymentCollectionMethod,
        CustomerId = subscription.Customer?.Id ?? 0,
        CustomerReference = subscription.Customer?.Reference ?? customerReference,
        CustomerEmail = subscription.Customer?.Email
    };

    /// <summary>
    /// Reads the site currency, which plan payloads do not carry. A failure here is not worth
    /// failing the whole catalog for, so it degrades to an unset currency.
    /// </summary>
    private async Task<string?> GetSiteCurrencyAsync(CancellationToken cancellationToken)
    {
        if (_cache.TryGetValue(SiteCurrencyCacheKey, out string? cached))
        {
            return cached;
        }

        try
        {
            var site = await _client.ReadSiteAsync(cancellationToken);
            _cache.Set(SiteCurrencyCacheKey, site.Currency, SiteCurrencyCacheDuration);
            return site.Currency;
        }
        catch (Exception ex) when (ex is MaxioApiException or MaxioTransportException)
        {
            _logger.LogWarning(ex, "Could not read the Maxio site currency; plans will be reported without one.");
            return null;
        }
    }

    private void EnsureConfigured()
    {
        var errors = _settings.Validate();
        if (errors.Count > 0)
        {
            throw new BillingConfigurationException(
                "Maxio subscription billing is not configured: " + string.Join(" ", errors));
        }
    }

    private async Task<T> ExecuteAsync<T>(Func<Task<T>> operation, string description)
    {
        try
        {
            return await operation();
        }
        catch (Exception ex) when (ex is MaxioApiException or MaxioTransportException)
        {
            throw Translate(ex, description);
        }
    }

    /// <summary>
    /// Converts Maxio transport and protocol failures into the billing exceptions the API layer
    /// knows how to map onto HTTP status codes.
    /// </summary>
    private Exception Translate(Exception exception, string description)
    {
        switch (exception)
        {
            case MaxioApiException api when api.IsAuthenticationFailure:
                _logger.LogError(api, "Maxio rejected the credentials of this deployment while trying to {Description}.", description);
                return new BillingGatewayException(
                    $"Could not {description}: the billing system rejected this deployment's credentials.",
                    (int)api.StatusCode, api);

            case MaxioApiException api when api.IsValidationFailure:
                return new BillingValidationException(api.Errors);

            case MaxioApiException api:
                _logger.LogError(api, "Maxio returned {StatusCode} while trying to {Description}.", (int)api.StatusCode, description);
                return new BillingGatewayException(
                    $"Could not {description}: the billing system returned an error.", (int)api.StatusCode, api);

            case MaxioTransportException transport:
                _logger.LogError(transport, "Could not reach Maxio while trying to {Description}.", description);
                return new BillingGatewayException($"Could not {description}: the billing system is unreachable.", null, transport);

            default:
                return exception;
        }
    }
}
