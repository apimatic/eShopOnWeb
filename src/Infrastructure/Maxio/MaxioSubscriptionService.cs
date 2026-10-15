using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.Exceptions;
using MaxioAdvancedBilling.Errors;
using MaxioAdvancedBilling.Models;
using MaxioAdvancedBilling.Models.Enums;
using MaxioAdvancedBilling.Requests.Customers;
using MaxioAdvancedBilling.Requests.ProductFamilies;
using MaxioAdvancedBilling.Requests.Subscriptions;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.eShopWeb.Infrastructure.Identity;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Subscribe flow against Maxio Advanced Billing. Every write follows: claim (store-enforced primary key)
/// -> reconcile by reference -> SDK call -> record the result. Unknown outcomes are settled by re-reading
/// Maxio by the reference that was sent.
/// </summary>
public class MaxioSubscriptionService : ISubscriptionService
{
    private const int PER_PAGE = 200;
    private const int MAX_PAGES = 10;
    /// <summary>Ceiling for one HTTP request of ours, however many SDK calls and retries it makes.</summary>
    private static readonly TimeSpan CallBudget = TimeSpan.FromSeconds(20);
    /// <summary>A Pending claim older than this is treated as abandoned (its owner crashed).</summary>
    private static readonly TimeSpan StaleClaimAge = TimeSpan.FromMinutes(2);

    private readonly MaxioAdvancedBillingClient _client;
    private readonly MaxioSettings _settings;
    private readonly CatalogContext _db;
    private readonly UserManager<ApplicationUser> _users;
    private readonly ILogger<MaxioSubscriptionService> _logger;

    public MaxioSubscriptionService(MaxioAdvancedBillingClient client, MaxioSettings settings, CatalogContext db,
        UserManager<ApplicationUser> users, ILogger<MaxioSubscriptionService> logger)
    {
        _client = client;
        _settings = settings;
        _db = db;
        _users = users;
        _logger = logger;
    }

    // ---------------------------------------------------------------- plans

    public async Task<SubscriptionPlansResult> ListPlansAsync(CancellationToken cancellationToken)
    {
        using var budget = Budget(cancellationToken);
        return await ListPlansCoreAsync(budget.Token);
    }

    private async Task<SubscriptionPlansResult> ListPlansCoreAsync(CancellationToken ct)
    {
        var plans = new List<SubscriptionPlan>();
        var truncated = false;
        try
        {
            for (var page = 1; ; page++)
            {
                var products = await _client.ProductFamilies.ListProductsForProductFamily(
                    new ListProductsForProductFamilyRequest
                    {
                        ProductFamilyId = "handle:" + _settings.ProductFamilyHandle,
                        Page = page,
                        PerPage = PER_PAGE
                    }, cancellationToken: ct);

                foreach (var item in products)
                {
                    var p = item.Product;
                    if (p.ArchivedAt != null || string.IsNullOrEmpty(p.Handle)) continue;
                    plans.Add(new SubscriptionPlan(p.Handle!, p.Name ?? p.Handle!, p.Description, p.PriceInCents,
                        p.Interval, p.IntervalUnit?.Value, p.TrialPriceInCents, p.InitialChargeInCents));
                }

                if (products.Count < PER_PAGE) break;
                if (page >= MAX_PAGES)
                {
                    truncated = true;
                    break;
                }
            }
        }
        catch (Exception ex) when (ex is SdkException)
        {
            LogFailure("ListProductsForProductFamily", ex);
            throw new SubscriptionException(SubscriptionFailure.Unavailable, "The plan catalog is temporarily unavailable.", ex);
        }

        _logger.LogInformation("Maxio plan list returned {Count} plans (truncated: {Truncated})", plans.Count, truncated);
        return new SubscriptionPlansResult(plans, truncated);
    }

    // ------------------------------------------------------------ subscribe

    public async Task<SubscribeResult> SubscribeAsync(string userName, string planHandle, CancellationToken cancellationToken)
    {
        using var budget = Budget(cancellationToken);
        var ct = budget.Token;

        var user = await _users.FindByNameAsync(userName)
            ?? throw new SubscriptionException(SubscriptionFailure.UserNotFound, "The signed-in user no longer exists.");
        var userId = user.Id;

        // Cross-operation invariant: the plan must be one the configured product family currently offers.
        var catalog = await ListPlansCoreAsync(ct);
        if (catalog.IsTruncated)
        {
            throw new SubscriptionException(SubscriptionFailure.Unavailable, "The plan catalog could not be read in full; try again later.");
        }
        if (!catalog.Plans.Any(p => string.Equals(p.Handle, planHandle, StringComparison.Ordinal)))
        {
            throw new SubscriptionException(SubscriptionFailure.InvalidPlan, $"'{planHandle}' is not an available subscription plan.");
        }

        var customerId = await EnsureCustomerAsync(user, ct);
        if (customerId == null)
        {
            return new SubscribeResult(SubscribeOutcome.Pending, null);
        }

        return await EnsureSubscriptionAsync(userId, planHandle, customerId.Value, ct);
    }

    private async Task<int?> EnsureCustomerAsync(ApplicationUser user, CancellationToken ct)
    {
        var userId = user.Id;
        for (var attempt = 0; attempt < 2; attempt++)
        {
            var link = new MaxioCustomerLink { UserId = userId, State = SubscriptionLinkState.Pending, CreatedAt = DateTimeOffset.UtcNow };
            if (await TryClaimAsync(link, ct))
            {
                return await CreateCustomerAsync(link, user, ct);
            }

            // The store refused the claim: somebody (this process earlier, or a concurrent request) holds it.
            var existing = await _db.MaxioCustomerLinks.FirstOrDefaultAsync(l => l.UserId == userId, ct);
            if (existing == null) continue; // released in between; claim again
            if (existing.State == SubscriptionLinkState.Active && existing.MaxioCustomerId != null)
            {
                return existing.MaxioCustomerId;
            }

            var found = await LookupCustomerAsync(userId, ct);
            if (found != null)
            {
                existing.State = SubscriptionLinkState.Active;
                existing.MaxioCustomerId = found;
                await _db.SaveChangesAsync(CancellationToken.None);
                return found;
            }

            if (IsLive(existing.State, existing.CreatedAt))
            {
                throw new SubscriptionException(SubscriptionFailure.InProgress, "Your account is being set up for billing; try again in a moment.");
            }
            if (!await TryReleaseAsync(existing)) throw new SubscriptionException(SubscriptionFailure.InProgress, "Your account is being set up for billing; try again in a moment.");
        }
        throw new SubscriptionException(SubscriptionFailure.InProgress, "Your account is being set up for billing; try again in a moment.");
    }

    private async Task<int?> CreateCustomerAsync(MaxioCustomerLink link, ApplicationUser user, CancellationToken ct)
    {
        try
        {
            // The claim may be new while the customer already exists at Maxio (e.g. the local store was reset).
            var existingId = await LookupCustomerAsync(link.UserId, ct);
            if (existingId == null)
            {
                var (first, last) = SplitName(user);
                var response = await _client.Customers.CreateCustomer(new CreateCustomerOperationRequest
                {
                    Body = new CreateCustomerRequest
                    {
                        Customer = new CreateCustomer
                        {
                            FirstName = first,
                            LastName = last,
                            Email = user.Email ?? user.UserName ?? link.UserId,
                            Reference = link.UserId
                        }
                    }
                }, cancellationToken: ct);
                existingId = response.Customer.Id;
                if (existingId == null) throw new InvalidOperationException("Maxio returned a customer without an id.");
            }

            link.State = SubscriptionLinkState.Active;
            link.MaxioCustomerId = existingId;
            await _db.SaveChangesAsync(CancellationToken.None);
            _logger.LogInformation("Maxio customer {CustomerId} linked to user", existingId);
            return existingId;
        }
        catch (Exception ex) when (ex is SdkException || ex is InvalidOperationException)
        {
            LogFailure("CreateCustomer", ex);
            if (IsUnknownOutcome(ex) || ex is InvalidOperationException)
            {
                // The write may have landed: settle by re-reading with the reference we sent.
                try
                {
                    var settled = await LookupCustomerAsync(link.UserId, CancellationToken.None);
                    if (settled != null)
                    {
                        link.State = SubscriptionLinkState.Active;
                        link.MaxioCustomerId = settled;
                        await _db.SaveChangesAsync(CancellationToken.None);
                        return settled;
                    }
                    await TryReleaseAsync(link); // Maxio definitely does not have it
                    throw new SubscriptionException(SubscriptionFailure.Unavailable, "The billing system is temporarily unavailable.", ex);
                }
                catch (SdkException)
                {
                    link.State = SubscriptionLinkState.Unknown;
                    await _db.SaveChangesAsync(CancellationToken.None);
                    _logger.LogWarning("Maxio customer outcome unknown; claim kept for reconciliation");
                    return null;
                }
            }

            await TryReleaseAsync(link);
            throw ToFailure(ex, "The billing system rejected the customer details.");
        }
    }

    private async Task<SubscribeResult> EnsureSubscriptionAsync(string userId, string planHandle, int customerId, CancellationToken ct)
    {
        var reference = SubscriptionReference(userId, planHandle);
        for (var attempt = 0; attempt < 2; attempt++)
        {
            var link = new MaxioSubscriptionLink
            {
                UserId = userId,
                PlanHandle = planHandle,
                Reference = reference,
                State = SubscriptionLinkState.Pending,
                CreatedAt = DateTimeOffset.UtcNow
            };
            if (await TryClaimAsync(link, ct))
            {
                return await CreateSubscriptionAsync(link, customerId, ct);
            }

            var existing = await _db.MaxioSubscriptionLinks.FirstOrDefaultAsync(l => l.UserId == userId && l.PlanHandle == planHandle, ct);
            if (existing == null) continue;

            if (existing.State == SubscriptionLinkState.Active && existing.MaxioSubscriptionId != null)
            {
                return new SubscribeResult(SubscribeOutcome.AlreadySubscribed, await ReadInfoAsync(existing, ct));
            }

            var found = await FindSubscriptionAsync(reference, ct);
            if (found != null)
            {
                existing.State = SubscriptionLinkState.Active;
                existing.MaxioSubscriptionId = found.Id;
                await _db.SaveChangesAsync(CancellationToken.None);
                return new SubscribeResult(SubscribeOutcome.AlreadySubscribed, ToInfo(planHandle, found));
            }

            if (IsLive(existing.State, existing.CreatedAt))
            {
                throw new SubscriptionException(SubscriptionFailure.InProgress, "This subscription is already being created; try again in a moment.");
            }
            if (!await TryReleaseAsync(existing)) throw new SubscriptionException(SubscriptionFailure.InProgress, "This subscription is already being created; try again in a moment.");
        }
        throw new SubscriptionException(SubscriptionFailure.InProgress, "This subscription is already being created; try again in a moment.");
    }

    private async Task<SubscribeResult> CreateSubscriptionAsync(MaxioSubscriptionLink link, int customerId, CancellationToken ct)
    {
        try
        {
            // The claim may be new while Maxio already has the subscription (local store reset): adopt it.
            var subscription = await FindSubscriptionAsync(link.Reference, ct);
            var outcome = SubscribeOutcome.AlreadySubscribed;
            if (subscription == null)
            {
                var response = await _client.Subscriptions.CreateSubscription(new CreateSubscriptionOperationRequest
                {
                    Body = new CreateSubscriptionRequest
                    {
                        Subscription = new MaxioAdvancedBilling.Models.CreateSubscription
                        {
                            ProductHandle = link.PlanHandle,
                            CustomerId = customerId,
                            Reference = link.Reference,
                            // No payment profile is captured by this flow; invoice-style collection needs none.
                            PaymentCollectionMethod = CollectionMethod.Remittance
                        }
                    }
                }, cancellationToken: ct);
                subscription = response.Subscription ?? throw new InvalidOperationException("Maxio returned no subscription.");
                outcome = SubscribeOutcome.Created;
            }

            if (subscription.State != null && subscription.State.Is("failed_to_create"))
            {
                await TryReleaseAsync(link);
                throw new SubscriptionException(SubscriptionFailure.Rejected, "The billing system could not create the subscription.");
            }

            link.State = SubscriptionLinkState.Active;
            link.MaxioSubscriptionId = subscription.Id;
            await _db.SaveChangesAsync(CancellationToken.None);
            _logger.LogInformation("Maxio subscription {SubscriptionId} created for plan {Plan}", subscription.Id, link.PlanHandle);
            return new SubscribeResult(outcome, ToInfo(link.PlanHandle, subscription));
        }
        catch (SubscriptionException)
        {
            throw;
        }
        catch (Exception ex) when (ex is SdkException || ex is InvalidOperationException)
        {
            LogFailure("CreateSubscription", ex);
            if (IsUnknownOutcome(ex) || ex is InvalidOperationException)
            {
                try
                {
                    var settled = await FindSubscriptionAsync(link.Reference, CancellationToken.None);
                    if (settled != null)
                    {
                        link.State = SubscriptionLinkState.Active;
                        link.MaxioSubscriptionId = settled.Id;
                        await _db.SaveChangesAsync(CancellationToken.None);
                        return new SubscribeResult(SubscribeOutcome.Created, ToInfo(link.PlanHandle, settled));
                    }
                    await TryReleaseAsync(link);
                    throw new SubscriptionException(SubscriptionFailure.Unavailable, "The billing system is temporarily unavailable.", ex);
                }
                catch (SdkException)
                {
                    link.State = SubscriptionLinkState.Unknown;
                    await _db.SaveChangesAsync(CancellationToken.None);
                    _logger.LogWarning("Maxio subscription outcome unknown for plan {Plan}; claim kept for reconciliation", link.PlanHandle);
                    return new SubscribeResult(SubscribeOutcome.Pending, null);
                }
            }

            await TryReleaseAsync(link);
            throw ToFailure(ex, "The billing system rejected the subscription request.");
        }
    }

    // ------------------------------------------------------ my subscriptions

    public async Task<IReadOnlyList<SubscriptionInfo>> ListSubscriptionsAsync(string userName, CancellationToken cancellationToken)
    {
        using var budget = Budget(cancellationToken);
        var ct = budget.Token;

        var user = await _users.FindByNameAsync(userName)
            ?? throw new SubscriptionException(SubscriptionFailure.UserNotFound, "The signed-in user no longer exists.");

        var links = await _db.MaxioSubscriptionLinks.Where(l => l.UserId == user.Id).OrderBy(l => l.CreatedAt).ToListAsync(ct);
        var result = new List<SubscriptionInfo>();
        foreach (var link in links)
        {
            try
            {
                if (link.MaxioSubscriptionId == null && (link.State == SubscriptionLinkState.Unknown || !IsLive(link.State, link.CreatedAt)))
                {
                    var settled = await FindSubscriptionAsync(link.Reference, ct);
                    if (settled != null)
                    {
                        link.State = SubscriptionLinkState.Active;
                        link.MaxioSubscriptionId = settled.Id;
                        await _db.SaveChangesAsync(CancellationToken.None);
                    }
                }
                result.Add(await ReadInfoAsync(link, ct));
            }
            catch (SdkException ex)
            {
                LogFailure("ReadSubscription", ex);
                result.Add(new SubscriptionInfo(link.MaxioSubscriptionId, link.PlanHandle, null, null, null, null, false));
            }
        }
        return result;
    }

    private async Task<SubscriptionInfo> ReadInfoAsync(MaxioSubscriptionLink link, CancellationToken ct)
    {
        if (link.MaxioSubscriptionId == null)
        {
            return new SubscriptionInfo(null, link.PlanHandle, null, null, null, null, false);
        }
        try
        {
            var response = await _client.Subscriptions.ReadSubscription(
                new ReadSubscriptionRequest { SubscriptionId = link.MaxioSubscriptionId.Value }, cancellationToken: ct);
            return response.Subscription != null
                ? ToInfo(link.PlanHandle, response.Subscription)
                : new SubscriptionInfo(link.MaxioSubscriptionId, link.PlanHandle, null, null, null, null, false);
        }
        catch (SdkException ex)
        {
            LogFailure("ReadSubscription", ex);
            return new SubscriptionInfo(link.MaxioSubscriptionId, link.PlanHandle, null, null, null, null, false);
        }
    }

    // --------------------------------------------------------------- helpers

    /// <summary>Returns the Maxio customer id for the reference, or null when Maxio has none (404).</summary>
    private async Task<int?> LookupCustomerAsync(string reference, CancellationToken ct)
    {
        try
        {
            var response = await _client.Customers.ReadCustomerByReference(
                new ReadCustomerByReferenceRequest { Reference = reference }, cancellationToken: ct);
            return response.Customer.Id;
        }
        catch (ApiException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    private async Task<MaxioAdvancedBilling.Models.Subscription?> FindSubscriptionAsync(string reference, CancellationToken ct)
    {
        try
        {
            var response = await _client.Subscriptions.FindSubscription(
                new FindSubscriptionRequest { Reference = reference }, cancellationToken: ct);
            return response.Subscription;
        }
        catch (ApiException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    /// <summary>Insert-or-fail on the primary key; false when the store refuses (an earlier claim exists).</summary>
    private async Task<bool> TryClaimAsync(object link, CancellationToken ct)
    {
        try
        {
            _db.Add(link);
            await _db.SaveChangesAsync(ct);
            return true;
        }
        catch (Exception ex) when (ex is DbUpdateException || ex is InvalidOperationException || ex is ArgumentException)
        {
            _logger.LogWarning("Billing claim refused by the store ({Type}); an earlier claim exists", ex.GetType().Name);
            _db.ChangeTracker.Clear();
            return false;
        }
    }

    private async Task<bool> TryReleaseAsync(object link)
    {
        try
        {
            var entry = _db.Entry(link);
            if (entry.State == EntityState.Detached) _db.Attach(link);
            _db.Remove(link);
            await _db.SaveChangesAsync(CancellationToken.None);
            return true;
        }
        catch (Exception ex) when (ex is DbUpdateException || ex is InvalidOperationException)
        {
            _logger.LogWarning("Could not release billing claim ({Type})", ex.GetType().Name);
            _db.ChangeTracker.Clear();
            return false;
        }
    }

    private static bool IsLive(SubscriptionLinkState state, DateTimeOffset createdAt) =>
        state == SubscriptionLinkState.Pending && DateTimeOffset.UtcNow - createdAt < StaleClaimAge;

    /// <summary>The write may or may not have landed: no answer, or a server-side failure.</summary>
    private static bool IsUnknownOutcome(Exception ex) => ex switch
    {
        SdkConnectionException => true, // includes SdkTimeoutException
        ResponseDeserializationException r => (int)r.StatusCode is >= 200 and < 300 || (int)r.StatusCode >= 500,
        ApiException a => (int)a.StatusCode >= 500,
        _ => false
    };

    private static SubscriptionException ToFailure(Exception ex, string rejectedMessage)
    {
        // Our own credentials/quota (401, 403, 429) are not the caller's fault.
        if (ex is ApiException a && a.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.NotFound or HttpStatusCode.Conflict or HttpStatusCode.UnprocessableEntity)
        {
            var detail = ex is ApiException<CreateSubscriptionError> sub && sub.Error.TryGetErrorListResponse1(out var list)
                ? string.Join("; ", list.Errors)
                : null;
            return new SubscriptionException(SubscriptionFailure.Rejected, detail ?? rejectedMessage, ex);
        }
        return new SubscriptionException(SubscriptionFailure.Unavailable, "The billing system is temporarily unavailable.", ex);
    }

    private void LogFailure(string operation, Exception ex)
    {
        if (ex is ApiException api)
        {
            string? detail = null;
            if (ex is ApiException<CreateSubscriptionError> sub && sub.Error.TryGetErrorListResponse1(out var list))
            {
                detail = string.Join("; ", list.Errors);
            }
            _logger.LogWarning("Maxio {Operation} failed: HTTP {Status} {Detail} ({Type})", operation, (int)api.StatusCode, detail, ex.GetType().Name);
        }
        else
        {
            _logger.LogWarning("Maxio {Operation} failed: {Type}: {Message}", operation, ex.GetType().Name, ex.Message);
        }
    }

    private static CancellationTokenSource Budget(CancellationToken ct)
    {
        var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(CallBudget);
        return cts;
    }

    private static string SubscriptionReference(string userId, string planHandle) => $"eshop:{userId}:{planHandle}";

    private static SubscriptionInfo ToInfo(string planHandle, MaxioAdvancedBilling.Models.Subscription s) =>
        new(s.Id, planHandle, s.Product?.Name, s.ProductPriceInCents ?? s.CurrentBillingAmountInCents, s.State?.Value,
            s.NextAssessmentAt, true);

    private static (string First, string Last) SplitName(ApplicationUser user)
    {
        var local = (user.Email ?? user.UserName ?? "shopper").Split('@')[0];
        var parts = local.Split(new[] { '.', '_', '-', '+' }, StringSplitOptions.RemoveEmptyEntries);
        string Cap(string s) => char.ToUpperInvariant(s[0]) + s.Substring(1);
        if (parts.Length == 0) return ("Shopper", "Shopper");
        if (parts.Length == 1) return (Cap(parts[0]), "Shopper");
        return (Cap(parts[0]), string.Join(" ", parts.Skip(1).Select(Cap)));
    }
}
