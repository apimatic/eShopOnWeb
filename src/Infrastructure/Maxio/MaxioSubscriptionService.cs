using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Maxio.Models;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Subscription billing backed by Maxio Advanced Billing.
/// <para>
/// eShopOnWeb stores no billing state of its own: Maxio is the system of record, and the link
/// between the two is a deterministic <c>reference</c> value derived from the eShopOnWeb
/// identity. That is what makes subscribing idempotent without a local database - a repeated
/// call finds the customer and the subscription that a previous call created, and Maxio's own
/// uniqueness constraint on references settles any race we lose.
/// </para>
/// </summary>
public class MaxioSubscriptionService : ISubscriptionService
{
    private const string CacheKeyPrefix = "maxio:plans:";

    private readonly IMaxioApiClient _client;
    private readonly IOptionsMonitor<MaxioOptions> _options;
    private readonly IMemoryCache _cache;
    private readonly KeyedAsyncLock _subscriberLock;
    private readonly ILogger<MaxioSubscriptionService> _logger;

    public MaxioSubscriptionService(
        IMaxioApiClient client,
        IOptionsMonitor<MaxioOptions> options,
        IMemoryCache cache,
        KeyedAsyncLock subscriberLock,
        ILogger<MaxioSubscriptionService> logger)
    {
        _client = client;
        _options = options;
        _cache = cache;
        _subscriberLock = subscriberLock;
        _logger = logger;
    }

    public async Task<IReadOnlyCollection<SubscriptionPlan>> ListPlansAsync(CancellationToken cancellationToken = default)
    {
        var options = GetOptions();
        var familyHandle = options.ProductFamilyHandle!;
        var cacheKey = CacheKeyPrefix + familyHandle;

        if (options.PlanCacheSeconds > 0 && _cache.TryGetValue(cacheKey, out IReadOnlyCollection<SubscriptionPlan>? cached) && cached is not null)
        {
            return cached;
        }

        var plans = await LoadPlansAsync(familyHandle, cancellationToken).ConfigureAwait(false);

        if (options.PlanCacheSeconds > 0)
        {
            _cache.Set(cacheKey, plans, TimeSpan.FromSeconds(options.PlanCacheSeconds));
        }

        return plans;
    }

    public async Task<SubscribeResult> SubscribeAsync(SubscribeRequest request, CancellationToken cancellationToken = default)
    {
        var options = GetOptions();
        var subscriber = request.Subscriber;
        var planHandle = request.PlanHandle ?? options.DefaultPlanHandle;

        if (string.IsNullOrWhiteSpace(planHandle))
        {
            throw new ArgumentException(
                $"A plan handle is required because no default plan is configured ('{MaxioOptions.SectionName}:{nameof(MaxioOptions.DefaultPlanHandle)}').",
                nameof(request));
        }

        var plan = await FindPlanAsync(planHandle!, cancellationToken).ConfigureAwait(false)
            ?? throw new SubscriptionPlanNotFoundException(planHandle!);

        var customerReference = BuildCustomerReference(options, subscriber);

        // Serialise a shopper's concurrent attempts so the common double click does not turn
        // into two round trips that both believe they are the first.
        using (await _subscriberLock.LockAsync(customerReference, cancellationToken).ConfigureAwait(false))
        {
            var customer = await EnsureCustomerAsync(options, subscriber, customerReference, cancellationToken).ConfigureAwait(false);
            return await EnrollAsync(options, subscriber, customer, plan, request.IdempotencyKey, cancellationToken).ConfigureAwait(false);
        }
    }

    public async Task<IReadOnlyCollection<CustomerSubscription>> ListSubscriptionsAsync(Subscriber subscriber, CancellationToken cancellationToken = default)
    {
        var options = GetOptions();
        var customerReference = BuildCustomerReference(options, subscriber);

        var customer = await ExecuteAsync(
            () => _client.ReadCustomerByReferenceAsync(customerReference, cancellationToken),
            "look up the billing customer").ConfigureAwait(false);

        if (customer is null)
        {
            // The shopper has never subscribed, so there is nothing to report.
            return Array.Empty<CustomerSubscription>();
        }

        var subscriptions = await ExecuteAsync(
            () => _client.ListCustomerSubscriptionsAsync(customer.Id, cancellationToken),
            "list the customer's subscriptions").ConfigureAwait(false);

        return subscriptions
            .Select(ToCustomerSubscription)
            .OrderByDescending(subscription => subscription.CreatedAt ?? DateTimeOffset.MinValue)
            .ToList();
    }

    private async Task<IReadOnlyCollection<SubscriptionPlan>> LoadPlansAsync(string familyHandle, CancellationToken cancellationToken)
    {
        // The spec allows a product family to be addressed by handle using the "handle:" prefix;
        // handles are stable across sites and re-seeds, numeric ids are not.
        var products = await ExecuteAsync(
            () => _client.ListProductsForProductFamilyAsync($"handle:{familyHandle}", includeArchived: false, cancellationToken),
            "list the subscription plans").ConfigureAwait(false);

        return products
            .Where(product => product.ArchivedAt is null && !string.IsNullOrWhiteSpace(product.Handle))
            .Select(ToSubscriptionPlan)
            .OrderBy(plan => plan.PriceInCents)
            .ToList();
    }

    private async Task<SubscriptionPlan?> FindPlanAsync(string planHandle, CancellationToken cancellationToken)
    {
        var plans = await ListPlansAsync(cancellationToken).ConfigureAwait(false);
        return plans.FirstOrDefault(plan => string.Equals(plan.Handle, planHandle, StringComparison.OrdinalIgnoreCase));
    }

    private async Task<MaxioCustomer> EnsureCustomerAsync(
        MaxioOptions options,
        Subscriber subscriber,
        string customerReference,
        CancellationToken cancellationToken)
    {
        var existing = await ExecuteAsync(
            () => _client.ReadCustomerByReferenceAsync(customerReference, cancellationToken),
            "look up the billing customer").ConfigureAwait(false);

        if (existing is not null)
        {
            return existing;
        }

        var (firstName, lastName) = SplitName(subscriber);
        var newCustomer = new MaxioCreateCustomer
        {
            FirstName = firstName,
            LastName = lastName,
            Email = subscriber.Email,
            Reference = customerReference
        };

        try
        {
            var created = await _client.CreateCustomerAsync(newCustomer, cancellationToken).ConfigureAwait(false);
            _logger.LogInformation(
                "Created Maxio customer {CustomerId} for reference {CustomerReference}.",
                created.Id,
                customerReference);
            return created;
        }
        catch (MaxioApiException ex) when (ex.IsDuplicateReference)
        {
            // Another request created the customer first: adopt it instead of failing.
            _logger.LogInformation(
                "Maxio customer for reference {CustomerReference} already existed; reusing it.",
                customerReference);

            var winner = await ExecuteAsync(
                () => _client.ReadCustomerByReferenceAsync(customerReference, cancellationToken),
                "look up the billing customer").ConfigureAwait(false);

            return winner ?? throw Translate(ex, "create the billing customer");
        }
        catch (MaxioApiException ex)
        {
            throw Translate(ex, "create the billing customer");
        }
    }

    private async Task<SubscribeResult> EnrollAsync(
        MaxioOptions options,
        Subscriber subscriber,
        MaxioCustomer customer,
        SubscriptionPlan plan,
        string? idempotencyKey,
        CancellationToken cancellationToken)
    {
        var reference = BuildSubscriptionReference(options, subscriber, plan.Handle, idempotencyKey);

        var existing = await ExecuteAsync(
            () => _client.FindSubscriptionAsync(reference, cancellationToken),
            "look up an existing subscription").ConfigureAwait(false);

        if (existing is not null)
        {
            // A caller supplied key identifies one specific operation, so replaying it always
            // returns that same subscription. Otherwise only a still-live subscription counts
            // as "already subscribed" - an ended one may be started again.
            if (idempotencyKey is not null || SubscriptionStates.IsLive(existing.State))
            {
                _logger.LogInformation(
                    "Subscribe request for reference {SubscriptionReference} matched existing subscription {SubscriptionId} ({State}).",
                    reference,
                    existing.Id,
                    existing.State);

                return new SubscribeResult { Subscription = ToCustomerSubscription(existing), AlreadySubscribed = true };
            }

            // The reference belongs to a subscription that has ended, so this shopper may start
            // a new one - but Maxio enforces reference uniqueness site-wide, hence the suffix.
            reference = $"{reference}:{ShortId()}";
        }

        if (idempotencyKey is null)
        {
            // Safety net for a subscription created outside this reference scheme (for example
            // directly in the Maxio UI): never enroll a shopper twice on the same plan.
            var live = await FindLiveSubscriptionForPlanAsync(customer.Id, plan.Handle, cancellationToken).ConfigureAwait(false);
            if (live is not null)
            {
                return new SubscribeResult { Subscription = ToCustomerSubscription(live), AlreadySubscribed = true };
            }
        }

        var payload = new MaxioCreateSubscription
        {
            ProductHandle = plan.Handle,
            CustomerId = customer.Id,
            Reference = reference,
            PaymentCollectionMethod = options.PaymentCollectionMethod
        };

        try
        {
            var created = await _client.CreateSubscriptionAsync(payload, cancellationToken).ConfigureAwait(false);
            _logger.LogInformation(
                "Created Maxio subscription {SubscriptionId} on plan {PlanHandle} for customer {CustomerId}.",
                created.Id,
                plan.Handle,
                customer.Id);

            return new SubscribeResult { Subscription = ToCustomerSubscription(created), AlreadySubscribed = false };
        }
        catch (MaxioApiException ex) when (ex.IsDuplicateReference)
        {
            // We lost a race against an identical request; the winner's subscription is the
            // one and only result of both calls.
            var winner = await ExecuteAsync(
                () => _client.FindSubscriptionAsync(reference, cancellationToken),
                "look up an existing subscription").ConfigureAwait(false);

            if (winner is null)
            {
                throw Translate(ex, "create the subscription");
            }

            _logger.LogInformation(
                "Subscribe request lost a race for reference {SubscriptionReference}; returning subscription {SubscriptionId}.",
                reference,
                winner.Id);

            return new SubscribeResult { Subscription = ToCustomerSubscription(winner), AlreadySubscribed = true };
        }
        catch (MaxioApiException ex)
        {
            throw Translate(ex, "create the subscription");
        }
    }

    private async Task<MaxioSubscription?> FindLiveSubscriptionForPlanAsync(int customerId, string planHandle, CancellationToken cancellationToken)
    {
        var subscriptions = await ExecuteAsync(
            () => _client.ListCustomerSubscriptionsAsync(customerId, cancellationToken),
            "list the customer's subscriptions").ConfigureAwait(false);

        return subscriptions.FirstOrDefault(subscription =>
            SubscriptionStates.IsLive(subscription.State) &&
            string.Equals(subscription.Product?.Handle, planHandle, StringComparison.OrdinalIgnoreCase));
    }

    private MaxioOptions GetOptions()
    {
        var options = _options.CurrentValue;
        var errors = options.Validate();
        if (errors.Count > 0)
        {
            throw new BillingConfigurationException("Maxio subscription billing is not configured.", errors);
        }

        return options;
    }

    private static string BuildCustomerReference(MaxioOptions options, Subscriber subscriber) =>
        $"{options.ReferencePrefix}:user:{Normalize(subscriber.UserName)}";

    private static string BuildSubscriptionReference(MaxioOptions options, Subscriber subscriber, string planHandle, string? idempotencyKey) =>
        idempotencyKey is null
            ? $"{options.ReferencePrefix}:sub:{Normalize(subscriber.UserName)}:{Normalize(planHandle)}"
            : $"{options.ReferencePrefix}:sub:{Normalize(subscriber.UserName)}:key:{idempotencyKey.Trim()}";

    private static string Normalize(string value) => value.Trim().ToLowerInvariant();

    private static string ShortId() => Guid.NewGuid().ToString("N").Substring(0, 8);

    /// <summary>
    /// Maxio requires a first and last name. eShopOnWeb only knows an email address, so derive
    /// something sensible from it rather than sending placeholders.
    /// </summary>
    private static (string FirstName, string LastName) SplitName(Subscriber subscriber)
    {
        if (!string.IsNullOrWhiteSpace(subscriber.FirstName) || !string.IsNullOrWhiteSpace(subscriber.LastName))
        {
            return (
                string.IsNullOrWhiteSpace(subscriber.FirstName) ? "eShopOnWeb" : subscriber.FirstName!.Trim(),
                string.IsNullOrWhiteSpace(subscriber.LastName) ? "Customer" : subscriber.LastName!.Trim());
        }

        var localPart = subscriber.Email.Split('@')[0];
        var parts = localPart.Split(new[] { '.', '_', '-', '+' }, StringSplitOptions.RemoveEmptyEntries);

        return parts.Length switch
        {
            0 => ("eShopOnWeb", "Customer"),
            1 => (TitleCase(parts[0]), "Customer"),
            _ => (TitleCase(parts[0]), TitleCase(string.Join(" ", parts.Skip(1))))
        };
    }

    private static string TitleCase(string value) =>
        CultureInfo.InvariantCulture.TextInfo.ToTitleCase(value.ToLowerInvariant());

    private static SubscriptionPlan ToSubscriptionPlan(MaxioProduct product) => new()
    {
        Handle = product.Handle!,
        Name = product.Name ?? product.Handle!,
        Description = product.Description,
        PriceInCents = product.PriceInCents,
        Interval = product.Interval,
        IntervalUnit = product.IntervalUnit,
        ProductFamilyHandle = product.ProductFamily?.Handle,
        RequiresPaymentMethod = product.RequireCreditCard,
        TrialInterval = product.TrialInterval,
        TrialIntervalUnit = product.TrialIntervalUnit,
        TrialPriceInCents = product.TrialPriceInCents
    };

    private static CustomerSubscription ToCustomerSubscription(MaxioSubscription subscription) => new()
    {
        Id = subscription.Id,
        Reference = subscription.Reference,
        State = subscription.State ?? "unknown",
        PlanHandle = subscription.Product?.Handle,
        PlanName = subscription.Product?.Name,
        PriceInCents = subscription.ProductPriceInCents,
        Currency = subscription.Currency,
        Interval = subscription.Product?.Interval ?? 0,
        IntervalUnit = subscription.Product?.IntervalUnit,
        CurrentPeriodStartedAt = subscription.CurrentPeriodStartedAt,
        CurrentPeriodEndsAt = subscription.CurrentPeriodEndsAt,
        NextBillingAt = subscription.NextAssessmentAt,
        ActivatedAt = subscription.ActivatedAt,
        CanceledAt = subscription.CanceledAt,
        CreatedAt = subscription.CreatedAt,
        CustomerId = subscription.Customer?.Id ?? 0,
        CustomerReference = subscription.Customer?.Reference,
        PaymentCollectionMethod = subscription.PaymentCollectionMethod
    };

    private async Task<T> ExecuteAsync<T>(Func<Task<T>> call, string description)
    {
        try
        {
            return await call().ConfigureAwait(false);
        }
        catch (MaxioApiException ex)
        {
            throw Translate(ex, description);
        }
    }

    private BillingProviderException Translate(MaxioApiException exception, string description)
    {
        _logger.LogError(
            exception,
            "Maxio rejected the attempt to {Description} with status {StatusCode}.",
            description,
            (int)exception.StatusCode);

        return new BillingProviderException(
            $"The billing provider could not {description}.",
            exception.Errors,
            exception)
        {
            IsRequestRejected = exception.IsClientError
        };
    }
}
