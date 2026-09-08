using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.Infrastructure.Identity;
using Microsoft.eShopWeb.PublicApi.Maxio;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>The optional billing contact details a shopper may supply when subscribing.</summary>
public class SubscribeContact
{
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
}

/// <summary>The outcome of an (idempotent) subscribe attempt.</summary>
public class SubscriptionEnrollmentResult
{
    public SubscriptionEnrollmentResult(SubscriptionDto subscription, bool created)
    {
        Subscription = subscription;
        Created = created;
    }

    public SubscriptionDto Subscription { get; }

    /// <summary>True when this call created the subscription; false when it was already present.</summary>
    public bool Created { get; }
}

public interface ISubscriptionService
{
    Task<IReadOnlyList<SubscriptionPlanDto>> GetAvailablePlansAsync(CancellationToken cancellationToken);

    Task<SubscriptionEnrollmentResult> SubscribeAsync(ApplicationUser user,
        string planHandle,
        SubscribeContact? contact,
        string? idempotencyKey,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<SubscriptionDto>> GetSubscriptionsForUserAsync(ApplicationUser user, CancellationToken cancellationToken);
}

/// <summary>
/// Orchestrates Maxio subscription billing for eShopOnWeb users.
///
/// Maxio is the system of record. An eShop user maps to exactly one Maxio customer through a
/// deterministic reference ("eshop-{userId}"), so customers are never duplicated - even across
/// app restarts - and the shopper's subscriptions are always read back from Maxio by reference.
///
/// Subscribe is idempotent for the (user, plan) pair: an in-process keyed lock serializes
/// concurrent attempts and, once a subscription to that plan exists in a live state, further
/// calls return the existing subscription instead of creating a second one. Clients that supply
/// an idempotency key additionally get Maxio-side duplicate prevention (uniqueness_token).
/// </summary>
public class SubscriptionService : ISubscriptionService
{
    private const string CustomerReferencePrefix = "eshop-";
    private const string PaymentCollectionMethodRemittance = "remittance";
    private const string DefaultLastName = "Shopper";
    private const string Organization = "eShopOnWeb";

    private static readonly HashSet<string> TerminalStates = new(StringComparer.OrdinalIgnoreCase)
    {
        "canceled",
        "expired"
    };

    private readonly MaxioBillingClient _maxio;
    private readonly IOptions<MaxioOptions> _options;
    private readonly ILogger<SubscriptionService> _logger;

    // Process-wide (not per-service-instance) so concurrent HTTP requests serialize per key.
    // The service is registered scoped; a per-instance lock would let two requests race past
    // the find-or-create check and create duplicate subscriptions on a double-click.
    private static readonly KeyedAsyncLock SharedLocks = new();

    public SubscriptionService(MaxioBillingClient maxio,
        IOptions<MaxioOptions> options,
        ILogger<SubscriptionService> logger)
    {
        _maxio = maxio;
        _options = options;
        _logger = logger;
    }

    private string ProductFamilyHandle => _options.Value.ProductFamilyHandle;

    public async Task<IReadOnlyList<SubscriptionPlanDto>> GetAvailablePlansAsync(CancellationToken cancellationToken)
    {
        var products = await ListPlansInFamilyAsync(cancellationToken);
        return products
            .Where(p => p.ArchivedAt is null)
            .Select(ToPlanDto)
            .OrderBy(p => p.Price)
            .ToList();
    }

    public async Task<SubscriptionEnrollmentResult> SubscribeAsync(ApplicationUser user,
        string planHandle,
        SubscribeContact? contact,
        string? idempotencyKey,
        CancellationToken cancellationToken)
    {
        var plan = await FindPlanInFamilyAsync(planHandle, cancellationToken)
            ?? throw new SubscriptionPlanNotFoundException(
                $"The plan '{planHandle}' is not available. It is not an active plan in the configured Maxio product family.");

        var customerReference = BuildCustomerReference(user.Id);

        // Serialize the whole ensure-customer + find-or-create subscription for this (user, plan)
        // pair so a double-click cannot create two customers/subscriptions within this process.
        using var lease = await SharedLocks.AcquireAsync($"{customerReference}|{plan.Handle}", cancellationToken);

        var customer = await EnsureCustomerAsync(user, contact, customerReference, cancellationToken);

        var existing = await FindLiveSubscriptionForPlanAsync(customer.Id, plan.Handle!, cancellationToken);
        if (existing is not null)
        {
            return new SubscriptionEnrollmentResult(ToSubscriptionDto(existing), created: false);
        }

        var draft = new MaxioSubscriptionDraft
        {
            ProductHandle = plan.Handle,
            CustomerId = customer.Id,
            // The demo plans do not require a payment method; "remittance" lets the subscription
            // start with no stored card instead of Maxio rejecting the signup for lack of payment.
            PaymentCollectionMethod = PaymentCollectionMethodRemittance
        };

        MaxioSubscription created;
        try
        {
            created = await _maxio.CreateSubscriptionAsync(draft, idempotencyKey, cancellationToken);
        }
        catch (MaxioApiException ex) when (ex.StatusCode == HttpStatusCode.Conflict)
        {
            // A previous attempt with the same uniqueness token was already accepted by Maxio.
            // Converge on the existing subscription rather than reporting a conflict.
            _logger.LogInformation(
                "Maxio rejected a duplicate subscription submission for reference '{Reference}' and plan '{Plan}' (idempotent replay). Re-reading the subscription.",
                customerReference, plan.Handle);
            var replayed = await FindLiveSubscriptionForPlanAsync(customer.Id, plan.Handle!, cancellationToken);
            if (replayed is null)
            {
                throw;
            }

            return new SubscriptionEnrollmentResult(ToSubscriptionDto(replayed), created: false);
        }

        _logger.LogInformation(
            "Created Maxio subscription {SubscriptionId} ({State}) for eShop user '{UserId}' on plan '{Plan}'.",
            created.Id, created.State, user.Id, plan.Handle);

        return new SubscriptionEnrollmentResult(ToSubscriptionDto(created), created: true);
    }

    public async Task<IReadOnlyList<SubscriptionDto>> GetSubscriptionsForUserAsync(ApplicationUser user, CancellationToken cancellationToken)
    {
        var customer = await _maxio.LookupCustomerByReferenceAsync(BuildCustomerReference(user.Id), cancellationToken);
        if (customer is null)
        {
            return Array.Empty<SubscriptionDto>();
        }

        var subscriptions = await _maxio.ListSubscriptionsByCustomerAsync(customer.Id, cancellationToken);
        return subscriptions
            .Select(ToSubscriptionDto)
            .OrderByDescending(s => s.CreatedAt)
            .ToList();
    }

    // ------------------------------------------------------------------------

    private async Task<IReadOnlyList<MaxioProduct>> ListPlansInFamilyAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(ProductFamilyHandle))
        {
            throw new MaxioConfigurationException(
                "Maxio:ProductFamilyHandle is not configured. Export the MAXIO_DEFAULT_PRODUCT_FAMILY environment variable (or set Maxio:ProductFamilyHandle).");
        }

        try
        {
            return await _maxio.ListProductsByFamilyHandleAsync(ProductFamilyHandle, includeArchived: false, cancellationToken);
        }
        catch (MaxioApiException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            throw new MaxioConfigurationException(
                $"The configured Maxio product family '{ProductFamilyHandle}' was not found on site '{_options.Value.Subdomain}'. " +
                "Check MAXIO_DEFAULT_PRODUCT_FAMILY / Maxio:ProductFamilyHandle.", ex);
        }
    }

    private async Task<MaxioProduct?> FindPlanInFamilyAsync(string planHandle, CancellationToken cancellationToken)
    {
        var products = await ListPlansInFamilyAsync(cancellationToken);
        return products.FirstOrDefault(p =>
            string.Equals(p.Handle, planHandle, StringComparison.OrdinalIgnoreCase) && p.ArchivedAt is null);
    }

    private async Task<MaxioCustomer> EnsureCustomerAsync(ApplicationUser user,
        SubscribeContact? contact,
        string reference,
        CancellationToken cancellationToken)
    {
        var customer = await _maxio.LookupCustomerByReferenceAsync(reference, cancellationToken);
        if (customer is not null)
        {
            return customer;
        }

        var (firstName, lastName) = ResolveCustomerNames(user, contact);
        var draft = new MaxioCustomerDraft
        {
            FirstName = firstName,
            LastName = lastName,
            Email = ResolveCustomerEmail(user),
            Reference = reference,
            Organization = Organization
        };

        try
        {
            customer = await _maxio.CreateCustomerAsync(draft, uniquenessToken: null, cancellationToken);
        }
        catch (MaxioApiException ex) when (ex.StatusCode == HttpStatusCode.UnprocessableEntity)
        {
            // Reference is unique in Maxio: a validation failure here almost always means another
            // concurrent request created the same customer first. Re-read and converge.
            _logger.LogInformation(
                "Creating Maxio customer for reference '{Reference}' failed validation ({Errors}); re-reading in case a concurrent request created it.",
                reference, string.Join("; ", ex.Errors));
            customer = await _maxio.LookupCustomerByReferenceAsync(reference, cancellationToken);
            if (customer is null)
            {
                throw;
            }
        }

        if (customer.Id <= 0)
        {
            throw new MaxioApiException("Maxio did not return a usable customer id.");
        }

        return customer;
    }

    private async Task<MaxioSubscription?> FindLiveSubscriptionForPlanAsync(long customerId,
        string planHandle,
        CancellationToken cancellationToken)
    {
        var subscriptions = await _maxio.ListSubscriptionsByCustomerAsync(customerId, cancellationToken);
        return subscriptions.FirstOrDefault(s =>
            string.Equals(s.Product?.Handle, planHandle, StringComparison.OrdinalIgnoreCase)
            && !string.IsNullOrWhiteSpace(s.State)
            && !TerminalStates.Contains(s.State));
    }

    private static (string firstName, string lastName) ResolveCustomerNames(ApplicationUser user, SubscribeContact? contact)
    {
        var emailLocalPart = ResolveCustomerEmail(user).Split('@')[0];
        var firstName = string.IsNullOrWhiteSpace(contact?.FirstName)
            ? (string.IsNullOrWhiteSpace(emailLocalPart) ? user.UserName ?? user.Id : emailLocalPart)
            : contact!.FirstName!;
        var lastName = string.IsNullOrWhiteSpace(contact?.LastName) ? DefaultLastName : contact!.LastName!;
        return (firstName, lastName);
    }

    private static string ResolveCustomerEmail(ApplicationUser user)
    {
        if (!string.IsNullOrWhiteSpace(user.Email))
        {
            return user.Email!;
        }

        return !string.IsNullOrWhiteSpace(user.UserName) && user.UserName!.Contains('@')
            ? user.UserName!
            : throw new MaxioConfigurationException(
                $"The eShop user '{user.Id}' has no email address to create a Maxio customer with.");
    }

    private static string BuildCustomerReference(string eshopUserId) => CustomerReferencePrefix + eshopUserId;

    private static SubscriptionPlanDto ToPlanDto(MaxioProduct product) => new()
    {
        Handle = product.Handle,
        Name = product.Name,
        Description = product.Description,
        Price = MoneyFromCents(product.PriceInCents),
        Interval = product.Interval ?? 0,
        IntervalUnit = product.IntervalUnit
    };

    private static SubscriptionDto ToSubscriptionDto(MaxioSubscription subscription) => new()
    {
        Id = subscription.Id,
        State = subscription.State,
        PlanHandle = subscription.Product?.Handle,
        PlanName = subscription.Product?.Name,
        Price = MoneyFromCents(subscription.Product?.PriceInCents),
        Interval = subscription.Product?.Interval ?? 0,
        IntervalUnit = subscription.Product?.IntervalUnit,
        Currency = subscription.Currency,
        CurrentPeriodStartedAt = subscription.CurrentPeriodStartedAt,
        CurrentPeriodEndsAt = subscription.CurrentPeriodEndsAt,
        NextAssessmentAt = subscription.NextAssessmentAt,
        ActivatedAt = subscription.ActivatedAt,
        CreatedAt = subscription.CreatedAt,
        CanceledAt = subscription.CanceledAt,
        PaymentCollectionMethod = subscription.PaymentCollectionMethod
    };

    private static decimal MoneyFromCents(long? cents) => (cents ?? 0) / 100m;

    /// <summary>
    /// Provides mutual exclusion per key (used to make subscribe idempotent against double-clicks).
    /// </summary>
    private sealed class KeyedAsyncLock
    {
        private readonly ConcurrentDictionary<string, SemaphoreSlim> _gates = new(StringComparer.OrdinalIgnoreCase);

        public async Task<IDisposable> AcquireAsync(string key, CancellationToken cancellationToken)
        {
            var gate = _gates.GetOrAdd(key, static _ => new SemaphoreSlim(1, 1));
            await gate.WaitAsync(cancellationToken);
            return new Releaser(gate);
        }

        private sealed class Releaser : IDisposable
        {
            private readonly SemaphoreSlim _gate;
            private int _released;

            public Releaser(SemaphoreSlim gate)
            {
                _gate = gate;
            }

            public void Dispose()
            {
                if (Interlocked.Exchange(ref _released, 1) == 0)
                {
                    _gate.Release();
                }
            }
        }
    }
}
