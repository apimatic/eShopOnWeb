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
using MaxioAdvancedBilling.Requests.ProductFamilies;
using MaxioAdvancedBilling.Requests.Subscriptions;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Subscription billing backed by Maxio Advanced Billing. Every write follows
/// claim (local row, primary key) -> Maxio call -> record the result.
/// </summary>
public class MaxioSubscriptionService : ISubscriptionService
{
    /// <summary>Total budget for one service call, including every Maxio call.</summary>
    public static readonly TimeSpan CallBudget = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan ReconcileBudget = TimeSpan.FromSeconds(10);
    // A claim older than this with no recorded outcome is presumed abandoned.
    private static readonly TimeSpan StaleClaimAfter = TimeSpan.FromMinutes(2);
    private const int PlansPerPage = 200;
    private const int MaxPlanPages = 10;

    private readonly MaxioAdvancedBillingClient _client;
    private readonly CatalogContext _db;
    private readonly MaxioSettings _settings;
    private readonly ILogger<MaxioSubscriptionService> _logger;

    public MaxioSubscriptionService(MaxioAdvancedBillingClient client, CatalogContext db,
        IOptions<MaxioSettings> settings, ILogger<MaxioSubscriptionService> logger)
    {
        _client = client;
        _db = db;
        _settings = settings.Value;
        _logger = logger;
    }

    // ---------------------------------------------------------------- plans

    public Task<SubscriptionPlanList> ListPlansAsync(CancellationToken cancellationToken) =>
        Bounded(budget => ListPlansCoreAsync(budget), cancellationToken);

    private async Task<SubscriptionPlanList> ListPlansCoreAsync(CancellationToken ct)
    {
        var plans = new List<SubscriptionPlan>();
        var truncated = false;
        try
        {
            for (var page = 1; ; page++)
            {
                var items = await _client.ProductFamilies.ListProductsForProductFamily(
                    new ListProductsForProductFamilyRequest
                    {
                        ProductFamilyId = "handle:" + _settings.ProductFamilyHandle,
                        Page = page,
                        PerPage = PlansPerPage
                    },
                    cancellationToken: ct);

                foreach (var item in items)
                {
                    var p = item.Product;
                    if (string.IsNullOrWhiteSpace(p.Handle) || p.ArchivedAt != null) continue;
                    plans.Add(new SubscriptionPlan(p.Handle!, p.Name ?? p.Handle!, p.Description,
                        p.PriceInCents ?? 0, p.Interval, p.IntervalUnit?.Value));
                }

                if (items.Count < PlansPerPage) break;
                if (page >= MaxPlanPages) { truncated = true; break; }
            }
        }
        catch (ApiException<ListProductsForProductFamilyError> ex)
        {
            var body = ex.Error.TryGetString(out var s) ? s : ex.Error.TryGetRawError(out RawError raw) ? raw.ReadAsString() : "";
            _logger.LogError("Maxio plan listing failed: HTTP {Status} {Body}", (int)ex.StatusCode, body);
            throw new BillingUnavailableException("The billing system could not list plans.", ex);
        }
        catch (Exception ex) when (IsProviderFailure(ex, ct))
        {
            _logger.LogError(ex, "Maxio plan listing failed");
            throw new BillingUnavailableException("The billing system could not list plans.", ex);
        }

        if (truncated) _logger.LogWarning("Maxio plan listing truncated at {Pages} pages", MaxPlanPages);
        _logger.LogInformation("Listed {Count} subscription plans", plans.Count);
        return new SubscriptionPlanList(plans, truncated);
    }

    // ------------------------------------------------------------ subscribe

    public Task<SubscribeResult> SubscribeAsync(string userId, string email, string displayName, string planHandle,
        CancellationToken cancellationToken) =>
        Bounded(budget => SubscribeCoreAsync(userId, email, displayName, planHandle, budget, cancellationToken), cancellationToken);

    private async Task<SubscribeResult> SubscribeCoreAsync(string userId, string email, string displayName, string planHandle,
        CancellationToken budget, CancellationToken caller)
    {
        // Invariant: only a plan the configured family offers may be subscribed to.
        var offered = await ListPlansCoreAsync(budget);
        if (!offered.Plans.Any(p => string.Equals(p.Handle, planHandle, StringComparison.Ordinal)))
        {
            if (offered.IsTruncated) throw new BillingUnavailableException("The plan list is incomplete; try again later.");
            throw new SubscriptionPlanNotFoundException(planHandle);
        }

        var existing = await _db.UserSubscriptionLinks.FindAsync(new object[] { userId }, budget);
        if (existing != null) return await ExistingAsync(existing, planHandle, budget);

        var customerId = await EnsureCustomerAsync(userId, email, displayName, budget, caller);

        // CLAIM: the second caller's insert is refused by the primary key before Maxio is called.
        var link = new UserSubscriptionLink
        {
            UserId = userId,
            PlanHandle = planHandle,
            MaxioCustomerId = customerId,
            Status = SubscriptionLinkStatus.Claimed,
            CreatedUtc = DateTime.UtcNow
        };
        _db.UserSubscriptionLinks.Add(link);
        try
        {
            await _db.SaveChangesAsync(budget);
        }
        catch (Exception ex) when (IsClaimConflict(ex))
        {
            _db.Entry(link).State = EntityState.Detached;
            var other = await _db.UserSubscriptionLinks.FindAsync(new object[] { userId }, budget);
            if (other == null) throw;
            return await ExistingAsync(other, planHandle, budget);
        }

        try
        {
            var response = await _client.Subscriptions.CreateSubscription(
                new CreateSubscriptionOperationRequest
                {
                    Body = new CreateSubscriptionRequest
                    {
                        Subscription = new CreateSubscription
                        {
                            ProductHandle = planHandle,
                            CustomerId = customerId,
                            // Invoice-based collection: no card or bank account is captured at signup.
                            PaymentCollectionMethod = CollectionMethod.Remittance,
                            Reference = userId
                        }
                    }
                },
                cancellationToken: budget);

            var created = response.Subscription;
            if (created?.Id == null) throw new UnreadableResponseException();

            await RecordActiveAsync(link, created.Id.Value);
            _logger.LogInformation("Subscription {SubscriptionId} created for user {UserId} on plan {Plan}",
                created.Id, userId, planHandle);
            return new SubscribeResult(ToSummary(created, planHandle), false);
        }
        catch (ApiException<CreateSubscriptionError> ex) when ((int)ex.StatusCode < 500)
        {
            var detail = ex.Error.TryGetErrorListResponse1(out var list) ? string.Join("; ", list.Errors)
                : ex.Error.TryGetRawError(out RawError raw) ? raw.ReadAsString() : "";
            _logger.LogWarning("Maxio refused subscription: HTTP {Status} {Detail}", (int)ex.StatusCode, detail);
            await ReleaseAsync(link);
            throw ex.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden or HttpStatusCode.TooManyRequests
                ? new BillingUnavailableException("The billing system is unavailable.", ex)
                : new BillingRejectedException("The billing system rejected the subscription request.");
        }
        catch (AuthSchemeException ex)
        {
            await ReleaseAsync(link);
            throw new BillingUnavailableException("The billing system is unavailable.", ex);
        }
        catch (Exception ex) when (IsAmbiguous(ex, caller))
        {
            // The write may have landed: settle it from Maxio's state instead of reporting failure.
            _logger.LogWarning(ex, "CreateSubscription outcome unknown for user {UserId}; reconciling", userId);
            return await ReconcileSubscriptionAsync(link, planHandle, caller);
        }
    }

    private async Task<SubscribeResult> ExistingAsync(UserSubscriptionLink link, string requestedPlan, CancellationToken ct)
    {
        if (link.Status != SubscriptionLinkStatus.Active) await SettleAsync(link, ct);

        if (link.Status == SubscriptionLinkStatus.Active)
        {
            if (!string.Equals(link.PlanHandle, requestedPlan, StringComparison.Ordinal))
                throw new DuplicateException($"You already have a subscription to plan '{link.PlanHandle}'.");
            return new SubscribeResult(await ReadSummaryAsync(link, ct), true);
        }

        var state = _db.Entry(link).State;
        if (state == EntityState.Detached || state == EntityState.Deleted)
            throw new BillingUnavailableException("The previous subscription attempt did not complete; please retry.");
        throw new DuplicateException("A subscription request for this user is already in progress.");
    }

    private async Task<SubscribeResult> ReconcileSubscriptionAsync(UserSubscriptionLink link, string planHandle, CancellationToken caller)
    {
        if (caller.IsCancellationRequested)
            throw new BillingUnavailableException("The request was cancelled; the outcome will be confirmed on the next request.");
        using var cts = new CancellationTokenSource(ReconcileBudget);
        try
        {
            var match = await FindSubscriptionAsync(link, cts.Token);
            if (match?.Id != null)
            {
                await RecordActiveAsync(link, match.Id.Value);
                return new SubscribeResult(ToSummary(match, planHandle), false);
            }
        }
        catch (Exception ex) when (IsProviderFailure(ex, caller))
        {
            // The re-read failed too: keep the claim as Unknown so a later request settles it.
            link.Status = SubscriptionLinkStatus.Unknown;
            await _db.SaveChangesAsync(CancellationToken.None);
            throw new BillingUnavailableException("The subscription outcome could not be confirmed yet; retrying will settle it.", ex);
        }

        await ReleaseAsync(link);
        throw new BillingUnavailableException("The subscription was not created; please retry.");
    }

    /// <summary>Settles a Claimed/Unknown row from Maxio's state; abandoned claims are released.</summary>
    private async Task SettleAsync(UserSubscriptionLink link, CancellationToken ct)
    {
        try
        {
            var match = await FindSubscriptionAsync(link, ct);
            if (match?.Id != null)
            {
                await RecordActiveAsync(link, match.Id.Value);
                return;
            }
        }
        catch (Exception ex) when (IsProviderFailure(ex, ct))
        {
            _logger.LogWarning(ex, "Could not settle subscription claim for user {UserId}", link.UserId);
            return;
        }

        if (DateTime.UtcNow - link.CreatedUtc > StaleClaimAfter) await ReleaseAsync(link);
    }

    private async Task<Subscription?> FindSubscriptionAsync(UserSubscriptionLink link, CancellationToken ct)
    {
        var subs = await _client.Customers.ListCustomerSubscriptions(
            new ListCustomerSubscriptionsRequest { CustomerId = link.MaxioCustomerId }, cancellationToken: ct);
        return subs.Select(s => s.Subscription).FirstOrDefault(s => s != null
            && string.Equals(s.Reference, link.UserId, StringComparison.Ordinal)
            && string.Equals(s.Product?.Handle, link.PlanHandle, StringComparison.Ordinal));
    }

    // ------------------------------------------------------------- customer

    private async Task<int> EnsureCustomerAsync(string userId, string email, string displayName, CancellationToken budget, CancellationToken caller)
    {
        var link = await _db.UserCustomerLinks.FindAsync(new object[] { userId }, budget);
        if (link?.MaxioCustomerId is int known) return known;

        if (link == null)
        {
            // CLAIM: primary key on UserId refuses a second concurrent customer creation.
            var claim = new UserCustomerLink { UserId = userId, CreatedUtc = DateTime.UtcNow };
            _db.UserCustomerLinks.Add(claim);
            var claimed = false;
            try
            {
                await _db.SaveChangesAsync(budget);
                claimed = true;
            }
            catch (Exception ex) when (IsClaimConflict(ex))
            {
                _db.Entry(claim).State = EntityState.Detached;
                link = await _db.UserCustomerLinks.FindAsync(new object[] { userId }, budget);
                if (link == null) throw;
                if (link.MaxioCustomerId is int raced) return raced;
            }
            if (claimed) return await CreateCustomerAsync(claim, email, displayName, budget, caller);
        }

        // Someone else's claim without a recorded id: settle it by reference.
        var found = await ReadCustomerIdByReferenceAsync(userId, budget);
        if (found != null)
        {
            link!.MaxioCustomerId = found;
            await _db.SaveChangesAsync(budget);
            return found.Value;
        }
        if (DateTime.UtcNow - link!.CreatedUtc > StaleClaimAfter)
        {
            _db.UserCustomerLinks.Remove(link);
            await _db.SaveChangesAsync(budget);
            throw new BillingUnavailableException("The previous customer setup did not complete; please retry.");
        }
        throw new DuplicateException("A subscription request for this user is already in progress.");
    }

    private async Task<int> CreateCustomerAsync(UserCustomerLink link, string email, string displayName, CancellationToken budget, CancellationToken caller)
    {
        var (first, last) = SplitName(displayName);
        try
        {
            var response = await _client.Customers.CreateCustomer(
                new CreateCustomerOperationRequest
                {
                    Body = new CreateCustomerRequest
                    {
                        Customer = new CreateCustomer { FirstName = first, LastName = last, Email = email, Reference = link.UserId }
                    }
                },
                cancellationToken: budget);
            if (response.Customer.Id == null) throw new UnreadableResponseException();
            return await RecordCustomerAsync(link, response.Customer.Id.Value);
        }
        catch (ApiException<CreateCustomerError> ex) when ((int)ex.StatusCode < 500)
        {
            _logger.LogWarning("Maxio refused customer creation: HTTP {Status}", (int)ex.StatusCode);
            // 422 commonly means the reference already exists (e.g. the local store was lost): adopt it.
            if (ex.StatusCode == (HttpStatusCode)422 && await TryAdoptAsync(link) is int adopted) return adopted;
            await ReleaseCustomerClaimAsync(link);
            throw ex.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden or HttpStatusCode.TooManyRequests
                ? new BillingUnavailableException("The billing system is unavailable.", ex)
                : new BillingRejectedException("The billing system rejected the customer details.");
        }
        catch (AuthSchemeException ex)
        {
            await ReleaseCustomerClaimAsync(link);
            throw new BillingUnavailableException("The billing system is unavailable.", ex);
        }
        catch (Exception ex) when (IsAmbiguous(ex, caller))
        {
            _logger.LogWarning(ex, "CreateCustomer outcome unknown for user {UserId}; reconciling", link.UserId);
            if (caller.IsCancellationRequested) throw new BillingUnavailableException("The request was cancelled.", ex);
            // If the re-read fails too it throws BillingUnavailable and the claim stays for the next request to settle.
            if (await TryAdoptAsync(link) is int adopted) return adopted;
            await ReleaseCustomerClaimAsync(link);
            throw new BillingUnavailableException("The customer was not created; please retry.", ex);
        }
    }

    private async Task<int?> TryAdoptAsync(UserCustomerLink link)
    {
        using var cts = new CancellationTokenSource(ReconcileBudget);
        var found = await ReadCustomerIdByReferenceAsync(link.UserId, cts.Token);
        if (found == null) return null;
        return await RecordCustomerAsync(link, found.Value);
    }

    private async Task<int?> ReadCustomerIdByReferenceAsync(string userId, CancellationToken ct)
    {
        try
        {
            var r = await _client.Customers.ReadCustomerByReference(new ReadCustomerByReferenceRequest { Reference = userId }, cancellationToken: ct);
            return r.Customer.Id;
        }
        catch (ApiException<RawError> ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
        catch (Exception ex) when (IsProviderFailure(ex, ct))
        {
            _logger.LogError(ex, "Maxio customer lookup by reference failed");
            throw new BillingUnavailableException("The billing system could not be reached.", ex);
        }
    }

    // ------------------------------------------------------------------ read

    public Task<IReadOnlyList<SubscriptionSummary>> GetSubscriptionsAsync(string userId, CancellationToken cancellationToken) =>
        Bounded<IReadOnlyList<SubscriptionSummary>>(async budget =>
        {
            var link = await _db.UserSubscriptionLinks.FindAsync(new object[] { userId }, budget);
            if (link == null) return Array.Empty<SubscriptionSummary>();
            if (link.Status != SubscriptionLinkStatus.Active) await SettleAsync(link, budget);

            if (link.Status != SubscriptionLinkStatus.Active)
            {
                return _db.Entry(link).State == EntityState.Detached
                    ? Array.Empty<SubscriptionSummary>()
                    : new[] { new SubscriptionSummary(link.PlanHandle, null, null, null, null, "unconfirmed", null) };
            }
            return new[] { await ReadSummaryAsync(link, budget) };
        }, cancellationToken);

    private async Task<SubscriptionSummary> ReadSummaryAsync(UserSubscriptionLink link, CancellationToken ct)
    {
        try
        {
            var r = await _client.Subscriptions.ReadSubscription(
                new ReadSubscriptionRequest { SubscriptionId = link.MaxioSubscriptionId!.Value }, cancellationToken: ct);
            if (r.Subscription == null) throw new UnreadableResponseException();
            return ToSummary(r.Subscription, link.PlanHandle);
        }
        catch (Exception ex) when (IsProviderFailure(ex, ct))
        {
            _logger.LogError(ex, "Maxio subscription read failed for user {UserId}", link.UserId);
            throw new BillingUnavailableException("The billing system could not be reached.", ex);
        }
    }

    // --------------------------------------------------------------- helpers

    private static SubscriptionSummary ToSummary(Subscription s, string fallbackPlan) => new(
        s.Product?.Handle ?? fallbackPlan,
        s.Product?.Name,
        s.ProductPriceInCents ?? s.Product?.PriceInCents,
        s.Product?.Interval,
        s.Product?.IntervalUnit?.Value,
        s.State?.Value,
        s.NextAssessmentAt);

    private async Task RecordActiveAsync(UserSubscriptionLink link, int subscriptionId)
    {
        link.MaxioSubscriptionId = subscriptionId;
        link.Status = SubscriptionLinkStatus.Active;
        await _db.SaveChangesAsync(CancellationToken.None);
    }

    private async Task<int> RecordCustomerAsync(UserCustomerLink link, int customerId)
    {
        link.MaxioCustomerId = customerId;
        await _db.SaveChangesAsync(CancellationToken.None);
        return customerId;
    }

    private async Task ReleaseAsync(UserSubscriptionLink link)
    {
        _db.UserSubscriptionLinks.Remove(link);
        await _db.SaveChangesAsync(CancellationToken.None);
    }

    private async Task ReleaseCustomerClaimAsync(UserCustomerLink link)
    {
        _db.UserCustomerLinks.Remove(link);
        await _db.SaveChangesAsync(CancellationToken.None);
    }

    private static (string First, string Last) SplitName(string displayName)
    {
        var parts = (displayName ?? "").Split(' ', 2, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return parts.Length switch
        {
            0 => ("Customer", "Customer"),
            1 => (parts[0], parts[0]),
            _ => (parts[0], parts[1])
        };
    }

    // The store refused the claim (primary key). The in-memory provider reports it as a raw ArgumentException.
    private static bool IsClaimConflict(Exception ex) => ex is DbUpdateException || ex is ArgumentException;

    private static bool IsProviderFailure(Exception ex, CancellationToken ct) =>
        ex is SdkException || ex is UnreadableResponseException || (ex is OperationCanceledException && !ct.IsCancellationRequested);

    private static bool IsAmbiguous(Exception ex, CancellationToken caller) =>
        ex is ApiException || ex is SdkConnectionException || ex is UnreadableResponseException
        || (ex is OperationCanceledException && !caller.IsCancellationRequested);

    private async Task<T> Bounded<T>(Func<CancellationToken, Task<T>> call, CancellationToken caller)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(caller);
        cts.CancelAfter(CallBudget);
        try
        {
            return await call(cts.Token);
        }
        catch (OperationCanceledException) when (!caller.IsCancellationRequested)
        {
            throw new BillingUnavailableException("The billing system did not answer in time.");
        }
    }

    private sealed class UnreadableResponseException : Exception { }
}
