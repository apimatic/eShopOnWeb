using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.ErrorResponse;
using MaxioAdvancedBilling.Core.Exceptions;
using MaxioAdvancedBilling.Errors;
using MaxioAdvancedBilling.Models;
using MaxioAdvancedBilling.Models.Enums;
using MaxioAdvancedBilling.Requests.Customers;
using MaxioAdvancedBilling.Requests.Subscriptions;
using Microsoft.AspNetCore.Identity;
using Microsoft.eShopWeb.ApplicationCore.Billing;
using Microsoft.eShopWeb.ApplicationCore.Entities.BillingAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Identity;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Billing;

/// <summary>
/// Subscription billing with Maxio Advanced Billing as the system of record.
/// Every write follows claim (local DB, insert-or-fail) → Maxio call → record the result, and a write whose
/// outcome is unknown (connection lost, timeout, unreadable 2xx) is settled by re-reading Maxio by our reference.
/// </summary>
public class MaxioSubscriptionBillingService : ISubscriptionBillingService
{
    /// <summary>Total time a caller-facing operation may spend on Maxio calls.</summary>
    internal static readonly TimeSpan OperationBudget = TimeSpan.FromSeconds(30);
    /// <summary>Budget for re-reading Maxio to settle an unknown outcome; independent of the caller's request.</summary>
    internal static readonly TimeSpan SettleBudget = TimeSpan.FromSeconds(10);
    /// <summary>A Pending claim older than this cannot belong to a live request (budget + settle &lt; 1 min).</summary>
    internal static readonly TimeSpan PendingStaleAfter = TimeSpan.FromMinutes(2);
    /// <summary>How long an Unknown enrollment that Maxio does not know is kept before the claim is released.</summary>
    internal static readonly TimeSpan UnknownGracePeriod = TimeSpan.FromMinutes(5);

    private const string PendingMessage =
        "Your subscription is being confirmed with the billing service. Check your subscriptions again shortly.";

    private readonly MaxioAdvancedBillingClient _client;
    private readonly MaxioPlanCatalog _catalog;
    private readonly EnrollmentStore _store;
    private readonly UserManager<ApplicationUser> _users;
    private readonly MaxioSettings _settings;
    private readonly TimeProvider _time;
    private readonly ILogger<MaxioSubscriptionBillingService> _logger;

    public MaxioSubscriptionBillingService(
        MaxioAdvancedBillingClient client,
        MaxioPlanCatalog catalog,
        EnrollmentStore store,
        UserManager<ApplicationUser> users,
        IOptions<MaxioSettings> settings,
        TimeProvider time,
        ILogger<MaxioSubscriptionBillingService> logger)
    {
        _client = client;
        _catalog = catalog;
        _store = store;
        _users = users;
        _settings = settings.Value;
        _time = time;
        _logger = logger;
    }

    private string FamilyHandle => _settings.ProductFamilyHandle!;
    private DateTimeOffset Now => _time.GetUtcNow();

    internal static string CustomerReference(string userId) => $"eshop-user-{userId}";
    internal string SubscriptionReference(string userId) => $"eshop-sub-{userId}-{FamilyHandle}";

    public Task<SubscriptionPlanList> GetPlansAsync(CancellationToken cancellationToken = default) =>
        BoundedAsync(cancellationToken, token => _catalog.GetPlansAsync(token));

    public async Task<SubscribeResult> SubscribeAsync(string userName, string planHandle, string? firstName, string? lastName,
        CancellationToken cancellationToken = default)
    {
        var user = await _users.FindByNameAsync(userName);
        if (user is null)
        {
            return new SubscribeResult(SubscribeOutcome.UserNotFound, Message: "Unknown user.");
        }

        using var scope = _logger.BeginScope(new Dictionary<string, object> { ["UserId"] = user.Id, ["PlanHandle"] = planHandle });
        return await BoundedAsync(cancellationToken,
            token => SubscribeCoreAsync(user, planHandle.Trim(), firstName, lastName, token, cancellationToken));
    }

    public async Task<MySubscriptionsResult> GetMySubscriptionsAsync(string userName, CancellationToken cancellationToken = default)
    {
        var user = await _users.FindByNameAsync(userName);
        if (user is null)
        {
            return new MySubscriptionsResult(false, Array.Empty<SubscriptionDetails>());
        }

        return await BoundedAsync(cancellationToken, async token =>
        {
            await SettleUnsettledEnrollmentsAsync(user.Id, token);

            var local = await _store.FindCustomerAsync(user.Id, token);
            var customerId = local?.BillingCustomerId ?? await TryFindCustomerAsync(CustomerReference(user.Id), token);
            if (customerId is not int id)
            {
                return new MySubscriptionsResult(true, Array.Empty<SubscriptionDetails>());
            }

            IReadOnlyList<SubscriptionResponse> subscriptions;
            try
            {
                subscriptions = await _client.Customers.ListCustomerSubscriptions(
                    new ListCustomerSubscriptionsRequest { CustomerId = id },
                    cancellationToken: token);
            }
            catch (SdkException ex)
            {
                _logger.LogError(ex, "Listing Maxio subscriptions of customer {CustomerId} failed", id);
                throw MaxioErrorTranslator.Translate(ex);
            }

            var planHandles = (await _catalog.GetPlansAsync(token)).Plans.Select(p => p.Handle).ToHashSet(StringComparer.Ordinal);
            var mine = subscriptions
                .Select(s => s.Subscription)
                .Where(s => s?.Id is not null && BelongsToFamily(s, planHandles))
                .OrderByDescending(s => s!.CreatedAt)
                .Select(s => ToDetails(s!))
                .ToList();
            return new MySubscriptionsResult(true, mine);
        });
    }

    /// <summary>
    /// Settles enrollments whose outcome is unknown (or whose claiming request died) by re-reading Maxio.
    /// Called for the user's own rows on each request and for all rows by <see cref="BillingReconciliationService"/>.
    /// </summary>
    public async Task<int> SettleUnsettledEnrollmentsAsync(string? userId, CancellationToken cancellationToken)
    {
        var unsettled = await _store.ListUnsettledAsync(userId, Now - PendingStaleAfter, max: 50, cancellationToken);
        foreach (var enrollment in unsettled)
        {
            await SettleEnrollmentAsync(enrollment);
        }
        return unsettled.Count;
    }

    private async Task<SubscribeResult> SubscribeCoreAsync(ApplicationUser user, string planHandle, string? firstName,
        string? lastName, CancellationToken token, CancellationToken callerToken)
    {
        // Cross-operation invariant: the plan must be one the family's product list offers.
        var plan = await _catalog.FindPlanAsync(planHandle, token);
        if (plan is null)
        {
            return new SubscribeResult(SubscribeOutcome.UnknownPlan, Message: $"'{planHandle}' is not an available subscription plan.");
        }

        var reference = SubscriptionReference(user.Id);
        for (var attempt = 0; attempt < 3; attempt++)
        {
            var claim = await _store.TryClaimEnrollmentAsync(
                new SubscriptionEnrollment(user.Id, FamilyHandle, plan.Handle, reference, Now), token);
            if (claim.Claimed)
            {
                return await EnrollAsync(user, plan, claim.Entity!, firstName, lastName, token, callerToken);
            }

            var existing = claim.Entity;
            if (existing is null)
            {
                continue;   // the holder released it in between — claim again
            }

            if (IsUnsettled(existing))
            {
                var (settled, _) = await SettleEnrollmentAsync(existing);
                if (settled == SettleOutcome.Released)
                {
                    continue;
                }
            }

            if (existing.State != EnrollmentState.Active)
            {
                _logger.LogInformation("Subscribe for user {UserId} is already in progress (enrollment {State})", user.Id, existing.State);
                return new SubscribeResult(SubscribeOutcome.Pending, Message: PendingMessage);
            }

            var current = await ReadSubscriptionAsync(existing.BillingSubscriptionId!.Value, token);
            if (current is null || IsEnded(current))
            {
                // The earlier subscription was canceled/expired (or removed) in Maxio: free the slot for a new one.
                _logger.LogInformation("Releasing ended enrollment {Reference} for user {UserId}", existing.Reference, user.Id);
                await _store.ReleaseEnrollmentAsync(existing);
                continue;
            }

            if (string.Equals(existing.PlanHandle, plan.Handle, StringComparison.Ordinal))
            {
                _logger.LogInformation("User {UserId} is already subscribed to {PlanHandle}; returning subscription {SubscriptionId}",
                    user.Id, plan.Handle, current.Id);
                return new SubscribeResult(SubscribeOutcome.AlreadySubscribed, ToDetails(current));
            }

            return new SubscribeResult(SubscribeOutcome.SubscribedToOtherPlan, ToDetails(current),
                Message: $"You already have a subscription to '{existing.PlanHandle}'. Changing plans is not supported here.");
        }

        return new SubscribeResult(SubscribeOutcome.Pending, Message: PendingMessage);
    }

    private async Task<SubscribeResult> EnrollAsync(ApplicationUser user, SubscriptionPlan plan, SubscriptionEnrollment enrollment,
        string? firstName, string? lastName, CancellationToken token, CancellationToken callerToken)
    {
        int customerId;
        try
        {
            customerId = await EnsureCustomerAsync(user, firstName, lastName, token, callerToken);
        }
        catch (CustomerRejectedException rejected)
        {
            await _store.ReleaseEnrollmentAsync(enrollment);
            return new SubscribeResult(SubscribeOutcome.Rejected, Errors: rejected.Errors,
                Message: "The billing service rejected the customer details.");
        }
        catch
        {
            // No subscription write happened: free the slot so the user can retry.
            await _store.ReleaseEnrollmentAsync(enrollment);
            throw;
        }

        return await CreateSubscriptionAsync(enrollment, plan, customerId, token, callerToken);
    }

    private async Task<SubscribeResult> CreateSubscriptionAsync(SubscriptionEnrollment enrollment, SubscriptionPlan plan,
        int customerId, CancellationToken token, CancellationToken callerToken)
    {
        _logger.LogInformation("Creating Maxio subscription {Reference} to {PlanHandle} for customer {CustomerId}",
            enrollment.Reference, plan.Handle, customerId);

        SubscriptionResponse response;
        try
        {
            response = await _client.Subscriptions.CreateSubscription(
                new CreateSubscriptionOperationRequest
                {
                    Body = new CreateSubscriptionRequest
                    {
                        Subscription = new CreateSubscription
                        {
                            ProductHandle = plan.Handle,
                            CustomerId = customerId,
                            Reference = enrollment.Reference,
                            // The storefront captures no payment method, so the subscription is collected by
                            // invoice (remittance) instead of an automatic card charge at signup.
                            PaymentCollectionMethod = CollectionMethod.Remittance
                        }
                    }
                },
                cancellationToken: token);
        }
        catch (ApiException<CreateSubscriptionError> ex)
        {
            await _store.ReleaseEnrollmentAsync(enrollment);
            if (ex.Error.TryGetErrorListResponse1(out var rejected))
            {
                _logger.LogWarning(ex, "Maxio rejected subscription {Reference} (HTTP {Status}): {Errors}",
                    enrollment.Reference, (int)ex.StatusCode, string.Join("; ", rejected.Errors));
                return new SubscribeResult(SubscribeOutcome.Rejected, Errors: rejected.Errors,
                    Message: "The billing service rejected the subscription.");
            }
            if (ex.Error.TryGetRawError(out RawError raw))
            {
                _logger.LogError(ex, "Creating Maxio subscription {Reference} failed with HTTP {Status}",
                    enrollment.Reference, (int)raw.StatusCode);
            }
            throw MaxioErrorTranslator.FromStatus(ex.StatusCode, ex);
        }
        catch (ResponseDeserializationException ex) when (!MaxioErrorTranslator.IsSuccess(ex.StatusCode))
        {
            // Maxio refused the call; only the error detail was unreadable.
            await _store.ReleaseEnrollmentAsync(enrollment);
            _logger.LogError(ex, "Creating Maxio subscription {Reference} failed with unreadable HTTP {Status}",
                enrollment.Reference, (int)ex.StatusCode);
            throw MaxioErrorTranslator.FromStatus(ex.StatusCode, ex);
        }
        catch (AuthSchemeException ex)
        {
            await _store.ReleaseEnrollmentAsync(enrollment);
            _logger.LogError(ex, "Maxio credentials could not be applied");
            throw MaxioErrorTranslator.Translate(ex);
        }
        catch (Exception ex) when (ex is SdkConnectionException or ResponseDeserializationException or OperationCanceledException)
        {
            // The request may have reached Maxio: settle by re-reading instead of reporting a failure.
            _logger.LogError(ex, "Outcome of creating Maxio subscription {Reference} is unknown; re-reading by reference",
                enrollment.Reference);
            var result = await SettleUnknownCreateAsync(enrollment);
            if (callerToken.IsCancellationRequested)
            {
                throw;
            }
            return result;
        }

        var subscription = response.Subscription;
        if (subscription?.Id is not int subscriptionId)
        {
            _logger.LogError("Maxio returned no subscription id for {Reference}; re-reading by reference", enrollment.Reference);
            return await SettleUnknownCreateAsync(enrollment);
        }

        enrollment.MarkActive(subscriptionId, customerId, Now);
        await _store.SaveAsync();
        _logger.LogInformation("Created Maxio subscription {SubscriptionId} ({Reference}) in state {State}",
            subscriptionId, enrollment.Reference, subscription.State?.Value);
        return new SubscribeResult(SubscribeOutcome.Created, ToDetails(subscription));
    }

    private async Task<SubscribeResult> SettleUnknownCreateAsync(SubscriptionEnrollment enrollment)
    {
        enrollment.MarkUnknown();
        await _store.SaveAsync();

        var (outcome, subscription) = await SettleEnrollmentAsync(enrollment);
        return outcome switch
        {
            SettleOutcome.Active when subscription is not null => new SubscribeResult(SubscribeOutcome.Created, ToDetails(subscription)),
            SettleOutcome.Released => throw new BillingProviderException(503, MaxioErrorTranslator.UnavailableMessage),
            _ => new SubscribeResult(SubscribeOutcome.Pending, Message: PendingMessage),
        };
    }

    private enum SettleOutcome { Active, Released, StillUnknown }

    private bool IsUnsettled(SubscriptionEnrollment enrollment) =>
        enrollment.State == EnrollmentState.Unknown
        || (enrollment.State == EnrollmentState.Pending && enrollment.ClaimedAt < Now - PendingStaleAfter);

    /// <summary>
    /// Settles one enrollment by looking its subscription up by our reference. Found → Active. Not found → the claim
    /// is released once no create can still be landing (stale Pending, or Unknown past the grace period); otherwise
    /// it stays Unknown for the next settlement pass.
    /// </summary>
    private async Task<(SettleOutcome, Subscription?)> SettleEnrollmentAsync(SubscriptionEnrollment enrollment)
    {
        using var settleBudget = new CancellationTokenSource(SettleBudget);
        try
        {
            var found = await _client.Subscriptions.FindSubscription(
                new FindSubscriptionRequest { Reference = enrollment.Reference },
                cancellationToken: settleBudget.Token);

            var subscription = found.Subscription;
            if (subscription?.Id is int subscriptionId
                && (subscription.Reference is null || string.Equals(subscription.Reference, enrollment.Reference, StringComparison.Ordinal)))
            {
                enrollment.MarkActive(subscriptionId, subscription.Customer?.Id ?? enrollment.BillingCustomerId, Now);
                await _store.SaveAsync();
                _logger.LogInformation("Settled enrollment {Reference}: Maxio subscription {SubscriptionId} exists",
                    enrollment.Reference, subscriptionId);
                return (SettleOutcome.Active, subscription);
            }

            _logger.LogWarning("Maxio lookup for {Reference} returned a subscription that does not carry our reference",
                enrollment.Reference);
        }
        catch (ApiException<FindSubscriptionError> ex)
        {
            if (!ex.Error.TryGetNoContent(out _))
            {
                if (ex.Error.TryGetRawError(out RawError raw))
                {
                    _logger.LogError(ex, "Settling enrollment {Reference}: lookup failed with HTTP {Status}",
                        enrollment.Reference, (int)raw.StatusCode);
                }
                return (SettleOutcome.StillUnknown, null);
            }
            // 404: Maxio has no subscription with our reference (yet).
        }
        catch (SdkException ex)
        {
            _logger.LogError(ex, "Settling enrollment {Reference}: lookup failed", enrollment.Reference);
            return (SettleOutcome.StillUnknown, null);
        }
        catch (OperationCanceledException ex)
        {
            _logger.LogError(ex, "Settling enrollment {Reference}: lookup timed out", enrollment.Reference);
            return (SettleOutcome.StillUnknown, null);
        }

        var age = Now - enrollment.ClaimedAt;
        var canRelease = enrollment.State == EnrollmentState.Pending
            ? age > PendingStaleAfter
            : age > UnknownGracePeriod;
        if (canRelease)
        {
            _logger.LogWarning("Releasing enrollment {Reference} ({State}, age {Age}): Maxio has no such subscription",
                enrollment.Reference, enrollment.State, age);
            await _store.ReleaseEnrollmentAsync(enrollment);
            return (SettleOutcome.Released, null);
        }

        enrollment.MarkUnknown();
        await _store.SaveAsync();
        return (SettleOutcome.StillUnknown, null);
    }

    /// <summary>Returns the user's Maxio customer id, creating the customer (claimed, idempotent by reference) if needed.</summary>
    private async Task<int> EnsureCustomerAsync(ApplicationUser user, string? firstName, string? lastName,
        CancellationToken token, CancellationToken callerToken)
    {
        var reference = CustomerReference(user.Id);
        var claim = await _store.TryClaimCustomerAsync(new BillingCustomer(user.Id, reference, Now), token);
        var customer = claim.Entity
            ?? throw new BillingProviderException(503, MaxioErrorTranslator.UnavailableMessage);
        if (customer.IsProvisioned)
        {
            return customer.BillingCustomerId!.Value;
        }

        try
        {
            // Look up by reference first: reuses a customer that already exists for this user and settles an
            // earlier create whose outcome was unknown.
            var existingId = await TryFindCustomerAsync(reference, token);
            if (existingId is int id)
            {
                customer.MarkProvisioned(id, Now);
                await _store.SaveAsync();
                return id;
            }

            return await CreateCustomerAsync(user, customer, firstName, lastName, token, callerToken);
        }
        catch
        {
            // Nothing is known to exist in Maxio for this claim; the next attempt looks up by reference before creating.
            await _store.ReleaseCustomerAsync(customer);
            throw;
        }
    }

    private async Task<int> CreateCustomerAsync(ApplicationUser user, BillingCustomer customer, string? firstName,
        string? lastName, CancellationToken token, CancellationToken callerToken)
    {
        var email = user.Email ?? user.UserName!;
        var (first, last) = ResolveNames(email, firstName, lastName);
        _logger.LogInformation("Creating Maxio customer {Reference} for user {UserId}", customer.Reference, user.Id);

        CustomerResponse response;
        try
        {
            response = await _client.Customers.CreateCustomer(
                new CreateCustomerOperationRequest
                {
                    Body = new CreateCustomerRequest
                    {
                        Customer = new CreateCustomer
                        {
                            FirstName = first,
                            LastName = last,
                            Email = email,
                            Reference = customer.Reference
                        }
                    }
                },
                cancellationToken: token);
        }
        catch (ApiException<CreateCustomerError> ex)
        {
            if (ex.Error.TryGetCustomerErrorResponse1(out var rejected))
            {
                var errors = DescribeCustomerErrors(rejected);
                _logger.LogWarning(ex, "Maxio rejected customer {Reference} (HTTP {Status}): {Errors}",
                    customer.Reference, (int)ex.StatusCode, string.Join("; ", errors));
                throw new CustomerRejectedException(errors);
            }
            if (ex.Error.TryGetRawError(out RawError raw))
            {
                _logger.LogError(ex, "Creating Maxio customer {Reference} failed with HTTP {Status}",
                    customer.Reference, (int)raw.StatusCode);
            }
            throw MaxioErrorTranslator.FromStatus(ex.StatusCode, ex);
        }
        catch (ResponseDeserializationException ex) when (!MaxioErrorTranslator.IsSuccess(ex.StatusCode))
        {
            _logger.LogError(ex, "Creating Maxio customer {Reference} failed with unreadable HTTP {Status}",
                customer.Reference, (int)ex.StatusCode);
            throw MaxioErrorTranslator.FromStatus(ex.StatusCode, ex);
        }
        catch (AuthSchemeException ex)
        {
            _logger.LogError(ex, "Maxio credentials could not be applied");
            throw MaxioErrorTranslator.Translate(ex);
        }
        catch (Exception ex) when (ex is SdkConnectionException or ResponseDeserializationException or OperationCanceledException)
        {
            // The customer may have been created: re-read by our reference before reporting anything.
            _logger.LogError(ex, "Outcome of creating Maxio customer {Reference} is unknown; re-reading by reference",
                customer.Reference);
            var settledId = await SettleCustomerAsync(customer);
            if (settledId is int id)
            {
                return id;
            }
            if (callerToken.IsCancellationRequested)
            {
                throw;
            }
            throw new BillingProviderException(503, MaxioErrorTranslator.UnavailableMessage, null, ex);
        }

        if (response.Customer.Id is not int customerId)
        {
            _logger.LogError("Maxio returned no customer id for {Reference}; re-reading by reference", customer.Reference);
            return await SettleCustomerAsync(customer)
                ?? throw new BillingProviderException(503, MaxioErrorTranslator.UnavailableMessage);
        }

        customer.MarkProvisioned(customerId, Now);
        await _store.SaveAsync();
        _logger.LogInformation("Created Maxio customer {CustomerId} ({Reference})", customerId, customer.Reference);
        return customerId;
    }

    private async Task<int?> SettleCustomerAsync(BillingCustomer customer)
    {
        using var settleBudget = new CancellationTokenSource(SettleBudget);
        try
        {
            var id = await TryFindCustomerAsync(customer.Reference, settleBudget.Token);
            if (id is int found)
            {
                customer.MarkProvisioned(found, Now);
                await _store.SaveAsync();
                _logger.LogInformation("Settled customer {Reference}: Maxio customer {CustomerId} exists", customer.Reference, found);
            }
            return id;
        }
        catch (Exception ex) when (ex is BillingProviderException or OperationCanceledException)
        {
            _logger.LogError(ex, "Settling customer {Reference}: lookup failed", customer.Reference);
            return null;
        }
    }

    /// <summary>Looks a customer up by our reference; null when Maxio has none.</summary>
    private async Task<int?> TryFindCustomerAsync(string reference, CancellationToken token)
    {
        try
        {
            var found = await _client.Customers.ReadCustomerByReference(
                new ReadCustomerByReferenceRequest { Reference = reference },
                cancellationToken: token);
            var customer = found.Customer;
            if (customer.Id is int id
                && (customer.Reference is null || string.Equals(customer.Reference, reference, StringComparison.Ordinal)))
            {
                return id;
            }

            _logger.LogWarning("Maxio customer lookup for {Reference} returned a customer that does not carry our reference", reference);
            return null;
        }
        catch (ApiException<RawError> ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
        catch (SdkException ex)
        {
            _logger.LogError(ex, "Looking up Maxio customer {Reference} failed", reference);
            throw MaxioErrorTranslator.Translate(ex);
        }
    }

    private async Task<Subscription?> ReadSubscriptionAsync(int subscriptionId, CancellationToken token)
    {
        try
        {
            var response = await _client.Subscriptions.ReadSubscription(
                new ReadSubscriptionRequest { SubscriptionId = subscriptionId },
                cancellationToken: token);
            return response.Subscription;
        }
        catch (ApiException<RawError> ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
        catch (SdkException ex)
        {
            _logger.LogError(ex, "Reading Maxio subscription {SubscriptionId} failed", subscriptionId);
            throw MaxioErrorTranslator.Translate(ex);
        }
    }

    private async Task<T> BoundedAsync<T>(CancellationToken callerToken, Func<CancellationToken, Task<T>> work)
    {
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(callerToken);
        budget.CancelAfter(OperationBudget);
        try
        {
            return await work(budget.Token);
        }
        catch (OperationCanceledException ex) when (!callerToken.IsCancellationRequested)
        {
            throw new BillingProviderException(504, MaxioErrorTranslator.UnavailableMessage, null, ex);
        }
    }

    private bool BelongsToFamily(Subscription subscription, IReadOnlySet<string> planHandles)
    {
        var familyHandle = subscription.Product?.ProductFamily?.Handle;
        return familyHandle is not null
            ? string.Equals(familyHandle, FamilyHandle, StringComparison.Ordinal)
            : subscription.Product?.Handle is { } handle && planHandles.Contains(handle);
    }

    private static bool IsEnded(Subscription subscription) =>
        subscription.State is { } state && (state == SubscriptionState.Canceled || state == SubscriptionState.Expired);

    private static SubscriptionDetails ToDetails(Subscription subscription) => new(
        SubscriptionId: subscription.Id!.Value,
        PlanHandle: subscription.Product?.Handle,
        PlanName: subscription.Product?.Name,
        State: subscription.State?.Value,
        PriceInCents: subscription.ProductPriceInCents ?? subscription.Product?.PriceInCents,
        Currency: subscription.Currency,
        Interval: subscription.Product?.Interval,
        IntervalUnit: subscription.Product?.IntervalUnit?.Value,
        // current_period_ends_at is when the next regularly scheduled charge occurs.
        NextBillingAt: subscription.CurrentPeriodEndsAt ?? subscription.NextAssessmentAt,
        ActivatedAt: subscription.ActivatedAt,
        CreatedAt: subscription.CreatedAt);

    private static IReadOnlyList<string> DescribeCustomerErrors(CustomerErrorResponse1 body)
    {
        if (body.Errors is { } errors)
        {
            if (errors.TryGetListOfString(out var list))
            {
                return list;
            }
            if (errors.TryGetCustomerError(out var customerError) && customerError.Customer is { } message)
            {
                return new[] { message };
            }
        }
        return new[] { "The customer details were not accepted." };
    }

    internal static (string First, string Last) ResolveNames(string email, string? firstName, string? lastName)
    {
        var localPart = email.Split('@')[0];
        var first = string.IsNullOrWhiteSpace(firstName) ? localPart : firstName.Trim();
        var last = string.IsNullOrWhiteSpace(lastName) ? "Customer" : lastName.Trim();
        return (first, last);
    }

    private sealed class CustomerRejectedException : Exception
    {
        public CustomerRejectedException(IReadOnlyList<string> errors) : base("Customer rejected by Maxio.") => Errors = errors;
        public IReadOnlyList<string> Errors { get; }
    }
}
