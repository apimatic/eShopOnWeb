using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionBilling;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

/// <summary>
/// The subscription capability behind the PublicApi endpoints. Every public method runs all of its Maxio
/// calls under ONE request budget (<see cref="MaxioCallBudget"/>).
/// </summary>
public class MaxioSubscriptionService
{
    private readonly CatalogContext _db;
    private readonly MaxioGateway _gateway;
    private readonly MaxioPlanCatalog _catalog;
    private readonly MaxioCustomerService _customers;
    private readonly TimeProvider _time;
    private readonly ILogger<MaxioSubscriptionService> _logger;

    public MaxioSubscriptionService(
        CatalogContext db,
        MaxioGateway gateway,
        MaxioPlanCatalog catalog,
        MaxioCustomerService customers,
        TimeProvider time,
        ILogger<MaxioSubscriptionService> logger)
    {
        _db = db;
        _gateway = gateway;
        _catalog = catalog;
        _customers = customers;
        _time = time;
        _logger = logger;
    }

    /// <summary>The subscription reference sent to Maxio: deterministic per (user, plan), and free of personal data.</summary>
    public static string ReferenceFor(string userName, string productHandle)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(userName + "\u001f" + productHandle));
        return "eshop-" + Convert.ToHexString(bytes, 0, 16).ToLowerInvariant();
    }

    public async Task<PlanCatalog> ListPlansAsync(Guid correlationId, CancellationToken requestAborted)
    {
        using var scope = BeginScope(correlationId);
        using var budget = MaxioCallBudget.ForRequest(requestAborted);
        try
        {
            return await _catalog.GetPlansAsync(budget.Token);
        }
        catch (OperationCanceledException ex) when (!requestAborted.IsCancellationRequested)
        {
            throw BudgetExpired(ex);
        }
    }

    public async Task<IReadOnlyList<SubscriptionSnapshot>> ListMySubscriptionsAsync(string userName, Guid correlationId, CancellationToken requestAborted)
    {
        using var scope = BeginScope(correlationId);
        using var budget = MaxioCallBudget.ForRequest(requestAborted);
        try
        {
            var customerId = await _customers.FindCustomerIdAsync(userName, budget.Token);
            if (customerId is null)
            {
                return Array.Empty<SubscriptionSnapshot>();
            }
            return await _gateway.ListCustomerSubscriptionsAsync(customerId.Value, budget.Token);
        }
        catch (OperationCanceledException ex) when (!requestAborted.IsCancellationRequested)
        {
            throw BudgetExpired(ex);
        }
    }

    /// <summary>
    /// Subscribes the user to <paramref name="productHandle"/>: invariant check -> claim (store PK) -> ensure
    /// customer -> look up by reference -> create -> record.
    /// </summary>
    public async Task<SubscribeOutcome> SubscribeAsync(string userName, string email, string? productHandle, Guid correlationId, CancellationToken requestAborted)
    {
        using var scope = BeginScope(correlationId);
        using var budget = MaxioCallBudget.ForRequest(requestAborted);
        try
        {
            return await SubscribeCoreAsync(userName, email, productHandle, budget.Token);
        }
        catch (OperationCanceledException ex) when (!requestAborted.IsCancellationRequested)
        {
            throw BudgetExpired(ex);
        }
    }

    private async Task<SubscribeOutcome> SubscribeCoreAsync(string userName, string email, string? productHandle, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(productHandle))
        {
            throw new MaxioBillingException(HttpStatus.BadRequest, "productHandle is required.");
        }

        // Invariant: the handle must be one of the plans this shop offers (checked before any claim is written).
        var catalog = await _catalog.GetPlansAsync(ct);
        var plan = catalog.Plans.FirstOrDefault(p => string.Equals(p.Handle, productHandle, StringComparison.Ordinal));
        if (plan is null)
        {
            if (catalog.Truncated)
            {
                throw new MaxioBillingException(HttpStatus.ServiceUnavailable, "The plan list could not be read completely, so the plan could not be verified. Try again later.");
            }
            throw new MaxioBillingException(HttpStatus.BadRequest, "productHandle is not one of the offered subscription plans.");
        }

        var reference = ReferenceFor(userName, plan.Handle);
        var now = _time.GetUtcNow();
        var claim = new MaxioSubscriptionClaim(userName, plan.Handle, reference, now);

        // Claim. The store refuses a second claim for the same (user, plan) - a double-click stops here.
        var existing = await BillingClaimStore.InsertOrGetExistingAsync(_db, claim,
            token => _db.MaxioSubscriptionClaims.SingleOrDefaultAsync(c => c.UserName == userName && c.ProductHandle == plan.Handle, token), ct);

        if (existing is not null)
        {
            switch (existing.Status)
            {
                case BillingClaimStatus.Completed:
                {
                    var current = await _gateway.FindSubscriptionByReferenceAsync(existing.Reference, ct);
                    _logger.LogInformation("Subscribe repeated for an existing subscription {MaxioSubscriptionId} ({ProductHandle}).", existing.MaxioSubscriptionId, plan.Handle);
                    return new SubscribeOutcome(SubscribeOutcomeKind.Existing, existing.Reference, WithPlanFallback(current, plan), "You are already subscribed to this plan.");
                }

                case BillingClaimStatus.Pending when now - existing.ClaimedAtUtc < MaxioCustomerService.ClaimLease:
                    throw new DuplicateException("A subscription to this plan is already being created. Try again in a moment.");

                default:
                    existing.TakeOver(now);
                    if (!await BillingClaimStore.TrySaveAsync(_db, existing, _logger, ct))
                    {
                        throw new DuplicateException("A subscription to this plan is already being created. Try again in a moment.");
                    }
                    _logger.LogInformation("Took over an unsettled subscription claim for {ProductHandle}; settling it by reference.", plan.Handle);
                    claim = existing;
                    break;
            }
        }
        else
        {
            _logger.LogInformation("Claimed subscription creation for {ProductHandle}.", plan.Handle);
        }

        // Everything before the write: nothing exists at the provider for this claim yet, so on failure release it.
        int customerId;
        try
        {
            var customer = await _customers.EnsureCustomerAsync(userName, email, ct);
            if (customer.IsPending)
            {
                await BillingClaimStore.ReleaseAsync(_db, claim, _logger);
                return new SubscribeOutcome(SubscribeOutcomeKind.Pending, reference, null,
                    "Your billing account is still being confirmed. Check again shortly; repeating this request is safe.");
            }
            customerId = customer.MaxioCustomerId!.Value;

            // Look up by reference: an earlier run (or an interrupted create) may already have subscribed.
            var found = await _gateway.FindSubscriptionByReferenceAsync(reference, ct);
            if (found?.Id is not null)
            {
                claim.Complete(found.Id.Value);
                await BillingClaimStore.TrySaveAsync(_db, claim, _logger, CancellationToken.None);
                _logger.LogInformation("Adopted existing Maxio subscription {MaxioSubscriptionId} ({ProductHandle}).", found.Id, plan.Handle);
                return new SubscribeOutcome(SubscribeOutcomeKind.Existing, reference, WithPlanFallback(found, plan), "You are already subscribed to this plan.");
            }
        }
        catch
        {
            await BillingClaimStore.ReleaseAsync(_db, claim, _logger);
            throw;
        }

        // Create.
        SubscriptionSnapshot created;
        try
        {
            created = await _gateway.CreateSubscriptionAsync(plan.Handle, customerId, reference, ct);
        }
        catch (MaxioWriteRejectedException ex)
        {
            await BillingClaimStore.ReleaseAsync(_db, claim, _logger);
            throw MaxioCustomerService.ToCallerFailure(ex);
        }
        catch (MaxioWriteOutcomeUnknownException)
        {
            // The provider may have created the subscription. Settle it here by re-reading.
            var settled = await TrySettleAsync(reference, customerId, plan.Handle);
            if (settled?.Id is not null)
            {
                claim.Complete(settled.Id.Value);
                await BillingClaimStore.TrySaveAsync(_db, claim, _logger, CancellationToken.None);
                _logger.LogInformation("Settled an unknown CreateSubscription outcome: subscription {MaxioSubscriptionId} exists.", settled.Id);
                return new SubscribeOutcome(SubscribeOutcomeKind.Created, reference, WithPlanFallback(settled, plan), "Subscription created.");
            }

            claim.MarkUnknown();
            await BillingClaimStore.TrySaveAsync(_db, claim, _logger, CancellationToken.None);
            _logger.LogWarning("CreateSubscription outcome is still unknown for {ProductHandle}; the claim is marked Unknown and the next request settles it.", plan.Handle);
            return new SubscribeOutcome(SubscribeOutcomeKind.Pending, reference, null,
                "Your subscription is being confirmed. Check again shortly; repeating this request is safe.");
        }

        // Record.
        claim.Complete(created.Id!.Value);
        await BillingClaimStore.TrySaveAsync(_db, claim, _logger, CancellationToken.None);
        _logger.LogInformation("Created Maxio subscription {MaxioSubscriptionId} for customer {MaxioCustomerId} ({ProductHandle}).", created.Id, customerId, plan.Handle);
        return new SubscribeOutcome(SubscribeOutcomeKind.Created, reference, WithPlanFallback(created, plan), "Subscription created.");
    }

    /// <summary>
    /// Re-reads an interrupted create under a fresh budget: by reference first; if that lookup itself fails,
    /// by listing the customer's subscriptions. Null when nothing is found or both reads fail.
    /// </summary>
    private async Task<SubscriptionSnapshot?> TrySettleAsync(string reference, int customerId, string productHandle)
    {
        using (var cts = MaxioCallBudget.ForReconciliation())
        {
            try
            {
                return await _gateway.FindSubscriptionByReferenceAsync(reference, cts.Token);
            }
            catch (Exception ex) when (ex is MaxioBillingException or OperationCanceledException)
            {
                _logger.LogWarning("Re-reading the subscription by reference failed ({ExceptionType}); trying the customer's subscription list.", ex.GetType().Name);
            }
        }

        using (var cts = MaxioCallBudget.ForReconciliation())
        {
            try
            {
                var all = await _gateway.ListCustomerSubscriptionsAsync(customerId, cts.Token);
                return all.FirstOrDefault(s => string.Equals(s.Reference, reference, StringComparison.Ordinal))
                    ?? all.FirstOrDefault(s => string.Equals(s.ProductHandle, productHandle, StringComparison.Ordinal));
            }
            catch (Exception ex) when (ex is MaxioBillingException or OperationCanceledException)
            {
                _logger.LogWarning("Listing the customer's subscriptions failed too ({ExceptionType}).", ex.GetType().Name);
                return null;
            }
        }
    }

    /// <summary>
    /// Fills the plan fields from the offered catalog plan where the provider left them out. The SDK documents
    /// <c>Subscription.Product</c> as possibly null (new Catalog experience), so handle, name, interval and
    /// (when <c>product_price_in_cents</c> is absent too) price come from the plan this request subscribed to.
    /// A snapshot that names a different product is left untouched.
    /// </summary>
    internal static SubscriptionSnapshot? WithPlanFallback(SubscriptionSnapshot? s, SubscriptionPlan plan)
    {
        if (s is null || (s.ProductHandle is not null && !string.Equals(s.ProductHandle, plan.Handle, StringComparison.Ordinal)))
        {
            return s;
        }
        return s with
        {
            ProductHandle = s.ProductHandle ?? plan.Handle,
            ProductName = s.ProductName ?? plan.Name,
            PriceInCents = s.PriceInCents ?? plan.PriceInCents,
            Interval = s.Interval ?? plan.Interval,
            IntervalUnit = s.IntervalUnit ?? plan.IntervalUnit,
        };
    }

    private MaxioBillingException BudgetExpired(Exception ex)
    {
        _logger.LogWarning("The Maxio call budget of {Budget} was exhausted.", MaxioCallBudget.PerRequest);
        return new MaxioBillingException(HttpStatus.GatewayTimeout, "The billing provider did not answer in time.", null, ex);
    }

    private IDisposable? BeginScope(Guid correlationId) =>
        _logger.BeginScope(new Dictionary<string, object> { ["CorrelationId"] = correlationId });
}
