using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.ErrorResponse;
using MaxioAdvancedBilling.Core.Exceptions;
using MaxioAdvancedBilling.Errors;
using MaxioAdvancedBilling.Models;
using MaxioAdvancedBilling.Models.Enums;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Maxio Advanced Billing implementation of <see cref="ISubscriptionBillingService"/>.
///
/// Idempotency (Maxio is the system of record; the local database is the in-memory provider, which cannot
/// durably enforce a uniqueness claim, so the claim lives in Maxio):
///  - Customers are keyed by <c>reference</c> = the eShop user reference; Maxio enforces that a reference is
///    unique, so a concurrent duplicate create is rejected (422) and reconciled by re-reading by reference.
///  - Subscriptions carry a deterministic <c>reference</c>; the create is gated on a prior find, and any
///    ambiguous/failed create is reconciled by re-reading that reference before reporting failure.
///
/// Every provider/transport/parse failure is translated into <see cref="SubscriptionBillingException"/> so
/// callers see a single failure type with a caller-safe message.
/// </summary>
public sealed class MaxioSubscriptionBillingService : ISubscriptionBillingService
{
    private const int PlansPerPage = 200;
    private const int PlansMaxPages = 50;

    private readonly MaxioAdvancedBillingClient _client;
    private readonly MaxioOptions _options;
    private readonly ILogger<MaxioSubscriptionBillingService> _logger;
    private readonly TimeSpan _budget;

    public MaxioSubscriptionBillingService(
        MaxioAdvancedBillingClient client,
        IOptions<MaxioOptions> options,
        ILogger<MaxioSubscriptionBillingService> logger)
    {
        _client = client;
        _options = options.Value;
        _logger = logger;
        _budget = TimeSpan.FromSeconds(_options.TotalBudgetSeconds);
    }

    private string ProductFamilyId => "handle:" + _options.ProductFamilyHandle;

    public async Task<IReadOnlyList<SubscriptionPlan>> GetPlansAsync(CancellationToken cancellationToken = default)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(_budget);
        var token = cts.Token;

        var plans = new List<SubscriptionPlan>();
        var page = 1;
        while (true)
        {
            IReadOnlyList<ProductResponse> pageItems;
            try
            {
                pageItems = await _client.ProductFamilies.ListProductsForProductFamily(
                    productFamilyId: ProductFamilyId,
                    dateField: null,
                    filter: null,
                    startDate: null,
                    endDate: null,
                    startDatetime: null,
                    endDatetime: null,
                    includeArchived: false,
                    include: null,
                    page: page,
                    perPage: PlansPerPage,
                    ct: token);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (SdkException<ListProductsForProductFamilyError> ex)
            {
                if (ex.Error.TryGetString(out _))
                    throw Fail("The configured product family could not be found", HttpStatusCode.NotFound, ex);
                if (ex.Error.TryGetRawError(out var raw))
                    throw Fail("Could not load subscription plans", raw.StatusCode, ex);
                throw Fail("Could not load subscription plans", null, ex);
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
            {
                throw Fail("The billing provider was unreachable while loading plans", null, ex);
            }
            catch (JsonException ex)
            {
                throw Fail("The plans response from the billing provider could not be processed", null, ex);
            }

            foreach (var pr in pageItems)
            {
                var plan = MapPlan(pr.Product);
                if (plan is not null)
                    plans.Add(plan);
            }

            if (pageItems.Count < PlansPerPage)
                break;

            page++;
            if (page > PlansMaxPages)
                throw new SubscriptionBillingException(
                    $"The plan catalog exceeded {PlansMaxPages * PlansPerPage} entries; refusing to return a silently truncated list.");
        }

        _logger.LogInformation("Loaded {PlanCount} subscription plan(s) for family {Family}.", plans.Count, _options.ProductFamilyHandle);
        return plans;
    }

    public async Task<SubscribeResult> SubscribeAsync(SubscriberIdentity subscriber, string planHandle, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(subscriber);
        if (string.IsNullOrWhiteSpace(planHandle))
            throw new PlanNotFoundException(planHandle ?? string.Empty);

        // Cross-operation invariant: the plan handle a caller submits must be one of the family's plans.
        var plans = await GetPlansAsync(cancellationToken);
        var plan = plans.FirstOrDefault(p => string.Equals(p.Handle, planHandle, StringComparison.OrdinalIgnoreCase));
        if (plan is null)
            throw new PlanNotFoundException(planHandle);

        // Use the resolved (canonical) handle from the catalog.
        var canonicalHandle = plan.Handle;

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(_budget);
        var token = cts.Token;

        // 1. Ensure the Maxio customer exists (idempotent).
        var customer = await EnsureCustomerAsync(subscriber, cancellationToken, token);

        var subscriptionReference = BuildSubscriptionReference(subscriber.UserReference, canonicalHandle);

        // 2. Idempotent hit: a subscription with this reference already exists.
        var existing = await FindSubscriptionAsync(subscriptionReference, token, cancellationToken);
        if (existing is not null)
        {
            _logger.LogInformation(
                "Subscribe replay for user {User} plan {Plan}: existing subscription {SubId} ({State}).",
                subscriber.UserReference, canonicalHandle, existing.Id, existing.State?.Value);
            return new SubscribeResult { Subscription = MapSubscription(existing)!, AlreadyExisted = true };
        }

        // 3. Create the subscription, binding to the ensured customer by reference.
        var body = new CreateSubscriptionRequest
        {
            Subscription = new CreateSubscription
            {
                ProductHandle = canonicalHandle,
                CustomerReference = subscriber.UserReference,
                Reference = subscriptionReference,
                // These plans require no payment method; invoice/remittance collection avoids an immediate
                // card charge (the provider default 'automatic' would fail with no card on file).
                PaymentCollectionMethod = string.IsNullOrWhiteSpace(_options.PaymentCollectionMethod)
                    ? null
                    : CollectionMethod.FromValue(_options.PaymentCollectionMethod),
            }
        };

        Subscription? created;
        try
        {
            var resp = await _client.Subscriptions.CreateSubscription(body, ct: token);
            created = resp.Subscription;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (SdkException<CreateSubscriptionError> ex)
        {
            // A concurrent create may already have produced the subscription — reconcile before reporting.
            var reconciled = await ReconcileSubscriptionAsync(subscriptionReference, cancellationToken);
            if (reconciled is not null)
                return new SubscribeResult { Subscription = MapSubscription(reconciled)!, AlreadyExisted = true };

            if (ex.Error.TryGetErrorListResponse1(out var errors))
                throw Fail("The billing provider rejected the subscription", HttpStatusCode.UnprocessableEntity, ex, JoinErrors(errors.Errors));
            if (ex.Error.TryGetRawError(out var raw))
                throw Fail("The billing provider rejected the subscription", raw.StatusCode, ex);
            throw Fail("The billing provider rejected the subscription", null, ex);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            // Unknown outcome: the create may have been received. Re-read by reference before reporting failure.
            var reconciled = await ReconcileSubscriptionAsync(subscriptionReference, cancellationToken);
            if (reconciled is not null)
                return new SubscribeResult { Subscription = MapSubscription(reconciled)!, AlreadyExisted = true };

            throw Fail("The subscription could not be created", null, ex);
        }

        var mapped = MapSubscription(created);
        if (mapped is null)
            throw new SubscriptionBillingException("The billing provider returned an empty subscription response.");

        _logger.LogInformation(
            "Created subscription {SubId} for user {User} plan {Plan} (state {State}, customer {CustomerId}).",
            mapped.Id, subscriber.UserReference, canonicalHandle, mapped.State, customer.Id);

        return new SubscribeResult { Subscription = mapped, AlreadyExisted = false };
    }

    public async Task<IReadOnlyList<CustomerSubscription>> GetSubscriptionsAsync(SubscriberIdentity subscriber, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(subscriber);

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(_budget);
        var token = cts.Token;

        var customer = await FindCustomerAsync(subscriber.UserReference, token, cancellationToken);
        if (customer?.Id is not int customerId)
            return Array.Empty<CustomerSubscription>();

        IReadOnlyList<SubscriptionResponse> subs;
        try
        {
            subs = await _client.Customers.ListCustomerSubscriptions(customerId, ct: token);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (SdkException<RawError> ex)
        {
            throw Fail("Could not load your subscriptions", ex.Error.StatusCode, ex);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw Fail("The billing provider was unreachable while loading your subscriptions", null, ex);
        }
        catch (JsonException ex)
        {
            throw Fail("The subscriptions response from the billing provider could not be processed", null, ex);
        }

        var result = subs
            .Select(s => MapSubscription(s.Subscription))
            .Where(s => s is not null)
            .Select(s => s!)
            .ToList();

        _logger.LogInformation("Loaded {Count} subscription(s) for user {User}.", result.Count, subscriber.UserReference);
        return result;
    }

    // --- Customer helpers -------------------------------------------------------------------------

    private async Task<Customer> EnsureCustomerAsync(SubscriberIdentity subscriber, CancellationToken callerToken, CancellationToken token)
    {
        var existing = await FindCustomerAsync(subscriber.UserReference, token, callerToken);
        if (existing is not null)
            return existing;

        var (firstName, lastName) = DeriveName(subscriber);
        var body = new CreateCustomerRequest
        {
            Customer = new CreateCustomer
            {
                FirstName = firstName,
                LastName = lastName,
                Email = subscriber.Email,
                Reference = subscriber.UserReference,
            }
        };

        try
        {
            var resp = await _client.Customers.CreateCustomer(body, ct: token);
            _logger.LogInformation("Created Maxio customer {CustomerId} for user {User}.", resp.Customer.Id, subscriber.UserReference);
            return resp.Customer;
        }
        catch (OperationCanceledException) when (callerToken.IsCancellationRequested)
        {
            throw;
        }
        catch (SdkException<CreateCustomerError> ex)
        {
            // Reference uniqueness is provider-enforced: a concurrent create yields a rejection here.
            // Reconcile by re-reading the (now-existing) customer before treating this as a hard error.
            var reconciled = await ReconcileCustomerAsync(subscriber.UserReference, callerToken);
            if (reconciled is not null)
                return reconciled;

            if (ex.Error.TryGetCustomerErrorResponse1(out _))
                throw Fail("The billing provider rejected the customer details", HttpStatusCode.UnprocessableEntity, ex);
            if (ex.Error.TryGetRawError(out var raw))
                throw Fail("Could not create your billing customer record", raw.StatusCode, ex);
            throw Fail("Could not create your billing customer record", null, ex);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            // Unknown outcome: the create may have landed. Re-read by reference before reporting failure.
            var reconciled = await ReconcileCustomerAsync(subscriber.UserReference, callerToken);
            if (reconciled is not null)
                return reconciled;

            throw Fail("Could not create your billing customer record", null, ex);
        }
    }

    private async Task<Customer?> FindCustomerAsync(string reference, CancellationToken token, CancellationToken callerToken)
    {
        try
        {
            var resp = await _client.Customers.ReadCustomerByReference(reference, ct: token);
            return resp.Customer;
        }
        catch (OperationCanceledException) when (callerToken.IsCancellationRequested)
        {
            throw;
        }
        catch (SdkException<RawError> ex) when (ex.Error.StatusCode == HttpStatusCode.NotFound)
        {
            return null; // no customer for this reference yet
        }
        catch (SdkException<RawError> ex)
        {
            throw Fail("Could not look up your billing customer record", ex.Error.StatusCode, ex);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw Fail("The billing provider was unreachable", null, ex);
        }
        catch (JsonException ex)
        {
            throw Fail("The customer response from the billing provider could not be processed", null, ex);
        }
    }

    /// <summary>Best-effort re-read used only on an error/ambiguous path; never throws.</summary>
    private async Task<Customer?> ReconcileCustomerAsync(string reference, CancellationToken callerToken)
    {
        if (callerToken.IsCancellationRequested)
            return null;
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(callerToken);
            cts.CancelAfter(_budget);
            var resp = await _client.Customers.ReadCustomerByReference(reference, ct: cts.Token);
            return resp.Customer;
        }
        catch
        {
            return null;
        }
    }

    // --- Subscription helpers ---------------------------------------------------------------------

    private async Task<Subscription?> FindSubscriptionAsync(string reference, CancellationToken token, CancellationToken callerToken)
    {
        try
        {
            var resp = await _client.Subscriptions.FindSubscription(reference, ct: token);
            return resp.Subscription;
        }
        catch (OperationCanceledException) when (callerToken.IsCancellationRequested)
        {
            throw;
        }
        catch (SdkException<FindSubscriptionError> ex)
        {
            if (ex.Error.TryGetNoContent(out _))
                return null; // 404 — no subscription with this reference
            if (ex.Error.TryGetRawError(out var raw))
                throw Fail("Could not look up your subscription", raw.StatusCode, ex);
            throw Fail("Could not look up your subscription", null, ex);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw Fail("The billing provider was unreachable", null, ex);
        }
        catch (JsonException ex)
        {
            throw Fail("The subscription response from the billing provider could not be processed", null, ex);
        }
    }

    /// <summary>Best-effort re-read used only on an error/ambiguous path; never throws.</summary>
    private async Task<Subscription?> ReconcileSubscriptionAsync(string reference, CancellationToken callerToken)
    {
        if (callerToken.IsCancellationRequested)
            return null;
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(callerToken);
            cts.CancelAfter(_budget);
            var resp = await _client.Subscriptions.FindSubscription(reference, ct: cts.Token);
            return resp.Subscription;
        }
        catch
        {
            return null;
        }
    }

    // --- Mapping & helpers ------------------------------------------------------------------------

    private static SubscriptionPlan? MapPlan(Product? product)
    {
        if (product?.Handle is not string handle || string.IsNullOrWhiteSpace(handle))
            return null;

        return new SubscriptionPlan
        {
            Id = product.Id,
            Handle = handle,
            Name = product.Name,
            Description = product.Description,
            PriceInCents = product.PriceInCents,
            Interval = product.Interval,
            IntervalUnit = product.IntervalUnit?.Value,
            RequiresPaymentMethod = product.RequireCreditCard,
        };
    }

    private static CustomerSubscription? MapSubscription(Subscription? sub)
    {
        if (sub is null)
            return null;

        return new CustomerSubscription
        {
            Id = sub.Id,
            Reference = sub.Reference,
            PlanHandle = sub.Product?.Handle,
            PlanName = sub.Product?.Name,
            State = sub.State?.Value,
            PriceInCents = sub.ProductPriceInCents,
            NextBillingDate = sub.CurrentPeriodEndsAt ?? sub.NextAssessmentAt,
            CreatedAt = sub.CreatedAt,
        };
    }

    private static string BuildSubscriptionReference(string userReference, string planHandle)
        => $"eshop:{userReference}:{planHandle}";

    private static (string FirstName, string LastName) DeriveName(SubscriberIdentity subscriber)
    {
        var first = subscriber.FirstName;
        var last = subscriber.LastName;

        if (string.IsNullOrWhiteSpace(first))
        {
            var localPart = subscriber.Email.Split('@')[0];
            first = string.IsNullOrWhiteSpace(localPart) ? subscriber.UserReference : localPart;
        }
        if (string.IsNullOrWhiteSpace(last))
            last = "eShopOnWeb";

        return (first!, last!);
    }

    private static string? JoinErrors(IReadOnlyList<string>? errors)
        => errors is { Count: > 0 } ? string.Join("; ", errors) : null;

    private SubscriptionBillingException Fail(string context, HttpStatusCode? status, Exception inner, string? detail = null)
    {
        var message = string.IsNullOrWhiteSpace(detail) ? $"{context}." : $"{context}: {detail}";
        _logger.LogWarning(inner, "Maxio billing failure ({Status}): {Context}", status?.ToString() ?? "no-status", context);
        return new SubscriptionBillingException(message, status, inner);
    }
}
