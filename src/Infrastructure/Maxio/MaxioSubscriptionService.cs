using System;
using System.Collections.Generic;
using System.Linq;
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
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Subscribes shoppers to Maxio plans. Order for every write: claim (row keyed by user id) -> SDK call -> record result.
/// </summary>
public class MaxioSubscriptionService : ISubscriptionService
{
    private const int PerPage = 200;
    private const int MaxPages = 5;
    private static readonly TimeSpan ReadBudget = TimeSpan.FromSeconds(20);
    private static readonly TimeSpan SubscribeBudget = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan ReconcileBudget = TimeSpan.FromSeconds(10);
    /// <summary>A Claiming row older than this is treated as abandoned and settled.</summary>
    public static readonly TimeSpan StaleClaimAfter = TimeSpan.FromMinutes(2);

    private readonly MaxioAdvancedBillingClient _client;
    private readonly CatalogContext _db;
    private readonly MaxioSettings _settings;
    private readonly TimeProvider _clock;
    private readonly ILogger<MaxioSubscriptionService> _logger;

    public MaxioSubscriptionService(
        MaxioAdvancedBillingClient client,
        CatalogContext db,
        IOptions<MaxioSettings> settings,
        TimeProvider clock,
        ILogger<MaxioSubscriptionService> logger)
    {
        _client = client;
        _db = db;
        _settings = settings.Value;
        _clock = clock;
        _logger = logger;
    }

    // ---------------------------------------------------------------- public operations

    public async Task<SubscriptionPlanListing> ListPlansAsync(CancellationToken cancellationToken)
    {
        return await BoundedAsync(ReadBudget, cancellationToken, async ct =>
        {
            var listing = await ListPlansCoreAsync(ct);
            _logger.LogInformation("Plan list served: {Count} plans, partial={Partial}", listing.Plans.Count, listing.IsPartial);
            return listing;
        });
    }

    public async Task<SubscribeResult> SubscribeAsync(SubscriberIdentity subscriber, string? planHandle, CancellationToken cancellationToken)
    {
        var handle = string.IsNullOrWhiteSpace(planHandle) ? SubscriptionConstants.DefaultPlanHandle : planHandle.Trim();
        return await BoundedAsync(SubscribeBudget, cancellationToken, ct => SubscribeCoreAsync(subscriber, handle, ct));
    }

    public async Task<IReadOnlyList<SubscriptionInfo>> GetSubscriptionsAsync(SubscriberIdentity subscriber, CancellationToken cancellationToken)
    {
        return await BoundedAsync<IReadOnlyList<SubscriptionInfo>>(ReadBudget, cancellationToken, async ct =>
        {
            MaxioSubscriptionLink? link = await _db.MaxioSubscriptionLinks.FirstOrDefaultAsync(l => l.UserId == subscriber.UserId, ct);
            if (link is null) return (IReadOnlyList<SubscriptionInfo>)Array.Empty<SubscriptionInfo>();

            if (link.State != MaxioSubscriptionLinkState.Active)
            {
                if (link.State == MaxioSubscriptionLinkState.Claiming && !IsStale(link))
                {
                    throw new SubscriptionException(409, "A subscription request is still being processed.");
                }
                if (!await SettleAsync(link, ct))
                {
                    return Array.Empty<SubscriptionInfo>();
                }
            }

            var info = await ReadSubscriptionAsync(link.MaxioSubscriptionId!.Value, ct);
            return info is null ? Array.Empty<SubscriptionInfo>() : new[] { info };
        });
    }

    // ---------------------------------------------------------------- subscribe

    private async Task<SubscribeResult> SubscribeCoreAsync(SubscriberIdentity who, string handle, CancellationToken ct)
    {
        // Invariant: the plan must be one the family listing offers (never trust the caller's handle).
        var listing = await ListPlansCoreAsync(ct);
        var plan = listing.Plans.FirstOrDefault(p => string.Equals(p.Handle, handle, StringComparison.OrdinalIgnoreCase));
        if (plan is null)
        {
            throw listing.IsPartial
                ? new SubscriptionException(503, "The plan catalogue is incomplete; try again later.")
                : new SubscriptionException(400, "Unknown subscription plan.");
        }

        var claim = await ClaimAsync(who.UserId, plan.Handle, ct);
        if (claim.Existing is { } existing)
        {
            // Claim lost to an Active row: idempotent answer (or conflict when it is another plan).
            if (!string.Equals(existing.ProductHandle, plan.Handle, StringComparison.OrdinalIgnoreCase))
            {
                throw new SubscriptionException(409, "You already have a subscription to a different plan.");
            }
            var current = await ReadSubscriptionAsync(existing.MaxioSubscriptionId!.Value, ct)
                ?? throw new SubscriptionException(502, "The subscription could not be read from the billing provider.");
            _logger.LogInformation("Subscribe claim lost to an active row for user {UserId}; returning existing subscription {SubscriptionId}",
                who.UserId, existing.MaxioSubscriptionId);
            return new SubscribeResult(current, false);
        }

        var link = claim.Won!;
        _logger.LogInformation("Subscribe claim won for user {UserId}, plan {Plan}", who.UserId, plan.Handle);
        try
        {
            var info = await PerformAsync(link, who, plan.Handle, ct);
            return new SubscribeResult(info, true);
        }
        catch (MaxioFailure f)
        {
            // Unknown outcomes keep the row (settled later); every other failure releases the claim.
            if (link.State != MaxioSubscriptionLinkState.Unknown)
            {
                await ReleaseAsync(link);
            }
            throw f.Public;
        }
    }

    private async Task<SubscriptionInfo> PerformAsync(MaxioSubscriptionLink link, SubscriberIdentity who, string handle, CancellationToken ct)
    {
        var customerCreated = false;
        if (link.MaxioCustomerId is null)
        {
            (link.MaxioCustomerId, customerCreated) = await EnsureCustomerAsync(who, link, ct);
            await SaveAsync(link, ct);
        }

        // A customer that already existed may have a subscription from an earlier, lost attempt.
        if (!customerCreated)
        {
            var already = await FindExistingSubscriptionAsync(link.MaxioCustomerId!.Value, handle, ct);
            if (already is not null)
            {
                return await RecordActiveAsync(link, already, ct);
            }
        }

        Subscription? created = null;
        MaxioFailure? ambiguous = null;
        try
        {
            var response = await CallAsync("CreateSubscription", () => _client.Subscriptions.CreateSubscription(
                new CreateSubscriptionOperationRequest
                {
                    Body = new CreateSubscriptionRequest
                    {
                        Subscription = new CreateSubscription
                        {
                            ProductHandle = handle,
                            CustomerId = link.MaxioCustomerId,
                            // No payment profile is collected: bill by invoice (remittance) instead of failing on a missing card.
                            PaymentCollectionMethod = CollectionMethod.Remittance,
                            Reference = who.UserId
                        }
                    }
                }, cancellationToken: ct));
            created = response.Subscription;
            if (created?.Id is null)
            {
                // A 2xx we cannot read is an unknown outcome, not a failure.
                ambiguous = new MaxioFailure("CreateSubscription", 200, true,
                    new SubscriptionException(502, "The billing provider returned an unreadable response."));
            }
        }
        catch (MaxioFailure f) when (f.Ambiguous)
        {
            ambiguous = f;
        }

        if (ambiguous is not null)
        {
            using var cts = new CancellationTokenSource(ReconcileBudget);
            Subscription? found = null;
            try
            {
                found = await FindExistingSubscriptionAsync(link.MaxioCustomerId!.Value, handle, cts.Token);
            }
            catch (Exception e) when (e is MaxioFailure or OperationCanceledException)
            {
                _logger.LogWarning("Reconciliation read after an ambiguous CreateSubscription failed: {Reason}", e.GetType().Name);
            }

            if (found is null)
            {
                link.State = MaxioSubscriptionLinkState.Unknown;
                await SaveAsync(link, CancellationToken.None);
                _logger.LogWarning("CreateSubscription outcome unknown for user {UserId}; row kept as Unknown", who.UserId);
                throw ambiguous;
            }
            created = found;
        }

        return await RecordActiveAsync(link, created!, ct);
    }

    private async Task<SubscriptionInfo> RecordActiveAsync(MaxioSubscriptionLink link, Subscription subscription, CancellationToken ct)
    {
        link.MaxioSubscriptionId = subscription.Id;
        link.State = MaxioSubscriptionLinkState.Active;
        await SaveAsync(link, CancellationToken.None);
        var info = ToInfo(subscription)!;
        _logger.LogInformation("Subscription {SubscriptionId} active for customer {CustomerId}, state {State}",
            link.MaxioSubscriptionId, link.MaxioCustomerId, info.State);
        return info;
    }

    // ---------------------------------------------------------------- claim

    private sealed record ClaimResult(MaxioSubscriptionLink? Won, MaxioSubscriptionLink? Existing);

    /// <summary>
    /// Inserts the row keyed by user id. The primary key refuses a second claim; that refusal is caught here.
    /// </summary>
    private async Task<ClaimResult> ClaimAsync(string userId, string handle, CancellationToken ct)
    {
        for (var attempt = 0; attempt < 4; attempt++)
        {
            var now = _clock.GetUtcNow();
            var row = new MaxioSubscriptionLink
            {
                UserId = userId,
                ProductHandle = handle,
                State = MaxioSubscriptionLinkState.Claiming,
                ClaimedAtUtc = now,
                UpdatedAtUtc = now
            };
            _db.MaxioSubscriptionLinks.Add(row);
            try
            {
                await _db.SaveChangesAsync(ct);
                return new ClaimResult(row, null);
            }
            catch (Exception e) when (e is DbUpdateException or InvalidOperationException or ArgumentException)
            {
                _db.Entry(row).State = EntityState.Detached;
            }

            var existing = await _db.MaxioSubscriptionLinks.FirstOrDefaultAsync(l => l.UserId == userId, ct);
            if (existing is null) continue; // released in between: claim again

            if (existing.State == MaxioSubscriptionLinkState.Active)
            {
                return new ClaimResult(null, existing);
            }
            if (existing.State == MaxioSubscriptionLinkState.Claiming && !IsStale(existing))
            {
                _logger.LogInformation("Subscribe claim lost to an in-flight request for user {UserId}", userId);
                throw new SubscriptionException(409, "A subscription request is already being processed.");
            }

            // Stale Claiming or Unknown: settle against Maxio, then adopt (Active) or claim again (released).
            if (await SettleAsync(existing, ct))
            {
                return new ClaimResult(null, existing);
            }
        }
        throw new SubscriptionException(409, "A subscription request is already being processed.");
    }

    private bool IsStale(MaxioSubscriptionLink link) => _clock.GetUtcNow() - link.UpdatedAtUtc > StaleClaimAfter;

    /// <summary>
    /// Settles an abandoned/unknown row by re-reading Maxio. Returns true when the row is now Active
    /// (subscription adopted), false when it was released (nothing found at Maxio).
    /// </summary>
    private async Task<bool> SettleAsync(MaxioSubscriptionLink link, CancellationToken ct)
    {
        try
        {
            var customerId = link.MaxioCustomerId ?? await FindCustomerByReferenceAsync(link.UserId, ct);
            if (customerId is int cid)
            {
                var found = await FindExistingSubscriptionAsync(cid, link.ProductHandle, ct);
                if (found is not null)
                {
                    link.MaxioCustomerId = cid;
                    link.MaxioSubscriptionId = found.Id;
                    link.State = MaxioSubscriptionLinkState.Active;
                    await SaveAsync(link, CancellationToken.None);
                    _logger.LogInformation("Settled row for user {UserId}: adopted subscription {SubscriptionId}", link.UserId, found.Id);
                    return true;
                }
            }
        }
        catch (MaxioFailure f)
        {
            throw f.Public;
        }

        await ReleaseAsync(link);
        _logger.LogInformation("Settled row for user {UserId}: nothing at Maxio, claim released", link.UserId);
        return false;
    }

    private async Task ReleaseAsync(MaxioSubscriptionLink link)
    {
        try
        {
            if (_db.Entry(link).State == EntityState.Detached) _db.MaxioSubscriptionLinks.Attach(link);
            _db.MaxioSubscriptionLinks.Remove(link);
            await _db.SaveChangesAsync(CancellationToken.None);
            _logger.LogWarning("Claim released for user {UserId}", link.UserId);
        }
        catch (DbUpdateConcurrencyException)
        {
            // already gone
            _db.Entry(link).State = EntityState.Detached;
        }
    }

    private async Task SaveAsync(MaxioSubscriptionLink link, CancellationToken ct)
    {
        link.UpdatedAtUtc = _clock.GetUtcNow();
        await _db.SaveChangesAsync(ct);
    }

    // ---------------------------------------------------------------- Maxio reads/writes

    private async Task<SubscriptionPlanListing> ListPlansCoreAsync(CancellationToken ct)
    {
        var familyId = "handle:" + _settings.ProductFamilyHandle;
        var plans = new List<SubscriptionPlan>();
        var partial = false;
        try
        {
            for (var page = 1; page <= MaxPages; page++)
            {
                var items = await CallAsync("ListProductsForProductFamily", () => _client.ProductFamilies.ListProductsForProductFamily(
                    new ListProductsForProductFamilyRequest { ProductFamilyId = familyId, Page = page, PerPage = PerPage },
                    cancellationToken: ct));

                foreach (var item in items)
                {
                    var p = item.Product;
                    if (p.ArchivedAt is not null || string.IsNullOrWhiteSpace(p.Handle)) continue;
                    plans.Add(new SubscriptionPlan(
                        p.Handle!,
                        p.Name ?? p.Handle!,
                        p.PriceInCents ?? 0,
                        p.Interval ?? 1,
                        p.IntervalUnit?.Value ?? "month",
                        string.Equals(p.Handle, SubscriptionConstants.DefaultPlanHandle, StringComparison.OrdinalIgnoreCase)));
                }

                if (items.Count < PerPage) break;
                if (page == MaxPages) partial = true;
            }
        }
        catch (MaxioFailure f)
        {
            throw f.Public;
        }
        return new SubscriptionPlanListing(plans, partial);
    }

    private async Task<int?> FindCustomerByReferenceAsync(string reference, CancellationToken ct)
    {
        try
        {
            var response = await CallAsync("ReadCustomerByReference", () => _client.Customers.ReadCustomerByReference(
                new ReadCustomerByReferenceRequest { Reference = reference }, cancellationToken: ct));
            return response.Customer.Id;
        }
        catch (MaxioFailure f) when (f.HttpStatus == 404)
        {
            return null;
        }
    }

    private async Task<(int Id, bool Created)> EnsureCustomerAsync(SubscriberIdentity who, MaxioSubscriptionLink link, CancellationToken ct)
    {
        if (await FindCustomerByReferenceAsync(who.UserId, ct) is int existing)
        {
            _logger.LogInformation("Maxio customer {CustomerId} found for user {UserId}", existing, who.UserId);
            return (existing, false);
        }

        var email = !string.IsNullOrWhiteSpace(who.Email) ? who.Email!
            : (who.UserName?.Contains('@') == true ? who.UserName : null);
        if (email is null)
        {
            throw new MaxioFailure("CreateCustomer", 422, false,
                new SubscriptionException(422, "Your account has no email address; one is needed to subscribe."));
        }
        var first = email.Split('@')[0];

        try
        {
            var response = await CallAsync("CreateCustomer", () => _client.Customers.CreateCustomer(
                new CreateCustomerOperationRequest
                {
                    Body = new CreateCustomerRequest
                    {
                        Customer = new CreateCustomer
                        {
                            FirstName = string.IsNullOrWhiteSpace(first) ? "Shopper" : first,
                            LastName = "Shopper",
                            Email = email,
                            Reference = who.UserId
                        }
                    }
                }, cancellationToken: ct));
            var id = response.Customer.Id
                ?? throw new MaxioFailure("CreateCustomer", 200, true, new SubscriptionException(502, "The billing provider returned an unreadable response."));
            _logger.LogInformation("Maxio customer {CustomerId} created for user {UserId}", id, who.UserId);
            return (id, true);
        }
        catch (MaxioFailure f) when (f.Ambiguous)
        {
            // The create may have landed: re-read by the reference we sent.
            using var cts = new CancellationTokenSource(ReconcileBudget);
            try
            {
                if (await FindCustomerByReferenceAsync(who.UserId, cts.Token) is int found)
                {
                    _logger.LogInformation("Maxio customer {CustomerId} found after ambiguous create for user {UserId}", found, who.UserId);
                    return (found, true);
                }
            }
            catch (Exception e) when (e is MaxioFailure or OperationCanceledException)
            {
                _logger.LogWarning("Reconciliation read after an ambiguous CreateCustomer failed: {Reason}", e.GetType().Name);
            }
            link.State = MaxioSubscriptionLinkState.Unknown;
            await SaveAsync(link, CancellationToken.None);
            throw;
        }
    }

    private async Task<Subscription?> FindExistingSubscriptionAsync(int customerId, string handle, CancellationToken ct)
    {
        var list = await CallAsync("ListCustomerSubscriptions", () => _client.Customers.ListCustomerSubscriptions(
            new ListCustomerSubscriptionsRequest { CustomerId = customerId }, cancellationToken: ct));
        return list
            .Select(r => r.Subscription)
            .Where(s => s?.Id is not null
                        && string.Equals(s.Product?.Handle, handle, StringComparison.OrdinalIgnoreCase)
                        && !IsEnded(s.State))
            .OrderBy(s => s!.State == SubscriptionState.Active ? 0 : 1)
            .FirstOrDefault();
    }

    private static bool IsEnded(SubscriptionState? state) =>
        state is not null && (state == SubscriptionState.Canceled || state == SubscriptionState.Expired || state == SubscriptionState.FailedToCreate);

    private async Task<SubscriptionInfo?> ReadSubscriptionAsync(int subscriptionId, CancellationToken ct)
    {
        try
        {
            var response = await CallAsync("ReadSubscription", () => _client.Subscriptions.ReadSubscription(
                new ReadSubscriptionRequest { SubscriptionId = subscriptionId }, cancellationToken: ct));
            return ToInfo(response.Subscription);
        }
        catch (MaxioFailure f) when (f.HttpStatus == 404)
        {
            _logger.LogWarning("Subscription {SubscriptionId} no longer exists at the billing provider", subscriptionId);
            return null;
        }
        catch (MaxioFailure f)
        {
            throw f.Public;
        }
    }

    private static SubscriptionInfo? ToInfo(Subscription? s)
    {
        if (s?.Id is not int id) return null;
        var p = s.Product;
        return new SubscriptionInfo(
            id,
            p?.Handle ?? string.Empty,
            p?.Name ?? p?.Handle ?? string.Empty,
            p?.PriceInCents ?? s.ProductPriceInCents ?? 0,
            p?.Interval ?? 1,
            p?.IntervalUnit?.Value ?? "month",
            s.State?.Value ?? "unknown",
            s.NextAssessmentAt,
            s.CurrentPeriodEndsAt);
    }

    // ---------------------------------------------------------------- budget + error boundary

    /// <summary>One whole-call deadline, linked to the caller's token, shared by every SDK call of the operation.</summary>
    private static async Task<T> BoundedAsync<T>(TimeSpan budget, CancellationToken caller, Func<CancellationToken, Task<T>> work)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(caller);
        cts.CancelAfter(budget);
        try
        {
            return await work(cts.Token);
        }
        catch (MaxioFailure f)
        {
            throw f.Public;
        }
        catch (OperationCanceledException) when (!caller.IsCancellationRequested)
        {
            throw new SubscriptionException(504, "The billing provider did not answer in time.");
        }
    }

    /// <summary>Failure of one Maxio call, classified once. Never leaves this class.</summary>
    private sealed class MaxioFailure : Exception
    {
        public int? HttpStatus { get; }
        /// <summary>True when the write may still have landed (no usable answer).</summary>
        public bool Ambiguous { get; }
        public SubscriptionException Public { get; }

        public MaxioFailure(string operation, int? httpStatus, bool ambiguous, SubscriptionException @public, Exception? inner = null)
            : base($"Maxio {operation} failed (status {httpStatus?.ToString() ?? "none"}).", inner)
        {
            HttpStatus = httpStatus;
            Ambiguous = ambiguous;
            Public = @public;
        }
    }

    private async Task<T> CallAsync<T>(string operation, Func<Task<T>> call)
    {
        try
        {
            return await call();
        }
        catch (SdkTimeoutException ex)
        {
            _logger.LogError(ex, "Maxio {Operation} timed out", operation);
            throw new MaxioFailure(operation, null, true, new SubscriptionException(504, "The billing provider did not answer in time.", ex), ex);
        }
        catch (SdkConnectionException ex)
        {
            _logger.LogError(ex, "Maxio {Operation} could not reach the provider", operation);
            throw new MaxioFailure(operation, null, true, new SubscriptionException(503, "The billing provider is unreachable.", ex), ex);
        }
        catch (ResponseDeserializationException ex)
        {
            var status = (int)ex.StatusCode;
            _logger.LogError(ex, "Maxio {Operation} returned an unreadable body (status {Status})", operation, status);
            // Unreadable 2xx or 5xx: the write may have landed. An unreadable 4xx is a refusal.
            var ambiguous = status < 400 || status >= 500;
            throw new MaxioFailure(operation, status, ambiguous, new SubscriptionException(502, "The billing provider returned an unreadable response.", ex), ex);
        }
        catch (ApiException ex)
        {
            var status = (int)ex.StatusCode;
            _logger.LogWarning("Maxio {Operation} answered {Status}: {Detail}", operation, status, Detail(ex));
            SubscriptionException pub = status switch
            {
                401 or 403 => new SubscriptionException(502, "The billing provider is not available.", ex),
                429 => new SubscriptionException(503, "The billing provider is busy; try again later.", ex),
                >= 400 and < 500 and not 404 => new SubscriptionException(422, "The billing provider rejected the request.", ex),
                _ => new SubscriptionException(502, "The billing provider failed to process the request.", ex)
            };
            throw new MaxioFailure(operation, status, status >= 500, pub, ex);
        }
        catch (AuthSchemeException ex)
        {
            _logger.LogError(ex, "Maxio {Operation}: credentials could not be applied", operation);
            throw new MaxioFailure(operation, null, false, new SubscriptionException(502, "The billing provider is not available.", ex), ex);
        }
        catch (OperationCanceledException ex)
        {
            throw new MaxioFailure(operation, null, true, new SubscriptionException(504, "The billing provider did not answer in time.", ex), ex);
        }
    }

    private static string Detail(ApiException ex) => ex switch
    {
        ApiException<CreateSubscriptionError> e when e.Error.TryGetErrorListResponse1(out var list)
            => string.Join("; ", list.Errors.Take(3)),
        ApiException<CreateCustomerError> e when e.Error.TryGetCustomerErrorResponse1(out _)
            => "customer validation error",
        _ => ex.Message
    };
}
