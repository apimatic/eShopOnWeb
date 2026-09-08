using System;
using System.Collections.Concurrent;
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
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using Microsoft.Extensions.Logging;
using Polly.Timeout;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>Raised when a subscription create send has an unknown outcome (transport failure).</summary>
internal sealed class SubscriptionCreateOutcomeUnknownException : Exception
{
    public SubscriptionCreateOutcomeUnknownException(Exception inner)
        : base("The subscription create request may or may not have reached the billing provider.", inner)
    {
    }
}

/// <summary>
/// Subscription billing backed by Maxio Advanced Billing. Maxio is the system of record; the local
/// <see cref="SubscriptionEnrollment"/> store is the idempotency gate that guarantees a double-click
/// (or a concurrent duplicate request) never creates two Maxio customers or two subscriptions for the
/// same shopper + plan.
/// </summary>
public sealed class MaxioSubscriptionService : ISubscriptionService
{
    private const string MeteredComponentHandle = "api-call";
    private const int MaxCreateAttempts = 3;
    private static readonly TimeSpan ClaimWaitTimeout = TimeSpan.FromSeconds(20);
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> _gates = new(StringComparer.Ordinal);

    private readonly MaxioAdvancedBillingClient _client;
    private readonly MaxioSettings _settings;
    private readonly IRepository<SubscriptionEnrollment> _enrollments;
    private readonly ILogger<MaxioSubscriptionService> _logger;

    public MaxioSubscriptionService(
        MaxioAdvancedBillingClient client,
        MaxioSettings settings,
        IRepository<SubscriptionEnrollment> enrollments,
        ILogger<MaxioSubscriptionService> logger)
    {
        _client = client;
        _settings = settings;
        _enrollments = enrollments;
        _logger = logger;
    }

    public async Task<SubscriptionCatalog> GetCatalogAsync(CancellationToken cancellationToken)
    {
        var family = await GetConfiguredFamilyAsync(cancellationToken);
        if (family is null)
        {
            throw new MaxioBillingUnavailableException(
                $"The configured Maxio product family ('{_settings.ProductFamilyHandle}') was not found on this site.");
        }

        var products = await ListFamilyProductsAsync(family.Id, cancellationToken);

        var plans = products
            .Where(p => p.ArchivedAt is null)
            .Select(p => new SubscriptionPlanSummary(
                p.Handle ?? string.Empty,
                p.Name ?? p.Handle ?? string.Empty,
                p.Interval,
                p.IntervalUnit?.Value,
                p.PriceInCents,
                p.RequireCreditCard ?? false,
                Archived: false))
            .OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var component = await GetComponentByHandleAsync(MeteredComponentHandle, cancellationToken);

        return new SubscriptionCatalog(
            plans,
            component is null
                ? null
                : new UsageComponentSummary(
                    component.Handle ?? string.Empty,
                    component.Kind?.Value,
                    component.PricePerUnitInCents));
    }

    public async Task<SubscribeResult> SubscribeAsync(SubscribeToPlanRequest request, CancellationToken cancellationToken)
    {
        var userName = request.UserName?.Trim() ?? string.Empty;
        var planHandle = request.PlanHandle?.Trim() ?? string.Empty;
        if (string.IsNullOrEmpty(userName))
        {
            throw new InvalidSubscriptionRequestException("A user name is required.");
        }

        if (string.IsNullOrEmpty(planHandle))
        {
            throw new InvalidSubscriptionRequestException("planHandle is required.");
        }

        var plan = await GetProductByHandleAsync(planHandle, cancellationToken)
            ?? throw new SubscriptionPlanNotFoundException(
                $"No subscription plan with handle '{planHandle}' is available.");
        if (plan.ArchivedAt is not null ||
            !string.Equals(plan.ProductFamily?.Handle, _settings.ProductFamilyHandle, StringComparison.Ordinal))
        {
            throw new SubscriptionPlanNotFoundException(
                $"No subscription plan with handle '{planHandle}' is available on this store.");
        }

        var planName = plan.Name ?? plan.Handle;
        var customerReference = MaxioReferenceKeys.CustomerReference(userName);
        var subscriptionReference = MaxioReferenceKeys.SubscriptionReference(userName, planHandle);

        var gate = _gates.GetOrAdd(userName + "\n" + planHandle, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            var customer = await EnsureCustomerAsync(
                userName, customerReference, request.FirstName, request.LastName, cancellationToken);

            // Idempotency: if a subscription that occupies this slot already exists, return it
            // instead of creating a second one (this is what makes a double-click harmless).
            var existing = await FindSubscriptionByReferenceAsync(customer.Id, subscriptionReference, cancellationToken);
            if (existing is not null && OccupiesSlot(existing.State))
            {
                await PersistEnrollmentAsync(userName, planHandle, customer.Id, existing, cancellationToken);
                return new SubscribeResult(ToSummary(existing, planHandle, planName), Created: false);
            }

            var claim = await ClaimEnrollmentAsync(
                userName, planHandle, customerReference, customer.Id, cancellationToken);
            if (claim.Existing is not null)
            {
                await PersistEnrollmentAsync(userName, planHandle, customer.Id, claim.Existing, cancellationToken);
                return new SubscribeResult(ToSummary(claim.Existing, planHandle, planName), Created: false);
            }

            var subscription = await CreateSubscriptionSettledAsync(
                customer.Id, customerReference, subscriptionReference, planHandle, cancellationToken);

            await PersistEnrollmentAsync(userName, planHandle, customer.Id, subscription, cancellationToken);
            return new SubscribeResult(ToSummary(subscription, planHandle, planName), Created: true);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<IReadOnlyList<SubscriptionSummary>> GetSubscriptionsAsync(
        string userName, CancellationToken cancellationToken)
    {
        userName = userName?.Trim() ?? string.Empty;
        if (string.IsNullOrEmpty(userName))
        {
            return Array.Empty<SubscriptionSummary>();
        }

        var customer = await GetCustomerByReferenceAsync(
            MaxioReferenceKeys.CustomerReference(userName), cancellationToken);
        if (customer is null)
        {
            return Array.Empty<SubscriptionSummary>();
        }

        var subscriptions = await ListCustomerSubscriptionsAsync(customer.Id, cancellationToken);
        if (subscriptions.Count == 0)
        {
            return Array.Empty<SubscriptionSummary>();
        }

        var enrollments = await _enrollments.ListAsync(
            new SubscriptionEnrollmentsByBuyerSpecification(userName));
        var enrollmentByReference = enrollments
            .Where(e => e.MaxioSubscriptionReference is not null)
            .ToDictionary(e => e.MaxioSubscriptionReference!, StringComparer.Ordinal);

        var summaries = new List<SubscriptionSummary>(subscriptions.Count);
        foreach (var subscription in subscriptions)
        {
            // "My subscriptions" shows current subscriptions; skip end-of-life ones (canceled,
            // expired, ...) that no longer occupy the shopper's account.
            if (!OccupiesSlot(subscription.State))
            {
                continue;
            }

            var planHandle = subscription.Product?.Handle;
            if (string.IsNullOrEmpty(planHandle) &&
                subscription.Reference is not null &&
                enrollmentByReference.TryGetValue(subscription.Reference, out var enrollment))
            {
                planHandle = enrollment.PlanHandle;
            }

            planHandle ??= MaxioReferenceKeys.TryGetPlanHandle(subscription.Reference);

            summaries.Add(ToSummary(subscription, planHandle, subscription.Product?.Name));
        }

        return summaries;
    }

    // -------- Maxio read accessors (each translates SDK/infra errors at the boundary) --------

    private async Task<ProductFamily?> GetConfiguredFamilyAsync(CancellationToken ct)
    {
        IReadOnlyList<ProductFamilyResponse> families;
        try
        {
            families = await WithInfraGuard(
                "ListProductFamilies",
                token => _client.ProductFamilies.ListProductFamilies(
                    dateField: null, startDate: null, endDate: null,
                    startDatetime: null, endDatetime: null, ct: token),
                ct);
        }
        catch (SdkException<RawError> ex)
        {
            throw TranslateRawError(ex);
        }

        foreach (var familyResponse in families)
        {
            var family = familyResponse.ProductFamily;
            if (family is not null &&
                string.Equals(family.Handle, _settings.ProductFamilyHandle, StringComparison.Ordinal))
            {
                return family;
            }
        }

        return null;
    }

    private async Task<List<Product>> ListFamilyProductsAsync(int? familyId, CancellationToken ct)
    {
        var results = new List<Product>();
        const int perPage = 20;
        var page = 1;

        while (true)
        {
            IReadOnlyList<ProductResponse> pageItems;
            try
            {
                pageItems = await WithInfraGuard(
                    "ListProductsForProductFamily",
                    token => _client.ProductFamilies.ListProductsForProductFamily(
                        productFamilyId: familyId?.ToString() ?? string.Empty,
                        dateField: null,
                        filter: null,
                        startDate: null,
                        endDate: null,
                        startDatetime: null,
                        endDatetime: null,
                        includeArchived: false,
                        include: null,
                        page: page,
                        perPage: perPage,
                        ct: token),
                    ct);
            }
            catch (SdkException<ListProductsForProductFamilyError> ex)
            {
                throw TranslateListProductsError(ex);
            }
            catch (SdkException<RawError> ex)
            {
                throw TranslateRawError(ex);
            }

            foreach (var productResponse in pageItems)
            {
                if (productResponse.Product is not null)
                {
                    results.Add(productResponse.Product);
                }
            }

            if (pageItems.Count < perPage || page >= 100)
            {
                break;
            }

            page++;
        }

        return results;
    }

    private async Task<Product?> GetProductByHandleAsync(string handle, CancellationToken ct)
    {
        try
        {
            var response = await WithInfraGuard(
                "ReadProductByHandle",
                token => _client.Products.ReadProductByHandle(apiHandle: handle, ct: token),
                ct);
            return response.Product;
        }
        catch (SdkException<RawError> ex) when (ex.Error.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
        catch (SdkException<RawError> ex)
        {
            throw TranslateRawError(ex);
        }
    }

    private async Task<Component?> GetComponentByHandleAsync(string handle, CancellationToken ct)
    {
        try
        {
            var response = await WithInfraGuard(
                "FindComponent",
                token => _client.Components.FindComponent(handle: handle, ct: token),
                ct);
            return response.Component;
        }
        catch (SdkException<RawError> ex) when (ex.Error.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
        catch (SdkException<RawError> ex)
        {
            throw TranslateRawError(ex);
        }
    }

    private async Task<Customer?> GetCustomerByReferenceAsync(string reference, CancellationToken ct)
    {
        try
        {
            var response = await WithInfraGuard(
                "ReadCustomerByReference",
                token => _client.Customers.ReadCustomerByReference(reference: reference, ct: token),
                ct);
            return response.Customer;
        }
        catch (SdkException<RawError> ex) when (ex.Error.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
        catch (SdkException<RawError> ex)
        {
            throw TranslateRawError(ex);
        }
    }

    private async Task<List<Subscription>> ListCustomerSubscriptionsAsync(int? customerId, CancellationToken ct)
    {
        if (customerId is null)
        {
            return new List<Subscription>();
        }

        try
        {
            var response = await WithInfraGuard(
                "ListCustomerSubscriptions",
                token => _client.Customers.ListCustomerSubscriptions(customerId: customerId.Value, ct: token),
                ct);
            return response
                .Select(r => r.Subscription)
                .Where(s => s is not null)
                .Select(s => s!)
                .ToList();
        }
        catch (SdkException<RawError> ex)
        {
            throw TranslateRawError(ex);
        }
    }

    private async Task<Subscription?> FindSubscriptionByReferenceAsync(int? customerId, string reference, CancellationToken ct)
    {
        var subscriptions = await ListCustomerSubscriptionsAsync(customerId, ct);
        return subscriptions.FirstOrDefault(s =>
            string.Equals(s.Reference, reference, StringComparison.Ordinal));
    }

    // -------- Customer + subscription creation --------

    private async Task<Customer> EnsureCustomerAsync(
        string userName, string customerReference, string? firstName, string? lastName, CancellationToken ct)
    {
        var existing = await GetCustomerByReferenceAsync(customerReference, ct);
        if (existing is not null)
        {
            return existing;
        }

        // Maxio requires first/last/email to create a customer. eShopOnWeb's identity stores only an
        // email-format username, so profile names come from the request when the storefront has them,
        // else are derived from the username. They are used only when the customer is first created.
        var (first, last) = ResolveNames(userName, firstName, lastName);

        var body = new CreateCustomerRequest
        {
            Customer = new CreateCustomer
            {
                FirstName = first,
                LastName = last,
                Email = userName,
                Reference = customerReference,
            },
        };

        try
        {
            var response = await WithInfraGuard(
                "CreateCustomer",
                token => _client.Customers.CreateCustomer(body: body, ct: token),
                ct);
            return response.Customer;
        }
        catch (SdkException<CreateCustomerError> ex)
        {
            // A 422 here is normally the server-enforced unique-reference race (two concurrent requests
            // both tried to create the same customer). Re-read: if the customer now exists the other
            // request won; otherwise surface the rejection.
            if (ex.Error.TryGetCustomerErrorResponse1(out _))
            {
                var raced = await GetCustomerByReferenceAsync(customerReference, ct);
                if (raced is not null)
                {
                    return raced;
                }
            }

            if (ex.Error.TryGetRawError(out var raw))
            {
                throw TranslateRawError(raw);
            }

            throw new SubscriptionProviderRejectedException(
                422, "The billing provider rejected the customer profile.");
        }
    }

    private async Task<Subscription> CreateSubscriptionSettledAsync(
        int? customerId,
        string customerReference,
        string subscriptionReference,
        string planHandle,
        CancellationToken ct)
    {
        for (var attempt = 0; attempt < MaxCreateAttempts; attempt++)
        {
            try
            {
                return await CreateSubscriptionGuardedAsync(
                    customerReference, subscriptionReference, planHandle, ct);
            }
            catch (Exception ex) when (ex is SubscriptionCreateOutcomeUnknownException or MaxioBillingUnavailableException)
            {
                // The send outcome is unknown (transport failure or unreadable response): settle it by
                // re-reading provider state before deciding whether a fresh (guarded) send is needed.
                // Never assume the create did not happen.
                var reconciled = await FindSubscriptionByReferenceAsync(customerId, subscriptionReference, ct);
                if (reconciled is not null)
                {
                    return reconciled;
                }
            }
            catch (SdkException<CreateSubscriptionError> ex)
            {
                // A deterministic rejection. If Maxio ever enforces reference uniqueness a duplicate
                // would land here; reconcile before treating it as a hard validation error.
                var reconciled = await FindSubscriptionByReferenceAsync(customerId, subscriptionReference, ct);
                if (reconciled is not null)
                {
                    return reconciled;
                }

                throw TranslateCreateSubscriptionError(ex);
            }
        }

        throw new MaxioBillingUnavailableException(
            "The subscription could not be confirmed as created. Please check the billing provider before retrying.");
    }

    private async Task<Subscription> CreateSubscriptionGuardedAsync(
        string customerReference, string subscriptionReference, string planHandle, CancellationToken ct)
    {
        using var writeOnceScope = MaxioWriteOnceScope.Enter();
        try
        {
            var body = new CreateSubscriptionRequest
            {
                Subscription = new CreateSubscription
                {
                    ProductHandle = planHandle,
                    CustomerReference = customerReference,
                    Reference = subscriptionReference,
                    // These plans do not require a card, but Maxio still tries to collect the first
                    // (immediate) balance at signup under the default automatic collection method and
                    // rejects the create when no payment method is on file. Invoicing the balance keeps
                    // subscribe working without card capture / 3-DS (the subscription is created active;
                    // the initial balance is invoiced instead of auto-charged).
                    PaymentCollectionMethod = CollectionMethod.Invoice,
                },
            };

            var response = await WithInfraGuard(
                "CreateSubscription",
                token => _client.Subscriptions.CreateSubscription(body: body, ct: token),
                ct);

            if (response.Subscription is null)
            {
                throw new MaxioBillingUnavailableException(
                    "The billing provider returned no subscription for the create request.");
            }

            return response.Subscription;
        }
        catch (MaxioWriteOnceRefusedException ex)
        {
            throw new SubscriptionCreateOutcomeUnknownException(ex);
        }
        catch (HttpRequestException ex)
        {
            throw new SubscriptionCreateOutcomeUnknownException(ex);
        }
        catch (TaskCanceledException ex)
        {
            throw new SubscriptionCreateOutcomeUnknownException(ex);
        }
        catch (SubscriptionProviderRejectedException)
        {
            throw;
        }
        catch (MaxioBillingUnavailableException)
        {
            throw;
        }
    }

    // -------- Local enrollment store (idempotency gate) --------

    private async Task<(SubscriptionEnrollment Enrollment, Subscription? Existing)> ClaimEnrollmentAsync(
        string userName, string planHandle, string customerReference, int? customerId, CancellationToken ct)
    {
        var existingEnrollment = await FindEnrollmentAsync(userName, planHandle, ct);
        if (existingEnrollment is not null)
        {
            existingEnrollment.MarkSubscribing(customerId);
            await _enrollments.UpdateAsync(existingEnrollment);
            return (existingEnrollment, null);
        }

        var enrollment = new SubscriptionEnrollment(userName, planHandle, customerReference);
        enrollment.MarkSubscribing(customerId);

        try
        {
            var added = await _enrollments.AddAsync(enrollment);
            return (added, null);
        }
        catch (DbUpdateException)
        {
            // Another instance won the unique (BuyerId, PlanHandle) race. Wait for its subscription to
            // appear; only take over once its claim looks stale.
            var deadline = DateTimeOffset.UtcNow + ClaimWaitTimeout;
            var subscriptionReference = MaxioReferenceKeys.SubscriptionReference(userName, planHandle);

            while (DateTimeOffset.UtcNow < deadline)
            {
                var appeared = await FindSubscriptionByReferenceAsync(customerId, subscriptionReference, ct);
                if (appeared is not null)
                {
                    var winner = await FindEnrollmentAsync(userName, planHandle, ct);
                    if (winner is not null)
                    {
                        return (winner, appeared);
                    }
                }

                await Task.Delay(TimeSpan.FromMilliseconds(500), ct);
            }

            var stale = await FindEnrollmentAsync(userName, planHandle, ct)
                ?? throw new MaxioBillingUnavailableException("The enrollment claim could not be established.");
            stale.MarkSubscribing(customerId);
            await _enrollments.UpdateAsync(stale);
            return (stale, null);
        }
    }

    private async Task<SubscriptionEnrollment?> FindEnrollmentAsync(string userName, string planHandle, CancellationToken ct)
    {
        var rows = await _enrollments.ListAsync(
            new SubscriptionEnrollmentByBuyerAndPlanSpecification(userName, planHandle), ct);
        return rows.FirstOrDefault();
    }

    private async Task PersistEnrollmentAsync(
        string userName, string planHandle, int? customerId, Subscription subscription, CancellationToken ct)
    {
        var enrollment = await FindEnrollmentAsync(userName, planHandle, ct);
        if (enrollment is null)
        {
            var reference = MaxioReferenceKeys.CustomerReference(userName);
            enrollment = new SubscriptionEnrollment(userName, planHandle, reference);
            enrollment.MarkSubscribing(customerId);
        }

        enrollment.MarkSubscribed(
            subscription.Id,
            subscription.Reference,
            Wire(subscription.State),
            subscription.ProductPriceInCents,
            NextBillingDate(subscription));

        if (enrollment.Id == 0)
        {
            await _enrollments.AddAsync(enrollment);
        }
        else
        {
            await _enrollments.UpdateAsync(enrollment);
        }
    }

    // -------- Mapping + helpers --------

    private static SubscriptionSummary ToSummary(Subscription subscription, string? planHandle, string? planName)
    {
        return new SubscriptionSummary(
            subscription.Reference,
            subscription.Id,
            planHandle,
            planName,
            subscription.ProductPriceInCents ?? subscription.Product?.PriceInCents,
            Wire(subscription.State),
            NextBillingDate(subscription),
            subscription.CreatedAt);
    }

    /// <summary>
    /// The next billing date. <c>current_period_ends_at</c> is when the next regularly scheduled charge
    /// occurs; <c>next_assessment_at</c> only diverges when a renewal payment failed and is scheduled for
    /// auto-retry, so read that instead for payment-problem states.
    /// </summary>
    private static DateTimeOffset? NextBillingDate(Subscription subscription)
    {
        if (IsProblemState(subscription.State))
        {
            return subscription.NextAssessmentAt ?? subscription.CurrentPeriodEndsAt;
        }

        return subscription.CurrentPeriodEndsAt ?? subscription.NextAssessmentAt;
    }

    private static bool IsProblemState(SubscriptionState? state) =>
        state == SubscriptionState.PastDue ||
        state == SubscriptionState.SoftFailure ||
        state == SubscriptionState.Unpaid;

    /// <summary>
    /// Whether a subscription occupies the shopper's slot for its plan (so a re-subscribe should return
    /// it instead of creating another). Everything except the SDK's documented end-of-life / pre-billing
    /// states counts; unknown states are treated conservatively as occupying.
    /// </summary>
    private static bool OccupiesSlot(SubscriptionState? state)
    {
        if (state is null)
        {
            return true;
        }

        return state != SubscriptionState.AwaitingSignup &&
               state != SubscriptionState.Canceled &&
               state != SubscriptionState.Expired &&
               state != SubscriptionState.FailedToCreate &&
               state != SubscriptionState.OnHold &&
               state != SubscriptionState.Suspended &&
               state != SubscriptionState.TrialEnded;
    }

    private static string? Wire(SubscriptionState? state) => state?.Value;

    private static (string First, string Last) ResolveNames(string userName, string? firstName, string? lastName)
    {
        var derived = MaxioReferenceKeys.DeriveNames(userName);
        return (
            !string.IsNullOrWhiteSpace(firstName) ? firstName.Trim() : derived.FirstName,
            !string.IsNullOrWhiteSpace(lastName) ? lastName.Trim() : derived.LastName);
    }

    private async Task<T> WithInfraGuard<T>(
        string operation, Func<CancellationToken, Task<T>> action, CancellationToken ct)
    {
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(MaxioServiceCollectionExtensions.OverallCallBudget);
            return await action(cts.Token).ConfigureAwait(false);
        }
        catch (SubscriptionProviderRejectedException)
        {
            throw;
        }
        catch (MaxioBillingUnavailableException)
        {
            throw;
        }
        catch (TimeoutRejectedException ex)
        {
            // The SDK's per-attempt retry timeout fired (Polly). Not a retryable outcome at this layer.
            throw new MaxioBillingUnavailableException("The billing provider call timed out.", ex);
        }
        catch (JsonException ex)
        {
            throw new MaxioBillingUnavailableException(
                $"The billing provider returned an unreadable response ({operation}).", ex);
        }
        catch (HttpRequestException ex)
        {
            throw new MaxioBillingUnavailableException("The billing provider could not be reached.", ex);
        }
        catch (TaskCanceledException ex)
        {
            throw new MaxioBillingUnavailableException("The billing provider call timed out.", ex);
        }
    }

    private static SubscriptionProviderRejectedException TranslateRawError(SdkException<RawError> ex)
    {
        var status = (int)ex.Error.StatusCode;
        var message = SafeBody(ex.Error);
        return new SubscriptionProviderRejectedException(
            status,
            string.IsNullOrWhiteSpace(message)
                ? $"The billing provider rejected the request (HTTP {status})."
                : message);
    }

    private static SubscriptionProviderRejectedException TranslateRawError(RawError raw)
    {
        var status = (int)raw.StatusCode;
        var message = SafeBody(raw);
        return new SubscriptionProviderRejectedException(
            status,
            string.IsNullOrWhiteSpace(message)
                ? $"The billing provider rejected the request (HTTP {status})."
                : message);
    }

    private static Exception TranslateListProductsError(SdkException<ListProductsForProductFamilyError> ex)
    {
        if (ex.Error.TryGetString(out var message))
        {
            return new SubscriptionProviderRejectedException(404, message);
        }

        if (ex.Error.TryGetRawError(out var raw))
        {
            return TranslateRawError(raw);
        }

        return new MaxioBillingUnavailableException("The billing provider failed while listing plans.");
    }

    private static Exception TranslateCreateSubscriptionError(SdkException<CreateSubscriptionError> ex)
    {
        if (ex.Error.TryGetErrorListResponse1(out _))
        {
            return new SubscriptionProviderRejectedException(
                422, "The billing provider rejected the subscription.");
        }

        if (ex.Error.TryGetRawError(out var raw))
        {
            return TranslateRawError(raw);
        }

        return new MaxioBillingUnavailableException(
            "The billing provider failed while creating the subscription.");
    }

    private static string SafeBody(RawError raw)
    {
        try
        {
            var body = raw.ReadAsString();
            if (string.IsNullOrWhiteSpace(body))
            {
                return string.Empty;
            }

            return body.Length <= 300 ? body : body.Substring(0, 300);
        }
        catch
        {
            return string.Empty;
        }
    }
}
