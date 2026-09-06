using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.ErrorResponse;
using MaxioAdvancedBilling.Core.Exceptions;
using MaxioAdvancedBilling.Errors;
using MaxioAdvancedBilling.Models;
using Microsoft.eShopWeb.ApplicationCore.Billing;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Maxio Advanced Billing implementation of <see cref="ISubscriptionBillingService"/>.
/// </summary>
/// <remarks>
/// Maxio is the system of record: eShopOnWeb stores nothing about subscriptions and re-reads them
/// from Maxio on every request. Idempotency therefore rests on two deterministic keys rather than on
/// local state — the customer <c>reference</c> derived from the authenticated user, and a
/// subscription <c>reference</c> derived from (user, plan).
/// </remarks>
public class MaxioSubscriptionBillingService : ISubscriptionBillingService
{
    /// <summary>
    /// States in which a subscription no longer occupies the shopper's slot for a plan, so
    /// subscribing again is a genuinely new enrollment. Anything else — including a state this build
    /// does not recognise — counts as live, because refusing to create a duplicate is the safe error.
    /// </summary>
    private static readonly HashSet<string> TerminalStates = new(StringComparer.OrdinalIgnoreCase)
    {
        "canceled",
        "expired",
        "failed_to_create",
        "trial_ended"
    };

    private readonly MaxioAdvancedBillingClient _client;
    private readonly MaxioProductFamilyResolver _familyResolver;
    private readonly MaxioSiteResolver _siteResolver;
    private readonly KeyedAsyncLock _subscribeLock;
    private readonly MaxioSettings _settings;
    private readonly ILogger<MaxioSubscriptionBillingService> _logger;

    public MaxioSubscriptionBillingService(MaxioAdvancedBillingClient client,
        MaxioProductFamilyResolver familyResolver,
        MaxioSiteResolver siteResolver,
        KeyedAsyncLock subscribeLock,
        IOptions<MaxioSettings> settings,
        ILogger<MaxioSubscriptionBillingService> logger)
    {
        _client = client;
        _familyResolver = familyResolver;
        _siteResolver = siteResolver;
        _subscribeLock = subscribeLock;
        _settings = settings.Value;
        _logger = logger;
    }

    public async Task<IReadOnlyList<SubscriptionPlan>> ListPlansAsync(CancellationToken cancellationToken = default)
    {
        EnsureConfigured();

        return await BoundedAsync(async ct =>
        {
            var products = await ListFamilyProductsAsync(ct);
            return (IReadOnlyList<SubscriptionPlan>)products
                .Where(product => product.ArchivedAt is null && !string.IsNullOrWhiteSpace(product.Handle))
                .Select(ToPlan)
                .OrderBy(plan => plan.PriceInCents)
                .ToList();
        }, cancellationToken);
    }

    public async Task<SubscribeResult> SubscribeAsync(BillingCustomerIdentity identity,
        string? planHandle,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(identity);
        EnsureConfigured();

        var requestedHandle = string.IsNullOrWhiteSpace(planHandle)
            ? _settings.DefaultPlanHandle
            : planHandle!.Trim();

        if (string.IsNullOrWhiteSpace(requestedHandle))
        {
            throw MaxioFailures.Rejected(
                "No plan was requested and no default plan is configured for this environment.");
        }

        return await BoundedAsync(async ct =>
        {
            // Only plans in the configured family may be subscribed to; an arbitrary site-wide
            // handle must not become a subscription just because it exists somewhere on the site.
            var plan = await FindPlanAsync(requestedHandle!, ct);

            using var serialized = await AcquireSubscribeLockAsync(identity.Reference, ct);

            var customer = await EnsureCustomerAsync(identity, ct);
            var customerId = RequireCustomerId(customer);

            var existing = await FindLiveSubscriptionAsync(customerId, plan.Handle, ct);
            if (existing is not null)
            {
                _logger.LogInformation(
                    "Maxio subscription {SubscriptionId} already exists for {Reference} on plan {PlanHandle}; not creating another.",
                    existing.Id, identity.Reference, plan.Handle);

                return SubscribeResult.Existing(existing);
            }

            var created = await CreateSubscriptionAsync(identity, customerId, plan.Handle, ct);

            _logger.LogInformation("Created Maxio subscription {SubscriptionId} for {Reference} on plan {PlanHandle}.",
                created.Subscription.Id, identity.Reference, plan.Handle);

            return created;
        }, cancellationToken);
    }

    public async Task<IReadOnlyList<CustomerSubscription>> ListSubscriptionsAsync(BillingCustomerIdentity identity,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(identity);
        EnsureConfigured();

        return await BoundedAsync(async ct =>
        {
            var customer = await ReadCustomerByReferenceAsync(identity.Reference, ct);
            if (customer?.Id is null)
            {
                // No provider customer yet simply means the shopper has never subscribed.
                return (IReadOnlyList<CustomerSubscription>)Array.Empty<CustomerSubscription>();
            }

            var subscriptions = await ListCustomerSubscriptionsAsync(customer.Id.Value, ct);

            return subscriptions
                .Select(ToCustomerSubscription)
                .OrderByDescending(subscription => subscription.CreatedAt ?? DateTimeOffset.MinValue)
                .ToList();
        }, cancellationToken);
    }

    private void EnsureConfigured()
    {
        if (!_settings.IsConfigured)
        {
            throw MaxioFailures.NotConfigured();
        }
    }

    /// <summary>
    /// Gives every provider operation one whole-call budget, linked to the caller's own token so a
    /// disconnected client also stops the outbound work. The SDK's own timeouts bound a single
    /// attempt; only this bounds the call the caller is actually waiting on.
    /// </summary>
    private async Task<T> BoundedAsync<T>(Func<CancellationToken, Task<T>> call, CancellationToken cancellationToken)
    {
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        budget.CancelAfter(TimeSpan.FromSeconds(Math.Max(1, _settings.RequestTimeoutSeconds)));

        try
        {
            return await call(budget.Token);
        }
        catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            throw MaxioFailures.Unavailable("waiting for Maxio", ex);
        }
    }

    private async Task<IDisposable> AcquireSubscribeLockAsync(string reference, CancellationToken cancellationToken)
    {
        var waited = await _subscribeLock.AcquireAsync(reference,
            TimeSpan.FromSeconds(Math.Max(1, _settings.RequestTimeoutSeconds)),
            cancellationToken);

        if (waited is null)
        {
            throw new SubscriptionBillingException(BillingFailureKind.Conflict,
                "Another subscribe request for this account is still in progress.");
        }

        return waited;
    }

    private async Task<SubscriptionPlan> FindPlanAsync(string handle, CancellationToken cancellationToken)
    {
        var products = await ListFamilyProductsAsync(cancellationToken);

        var product = products.FirstOrDefault(candidate =>
            string.Equals(candidate.Handle, handle, StringComparison.OrdinalIgnoreCase));

        if (product is null)
        {
            throw MaxioFailures.NotFound(
                $"No subscription plan with handle '{handle}' is offered by this store.");
        }

        if (product.ArchivedAt is not null)
        {
            throw MaxioFailures.Rejected($"The subscription plan '{handle}' is archived and cannot be subscribed to.");
        }

        return ToPlan(product);
    }

    /// <summary>
    /// Walks the configured family's products. The operation returns a bare list with no total and
    /// no next-page marker, so the only stop condition is a short page; the page cap keeps a
    /// provider that never returns one from looping forever.
    /// </summary>
    private async Task<IReadOnlyList<Product>> ListFamilyProductsAsync(CancellationToken cancellationToken)
    {
        const string operation = "listing subscription plans";

        var familyId = await _familyResolver.ResolveFamilyIdAsync(cancellationToken);
        var pageSize = Math.Clamp(_settings.PlanPageSize, 1, 200);
        var maxPages = Math.Max(1, _settings.MaxPlanPages);
        var products = new List<Product>();

        for (var page = 1; page <= maxPages; page++)
        {
            IReadOnlyList<ProductResponse> pageResults;
            try
            {
                pageResults = await _client.ProductFamilies.ListProductsForProductFamily(
                    productFamilyId: familyId,
                    dateField: null,
                    filter: null,
                    startDate: null,
                    endDate: null,
                    startDatetime: null,
                    endDatetime: null,
                    includeArchived: false,
                    include: null,
                    page: page,
                    perPage: pageSize,
                    ct: cancellationToken);
            }
            catch (SdkException<ListProductsForProductFamilyError> ex)
            {
                if (ex.Error.TryGetString(out var notFound))
                {
                    throw MaxioFailures.NotFound(
                        $"The configured Maxio product family is no longer available ({MaxioFailures.Truncate(notFound)}).");
                }

                if (ex.Error.TryGetRawError(out var raw))
                {
                    throw MaxioFailures.FromRawError(raw, operation, ex);
                }

                throw MaxioFailures.UnreadableRejection(operation, ex);
            }
            catch (JsonException ex)
            {
                throw MaxioFailures.UnreadableResponse(operation, ex);
            }
            catch (Exception ex) when (MaxioFailures.IsTransportFailure(ex))
            {
                throw MaxioFailures.Unavailable(operation, ex);
            }

            products.AddRange(pageResults.Select(response => response.Product));

            if (pageResults.Count < pageSize)
            {
                return products;
            }
        }

        _logger.LogWarning(
            "Stopped listing Maxio plans after {MaxPages} pages of {PageSize}; the plan list may be truncated.",
            maxPages, pageSize);

        return products;
    }

    private async Task<Customer> EnsureCustomerAsync(BillingCustomerIdentity identity, CancellationToken cancellationToken)
    {
        var existing = await ReadCustomerByReferenceAsync(identity.Reference, cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        return await CreateCustomerAsync(identity, cancellationToken);
    }

    /// <summary>
    /// Looks a customer up by its external reference. Returns null only for an explicit 404 — an
    /// unreadable response is not turned into "no such customer", because that would convert a
    /// corrupt reply into a spurious second customer.
    /// </summary>
    private async Task<Customer?> ReadCustomerByReferenceAsync(string reference, CancellationToken cancellationToken)
    {
        const string operation = "looking up the billing customer";

        try
        {
            var response = await _client.Customers.ReadCustomerByReference(reference, ct: cancellationToken);
            return response.Customer;
        }
        catch (SdkException<RawError> ex) when (ex.Error.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }
        catch (SdkException<RawError> ex)
        {
            throw MaxioFailures.FromRawError(ex.Error, operation, ex);
        }
        catch (JsonException ex)
        {
            throw MaxioFailures.UnreadableResponse(operation, ex);
        }
        catch (Exception ex) when (MaxioFailures.IsTransportFailure(ex))
        {
            throw MaxioFailures.Unavailable(operation, ex);
        }
    }

    /// <summary>
    /// Creates the provider customer. Maxio enforces that a reference is used by at most one
    /// customer, so every ambiguous outcome — a 422, an unreadable body, a refused re-send — is
    /// settled by re-reading the customer rather than by guessing.
    /// </summary>
    private async Task<Customer> CreateCustomerAsync(BillingCustomerIdentity identity, CancellationToken cancellationToken)
    {
        const string operation = "creating the billing customer";

        var body = new CreateCustomerRequest
        {
            Customer = new CreateCustomer
            {
                FirstName = identity.FirstName,
                LastName = identity.LastName,
                Email = identity.Email,
                Reference = identity.Reference
            }
        };

        try
        {
            using (SingleSendScope.Begin())
            {
                var response = await _client.Customers.CreateCustomer(body, ct: cancellationToken);
                return response.Customer;
            }
        }
        catch (SdkException<CreateCustomerError> ex)
        {
            if (ex.Error.TryGetCustomerErrorResponse1(out var validation))
            {
                // The overwhelmingly likely 422 here is "that reference is taken", i.e. a concurrent
                // request won the race — so look again before calling it a failure.
                var raced = await ReadCustomerByReferenceAsync(identity.Reference, cancellationToken);
                if (raced is not null)
                {
                    return raced;
                }

                throw MaxioFailures.Rejected(
                    $"Maxio rejected the billing customer ({DescribeValidation(validation)}).",
                    System.Net.HttpStatusCode.UnprocessableEntity);
            }

            if (ex.Error.TryGetRawError(out var raw))
            {
                throw MaxioFailures.FromRawError(raw, operation, ex);
            }

            throw MaxioFailures.UnreadableRejection(operation, ex);
        }
        catch (JsonException ex)
        {
            // Either an unreadable success body or a 422 whose shape the SDK could not parse (which
            // destroys the status). Both are settled the same way: re-read by reference.
            var reconciled = await ReadCustomerByReferenceAsync(identity.Reference, cancellationToken);
            if (reconciled is not null)
            {
                return reconciled;
            }

            throw MaxioFailures.UnreadableRejection(operation, ex);
        }
        catch (DuplicateSendRefusedException ex)
        {
            var reconciled = await ReadCustomerByReferenceAsync(identity.Reference, cancellationToken);
            if (reconciled is not null)
            {
                return reconciled;
            }

            throw MaxioFailures.UnknownWriteOutcome(operation, ex);
        }
        catch (Exception ex) when (MaxioFailures.IsTransportFailure(ex))
        {
            var reconciled = await ReadCustomerByReferenceAsync(identity.Reference, cancellationToken);
            if (reconciled is not null)
            {
                return reconciled;
            }

            throw MaxioFailures.Unavailable(operation, ex);
        }
    }

    private async Task<SubscribeResult> CreateSubscriptionAsync(BillingCustomerIdentity identity,
        int customerId,
        string planHandle,
        CancellationToken cancellationToken)
    {
        const string operation = "creating the subscription";

        var body = new CreateSubscriptionRequest
        {
            Subscription = new CreateSubscription
            {
                ProductHandle = planHandle,
                CustomerId = customerId,

                // Without this Maxio tries to charge a card for the balance that falls due at
                // signup, and refuses because the shopper has no payment profile. Invoicing the
                // balance is what lets the flow complete without card capture or 3-DS.
                PaymentCollectionMethod = await _siteResolver.ResolveCollectionMethodAsync(cancellationToken),

                // Deterministic per (shopper, plan): makes a subscription created by a request whose
                // response never arrived recognisable afterwards.
                Reference = identity.SubscriptionReferenceFor(planHandle)
            }
        };

        try
        {
            using (SingleSendScope.Begin())
            {
                var response = await _client.Subscriptions.CreateSubscription(body, ct: cancellationToken);
                if (response.Subscription is null)
                {
                    throw MaxioFailures.UnreadableResponse(operation,
                        new InvalidOperationException("Maxio returned no subscription payload."));
                }

                return SubscribeResult.Created(ToCustomerSubscription(response.Subscription));
            }
        }
        catch (SdkException<CreateSubscriptionError> ex)
        {
            if (ex.Error.TryGetErrorListResponse1(out var validation))
            {
                var detail = MaxioFailures.Truncate(string.Join("; ", validation.Errors));
                throw MaxioFailures.Rejected(
                    detail is null
                        ? "Maxio rejected the subscription."
                        : $"Maxio rejected the subscription: {detail}",
                    System.Net.HttpStatusCode.UnprocessableEntity);
            }

            if (ex.Error.TryGetRawError(out var raw))
            {
                throw MaxioFailures.FromRawError(raw, operation, ex);
            }

            throw MaxioFailures.UnreadableRejection(operation, ex);
        }
        catch (JsonException ex)
        {
            return await ReconcileSubscriptionAsync(customerId, planHandle, operation, ex,
                MaxioFailures.UnreadableRejection, cancellationToken);
        }
        catch (DuplicateSendRefusedException ex)
        {
            return await ReconcileSubscriptionAsync(customerId, planHandle, operation, ex,
                MaxioFailures.UnknownWriteOutcome, cancellationToken);
        }
        catch (Exception ex) when (MaxioFailures.IsTransportFailure(ex))
        {
            return await ReconcileSubscriptionAsync(customerId, planHandle, operation, ex,
                MaxioFailures.Unavailable, cancellationToken);
        }
    }

    /// <summary>
    /// Settles an ambiguous create by re-reading the customer's subscriptions: the write may well
    /// have taken effect even though its outcome never came back.
    /// </summary>
    private async Task<SubscribeResult> ReconcileSubscriptionAsync(int customerId,
        string planHandle,
        string operation,
        Exception cause,
        Func<string, Exception, SubscriptionBillingException> onStillUnknown,
        CancellationToken cancellationToken)
    {
        _logger.LogWarning(cause,
            "Maxio subscription create for customer {CustomerId} on plan {PlanHandle} did not confirm; reconciling.",
            customerId, planHandle);

        var reconciled = await FindLiveSubscriptionAsync(customerId, planHandle, cancellationToken);
        if (reconciled is not null)
        {
            return SubscribeResult.Created(reconciled);
        }

        throw onStillUnknown(operation, cause);
    }

    private async Task<CustomerSubscription?> FindLiveSubscriptionAsync(int customerId,
        string planHandle,
        CancellationToken cancellationToken)
    {
        var subscriptions = await ListCustomerSubscriptionsAsync(customerId, cancellationToken);

        return subscriptions
            .Select(ToCustomerSubscription)
            .Where(subscription => subscription.IsLive
                && string.Equals(subscription.PlanHandle, planHandle, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(subscription => subscription.CreatedAt ?? DateTimeOffset.MinValue)
            .FirstOrDefault();
    }

    /// <summary>
    /// Lists a customer's subscriptions. This operation exposes no paging and no filters, and it is
    /// the only customer-scoped listing Maxio offers, so state and plan are filtered here.
    /// </summary>
    private async Task<IReadOnlyList<Subscription>> ListCustomerSubscriptionsAsync(int customerId,
        CancellationToken cancellationToken)
    {
        const string operation = "listing the customer's subscriptions";

        try
        {
            var responses = await _client.Customers.ListCustomerSubscriptions(customerId, ct: cancellationToken);

            return responses
                .Select(response => response.Subscription)
                .Where(subscription => subscription is not null)
                .Select(subscription => subscription!)
                .ToList();
        }
        catch (SdkException<RawError> ex)
        {
            throw MaxioFailures.FromRawError(ex.Error, operation, ex);
        }
        catch (JsonException ex)
        {
            throw MaxioFailures.UnreadableResponse(operation, ex);
        }
        catch (Exception ex) when (MaxioFailures.IsTransportFailure(ex))
        {
            throw MaxioFailures.Unavailable(operation, ex);
        }
    }

    private static int RequireCustomerId(Customer customer)
    {
        if (customer.Id is null)
        {
            throw MaxioFailures.UnreadableResponse("reading the billing customer",
                new InvalidOperationException("Maxio returned a customer without an id."));
        }

        return customer.Id.Value;
    }

    private static SubscriptionPlan ToPlan(Product product) => new(
        handle: product.Handle ?? string.Empty,
        name: product.Name ?? product.Handle ?? string.Empty,
        description: product.Description,
        priceInCents: product.PriceInCents ?? 0,
        intervalLength: product.Interval ?? 0,
        intervalUnit: product.IntervalUnit?.Value ?? string.Empty,
        // require_credit_card is the live flag; request_credit_card is deprecated and ignored here.
        requiresPaymentMethod: product.RequireCreditCard);

    private static CustomerSubscription ToCustomerSubscription(Subscription subscription)
    {
        var state = subscription.State?.Value ?? "unknown";

        return new CustomerSubscription(
            id: subscription.Id ?? 0,
            reference: subscription.Reference,
            planHandle: subscription.Product?.Handle ?? string.Empty,
            planName: subscription.Product?.Name ?? subscription.Product?.Handle ?? string.Empty,
            priceInCents: subscription.ProductPriceInCents ?? subscription.Product?.PriceInCents ?? 0,
            currency: subscription.Currency,
            state: state,
            isLive: !TerminalStates.Contains(state),
            // current_period_ends_at is when the next regularly scheduled charge occurs.
            // next_assessment_at is deliberately not used: it diverges into a retry time after a
            // failed renewal, so surfacing it would misreport the billing date.
            nextBillingDate: subscription.CurrentPeriodEndsAt,
            currentPeriodStartedAt: subscription.CurrentPeriodStartedAt,
            createdAt: subscription.CreatedAt);
    }

    private static string DescribeValidation(CustomerErrorResponse1 validation)
    {
        var messages = new List<string>();
        if (validation.Errors?.PerPage is { Count: > 0 } perPage)
        {
            messages.AddRange(perPage);
        }

        if (validation.Errors?.PricePoint is { Count: > 0 } pricePoint)
        {
            messages.AddRange(pricePoint);
        }

        return messages.Count == 0
            ? "the customer could not be created"
            : MaxioFailures.Truncate(string.Join("; ", messages))!;
    }
}
