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
using MaxioAdvancedBilling.Requests.Customers;
using MaxioAdvancedBilling.Requests.Subscriptions;
using Microsoft.AspNetCore.Http;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.Billing;

/// <summary>A Maxio subscription as shown to the shopper.</summary>
public record SubscriptionDetails(
    int? SubscriptionId,
    string? Reference,
    string? PlanHandle,
    string? PlanName,
    long? PriceInCents,
    string? Currency,
    int? Interval,
    string? IntervalUnit,
    string? State,
    DateTimeOffset? NextBillingAt,
    DateTimeOffset? CreatedAt);

public enum SubscribeOutcome
{
    /// <summary>This request created the subscription.</summary>
    Created,

    /// <summary>The buyer was already subscribed to this plan; nothing new was created.</summary>
    AlreadySubscribed,

    /// <summary>The create was sent but its outcome is not confirmed yet; it is settled on the next request.</summary>
    Pending
}

public record SubscribeResult(SubscribeOutcome Outcome, string Reference, string PlanHandle, SubscriptionDetails? Subscription);

/// <summary>An enrollment whose Maxio outcome is not confirmed yet.</summary>
public record PendingEnrollment(string Reference, string PlanHandle, string Status, DateTimeOffset ClaimedAt);

public record MySubscriptions(IReadOnlyList<SubscriptionDetails> Subscriptions, IReadOnlyList<PendingEnrollment> Pending);

public interface ISubscriptionBillingService
{
    Task<SubscriptionPlanCatalog> GetPlansAsync(CancellationToken requestAborted);

    Task<SubscribeResult> SubscribeAsync(string buyerId, string planHandle, CustomerName? name, CancellationToken requestAborted);

    Task<MySubscriptions> GetMySubscriptionsAsync(string buyerId, CancellationToken requestAborted);
}

public class MaxioSubscriptionBillingService : ISubscriptionBillingService
{
    private readonly MaxioAdvancedBillingClient _client;
    private readonly ISubscriptionPlanCatalog _plans;
    private readonly MaxioCustomerProvisioner _customers;
    private readonly IBillingClaimStore _claims;
    private readonly MaxioCallBudget _budget;
    private readonly TimeProvider _clock;
    private readonly MaxioAdvancedBilling.Models.Enums.CollectionMethod _collectionMethod;
    private readonly ILogger<MaxioSubscriptionBillingService> _logger;

    public MaxioSubscriptionBillingService(
        MaxioAdvancedBillingClient client,
        ISubscriptionPlanCatalog plans,
        MaxioCustomerProvisioner customers,
        IBillingClaimStore claims,
        MaxioCallBudget budget,
        TimeProvider clock,
        IOptions<MaxioOptions> options,
        ILogger<MaxioSubscriptionBillingService> logger)
    {
        // Validated at startup (MaxioOptionsValidator); this guard only fires if that validation is bypassed.
        _collectionMethod = MaxioAdvancedBilling.Models.Enums.CollectionMethod.TryGetKnownValue(options.Value.PaymentCollectionMethod, out var collectionMethod) && collectionMethod is not null
            ? collectionMethod
            : throw new InvalidOperationException("Maxio:PaymentCollectionMethod is not a known collection method.");
        _client = client;
        _plans = plans;
        _customers = customers;
        _claims = claims;
        _budget = budget;
        _clock = clock;
        _logger = logger;
    }

    /// <summary>
    /// The subscription reference sent to Maxio and used as the enrollment's primary key: one subscription per
    /// buyer and plan.
    /// </summary>
    public static string SubscriptionReferenceFor(string buyerId, string planHandle) => $"eshop:{buyerId}:{planHandle}";

    public Task<SubscriptionPlanCatalog> GetPlansAsync(CancellationToken requestAborted) =>
        WithinBudget("ListPlans", requestAborted, token => _plans.GetPlansAsync(token));

    public Task<SubscribeResult> SubscribeAsync(string buyerId, string planHandle, CustomerName? name, CancellationToken requestAborted) =>
        WithinBudget("Subscribe", requestAborted, token => SubscribeCoreAsync(buyerId, planHandle?.Trim() ?? string.Empty, name, token));

    public Task<MySubscriptions> GetMySubscriptionsAsync(string buyerId, CancellationToken requestAborted) =>
        WithinBudget("ListMySubscriptions", requestAborted, token => GetMySubscriptionsCoreAsync(buyerId, token));

    private async Task<SubscribeResult> SubscribeCoreAsync(string buyerId, string planHandle, CustomerName? name, CancellationToken cancellationToken)
    {
        if (planHandle.Length == 0)
        {
            throw new BillingException(StatusCodes.Status400BadRequest, "A plan handle is required.");
        }

        // The handle must be one this application offers: a plan listed for the configured product family.
        var catalog = await _plans.GetPlansAsync(cancellationToken);
        var plan = catalog.Find(planHandle);
        if (plan is null)
        {
            if (catalog.Truncated)
            {
                throw new BillingException(StatusCodes.Status503ServiceUnavailable, "The plan could not be verified right now. Please retry.");
            }

            throw new BillingException(StatusCodes.Status400BadRequest, $"'{planHandle}' is not an available subscription plan. See GET /api/subscription-plans.");
        }

        var customerId = await _customers.EnsureCustomerAsync(buyerId, name, cancellationToken);
        var reference = SubscriptionReferenceFor(buyerId, plan.Handle);
        if (customerId is not int maxioCustomerId)
        {
            return new SubscribeResult(SubscribeOutcome.Pending, reference, plan.Handle, null);
        }

        var claim = new SubscriptionEnrollment(reference, buyerId, plan.Handle, _clock.GetUtcNow());
        if (!await _claims.TryClaimAsync(claim, cancellationToken))
        {
            return await ResolveHeldEnrollmentAsync(reference, maxioCustomerId, cancellationToken);
        }

        // Claim won. A subscription may already exist in Maxio (e.g. the local store was reset) — never create twice.
        MaxioAdvancedBilling.Models.Subscription? existing;
        try
        {
            existing = await FindByReferenceAsync(reference, cancellationToken);
        }
        catch
        {
            // Nothing was sent: free the claim so the shopper can retry.
            await _claims.ReleaseAsync(claim, CancellationToken.None);
            throw;
        }

        if (existing is not null)
        {
            EnsureOwnedBy(existing, maxioCustomerId, reference);
            await RecordCreatedAsync(claim, existing, cancellationToken);
            return new SubscribeResult(SubscribeOutcome.AlreadySubscribed, reference, plan.Handle, ToDetails(existing));
        }

        return await CreateAsync(claim, maxioCustomerId, cancellationToken);
    }

    /// <summary>Another request holds (or held) the enrollment claim for this buyer + plan.</summary>
    private async Task<SubscribeResult> ResolveHeldEnrollmentAsync(string reference, int maxioCustomerId, CancellationToken cancellationToken)
    {
        var held = await _claims.GetEnrollmentAsync(reference, cancellationToken)
            ?? throw new BillingException(StatusCodes.Status409Conflict, "A subscription request for this plan is in progress. Please retry shortly.");

        var found = await FindByReferenceAsync(reference, cancellationToken);
        if (found is not null)
        {
            EnsureOwnedBy(found, maxioCustomerId, reference);
            if (!held.IsSettled)
            {
                await RecordCreatedAsync(held, found, cancellationToken);
            }

            return new SubscribeResult(SubscribeOutcome.AlreadySubscribed, reference, held.PlanHandle, ToDetails(found));
        }

        if (held.IsSettled)
        {
            // Recorded locally but Maxio no longer returns it by reference.
            _logger.LogWarning("Enrollment {SubscriptionReference} is recorded as subscription {MaxioSubscriptionId} but Maxio did not find it by reference.", reference, held.ProviderSubscriptionId);
            throw new BillingException(StatusCodes.Status409Conflict, "You are already subscribed to this plan.");
        }

        if (!held.IsStale(_clock.GetUtcNow(), _budget.StaleClaimAfter))
        {
            _logger.LogInformation("Enrollment claim {SubscriptionReference} is held by an in-flight request.", reference);
            throw new BillingException(StatusCodes.Status409Conflict, "A subscription request for this plan is in progress. Please retry shortly.");
        }

        held.TakeOver(_clock.GetUtcNow());
        if (!await _claims.TryUpdateAsync(held, cancellationToken))
        {
            throw new BillingException(StatusCodes.Status409Conflict, "A subscription request for this plan is in progress. Please retry shortly.");
        }

        _logger.LogWarning("Took over stale enrollment claim {SubscriptionReference}.", reference);
        return await CreateAsync(held, maxioCustomerId, cancellationToken);
    }

    private async Task<SubscribeResult> CreateAsync(SubscriptionEnrollment claim, int maxioCustomerId, CancellationToken cancellationToken)
    {
        const string operation = "CreateSubscription";
        try
        {
            var response = await _client.Subscriptions.CreateSubscription(
                new CreateSubscriptionOperationRequest
                {
                    Body = new MaxioAdvancedBilling.Models.CreateSubscriptionRequest
                    {
                        Subscription = new MaxioAdvancedBilling.Models.CreateSubscription
                        {
                            ProductHandle = claim.PlanHandle,
                            CustomerId = maxioCustomerId,
                            Reference = claim.Reference,
                            // No payment method is captured by this flow, so collection must not be card-automatic.
                            PaymentCollectionMethod = _collectionMethod
                        }
                    }
                },
                cancellationToken: cancellationToken);

            if (response.Subscription is { Id: int } created)
            {
                await RecordCreatedAsync(claim, created, cancellationToken);
                _logger.LogInformation("Created Maxio subscription {MaxioSubscriptionId} for {SubscriptionReference}.", created.Id, claim.Reference);
                return new SubscribeResult(SubscribeOutcome.Created, claim.Reference, claim.PlanHandle, ToDetails(created));
            }

            _logger.LogWarning("Maxio {Operation} succeeded without a subscription for {SubscriptionReference}; settling by reference.", operation, claim.Reference);
            return await SettleUnknownAsync(claim, maxioCustomerId);
        }
        catch (ApiException<CreateSubscriptionError> ex)
        {
            IReadOnlyList<string>? errors = null;
            if (ex.Error.TryGetErrorListResponse1(out var errorList))
            {
                errors = errorList.Errors.Where(e => !string.IsNullOrWhiteSpace(e)).ToArray();
            }
            else if (ex.Error.TryGetRawError(out _))
            {
                // Status without a typed body: no caller-safe messages to carry.
            }

            if ((int)ex.StatusCode >= 500)
            {
                // A provider-side failure may still have created the subscription.
                return await SettleUnknownAsync(claim, maxioCustomerId);
            }

            // A definite refusal: nothing was created, free the claim.
            await _claims.ReleaseAsync(claim, CancellationToken.None);
            throw MaxioFailures.FromStatus(_logger, operation, ex.StatusCode, errors, ex);
        }
        catch (ResponseDeserializationException ex) when ((int)ex.StatusCode is >= 200 and < 300)
        {
            _logger.LogWarning("Maxio {Operation} returned an unreadable {StatusCode} body for {SubscriptionReference}; settling by reference.", operation, (int)ex.StatusCode, claim.Reference);
            return await SettleUnknownAsync(claim, maxioCustomerId);
        }
        catch (SdkConnectionException ex)
        {
            // Includes SdkTimeoutException: the create may have landed.
            _logger.LogWarning("Maxio {Operation} lost its connection for {SubscriptionReference} ({ExceptionType}); settling by reference.", operation, claim.Reference, ex.GetType().Name);
            return await SettleUnknownAsync(claim, maxioCustomerId);
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("Maxio {Operation} was cancelled in flight for {SubscriptionReference}; settling by reference.", operation, claim.Reference);
            return await SettleUnknownAsync(claim, maxioCustomerId);
        }
        catch (SdkException ex)
        {
            // Refused before or without acting (unreadable error body, credentials not applied).
            await _claims.ReleaseAsync(claim, CancellationToken.None);
            throw MaxioFailures.FromSdk(_logger, operation, ex);
        }
    }

    /// <summary>
    /// The create's outcome is unknown: re-read by the reference it carried. Found → recorded as created. Not found
    /// or unreadable → the claim is marked Unknown; the next request for this plan settles it before creating.
    /// </summary>
    private async Task<SubscribeResult> SettleUnknownAsync(SubscriptionEnrollment claim, int maxioCustomerId)
    {
        MaxioAdvancedBilling.Models.Subscription? found = null;
        using (var settlement = _budget.StartSettlement())
        {
            try
            {
                found = await FindByReferenceAsync(claim.Reference, settlement.Token);
            }
            catch (BillingException)
            {
            }
            catch (OperationCanceledException)
            {
            }
        }

        if (found is not null && found.Customer?.Id is int owner && owner == maxioCustomerId)
        {
            await RecordCreatedAsync(claim, found, CancellationToken.None);
            _logger.LogInformation("Settled unknown Maxio subscription create {SubscriptionReference}: subscription {MaxioSubscriptionId} exists.", claim.Reference, found.Id);
            return new SubscribeResult(SubscribeOutcome.Created, claim.Reference, claim.PlanHandle, ToDetails(found));
        }

        claim.MarkUnknown(_clock.GetUtcNow());
        await _claims.TryUpdateAsync(claim, CancellationToken.None);
        _logger.LogWarning("Maxio subscription create {SubscriptionReference} has an unknown outcome; left for the next request to settle.", claim.Reference);
        return new SubscribeResult(SubscribeOutcome.Pending, claim.Reference, claim.PlanHandle, null);
    }

    /// <summary>The subscription carrying <paramref name="reference"/>, or <c>null</c> when Maxio has none.</summary>
    private async Task<MaxioAdvancedBilling.Models.Subscription?> FindByReferenceAsync(string reference, CancellationToken cancellationToken)
    {
        const string operation = "FindSubscription";
        try
        {
            var response = await _client.Subscriptions.FindSubscription(
                new FindSubscriptionRequest { Reference = reference },
                cancellationToken: cancellationToken);

            // A 2xx without a subscription is an unreadable answer, not "none".
            return response.Subscription
                ?? throw new BillingException(StatusCodes.Status502BadGateway, "The billing provider returned a response that could not be processed.");
        }
        catch (ApiException<FindSubscriptionError> ex)
        {
            if (ex.Error.TryGetNoContent(out _))
            {
                return null;
            }

            if (ex.Error.TryGetRawError(out var raw))
            {
                throw MaxioFailures.FromStatus(_logger, operation, raw.StatusCode, null, ex);
            }

            throw MaxioFailures.FromStatus(_logger, operation, ex.StatusCode, null, ex);
        }
        catch (SdkException ex)
        {
            throw MaxioFailures.FromSdk(_logger, operation, ex);
        }
    }

    private async Task<MySubscriptions> GetMySubscriptionsCoreAsync(string buyerId, CancellationToken cancellationToken)
    {
        var unsettled = await _claims.ListUnsettledEnrollmentsAsync(buyerId, cancellationToken);
        var customerId = await _customers.FindCustomerIdAsync(buyerId, cancellationToken);

        var subscriptions = new List<MaxioAdvancedBilling.Models.Subscription>();
        if (customerId is int maxioCustomerId)
        {
            subscriptions.AddRange(await ListCustomerSubscriptionsAsync(maxioCustomerId, cancellationToken));
        }

        // Settle any unconfirmed enrollment that Maxio now shows.
        var pending = new List<PendingEnrollment>();
        foreach (var enrollment in unsettled)
        {
            var match = subscriptions.FirstOrDefault(s => string.Equals(s.Reference, enrollment.Reference, StringComparison.Ordinal));
            if (match is { Id: int })
            {
                await RecordCreatedAsync(enrollment, match, cancellationToken);
                continue;
            }

            pending.Add(new PendingEnrollment(enrollment.Reference, enrollment.PlanHandle, enrollment.Status.ToString(), enrollment.ClaimedAt));
        }

        return new MySubscriptions(subscriptions.Select(ToDetails).ToList(), pending);
    }

    private async Task<IReadOnlyList<MaxioAdvancedBilling.Models.Subscription>> ListCustomerSubscriptionsAsync(int customerId, CancellationToken cancellationToken)
    {
        const string operation = "ListCustomerSubscriptions";
        try
        {
            var response = await _client.Customers.ListCustomerSubscriptions(
                new ListCustomerSubscriptionsRequest { CustomerId = customerId },
                cancellationToken: cancellationToken);

            return response
                .Select(r => r.Subscription)
                .Where(s => s is not null)
                .Select(s => s!)
                .ToList();
        }
        catch (ApiException<RawError> ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            // The linked customer no longer exists in Maxio.
            _logger.LogWarning("Maxio customer {MaxioCustomerId} was not found while listing subscriptions.", customerId);
            return Array.Empty<MaxioAdvancedBilling.Models.Subscription>();
        }
        catch (SdkException ex)
        {
            throw MaxioFailures.FromSdk(_logger, operation, ex);
        }
    }

    private void EnsureOwnedBy(MaxioAdvancedBilling.Models.Subscription subscription, int maxioCustomerId, string reference)
    {
        if (subscription.Customer?.Id is int owner && owner != maxioCustomerId)
        {
            _logger.LogError("Maxio subscription {MaxioSubscriptionId} with reference {SubscriptionReference} belongs to customer {OwnerCustomerId}, not {MaxioCustomerId}.", subscription.Id, reference, owner, maxioCustomerId);
            throw new BillingException(StatusCodes.Status409Conflict, "This subscription reference is already in use by another billing account.");
        }
    }

    private async Task RecordCreatedAsync(SubscriptionEnrollment claim, MaxioAdvancedBilling.Models.Subscription subscription, CancellationToken cancellationToken)
    {
        if (subscription.Id is not int id)
        {
            return;
        }

        claim.MarkCreated(id, _clock.GetUtcNow());
        if (!await _claims.TryUpdateAsync(claim, cancellationToken))
        {
            _logger.LogWarning("Enrollment claim {SubscriptionReference} changed while being recorded; the other holder will record it.", claim.Reference);
        }
    }

    internal static SubscriptionDetails ToDetails(MaxioAdvancedBilling.Models.Subscription subscription)
    {
        var product = subscription.Product;
        return new SubscriptionDetails(
            subscription.Id,
            subscription.Reference,
            product?.Handle,
            product?.Name,
            subscription.ProductPriceInCents ?? product?.PriceInCents,
            subscription.Currency,
            product?.Interval,
            product?.IntervalUnit?.Value,
            subscription.State?.Value,
            // current_period_ends_at is when the next regularly scheduled charge occurs; next_assessment_at is
            // when capture is (re)tried — the fallback when the period end is absent.
            subscription.CurrentPeriodEndsAt ?? subscription.NextAssessmentAt,
            subscription.CreatedAt);
    }

    /// <summary>
    /// Runs one API request's Maxio work under the operation budget. The budget elapsing (as opposed to the caller
    /// disconnecting) becomes a 504 rather than an unhandled cancellation.
    /// </summary>
    private async Task<T> WithinBudget<T>(string operation, CancellationToken requestAborted, Func<CancellationToken, Task<T>> work)
    {
        using var budget = _budget.Start(requestAborted);
        try
        {
            return await work(budget.Token);
        }
        catch (OperationCanceledException ex) when (budget.IsCancellationRequested && !requestAborted.IsCancellationRequested)
        {
            throw MaxioFailures.BudgetExceeded(_logger, operation, ex);
        }
    }
}
