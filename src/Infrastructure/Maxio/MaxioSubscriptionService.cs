using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.Exceptions;
using MaxioAdvancedBilling.Core.ErrorResponse;
using MaxioAdvancedBilling.Errors;
using MaxioAdvancedBilling.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Subscriptions backed by Maxio Advanced Billing.
/// Every write follows: claim (local store, primary key) -> Maxio call -> record the result.
/// </summary>
public class MaxioSubscriptionService : ISubscriptionService
{
    /// <summary>Bound on one HTTP attempt (HttpClient and SDK retry pipeline).</summary>
    public static readonly TimeSpan AttemptTimeout = TimeSpan.FromSeconds(10);
    /// <summary>Bound on a whole service operation, however many SDK calls and retries it makes.</summary>
    public static readonly TimeSpan OperationBudget = TimeSpan.FromSeconds(30);
    /// <summary>A claim with no recorded result older than this may be taken over.</summary>
    public static readonly TimeSpan ClaimLease = TimeSpan.FromMinutes(2);

    private const int PLAN_PAGE_SIZE = 100;
    private const int PLAN_MAX_PAGES = 20;

    private readonly MaxioAdvancedBillingClient _client;
    private readonly CatalogContext _db;
    private readonly MaxioSettings _settings;
    private readonly ILogger<MaxioSubscriptionService> _logger;
    private readonly TimeProvider _clock;

    public MaxioSubscriptionService(MaxioAdvancedBillingClient client, CatalogContext db,
        IOptions<MaxioSettings> settings, ILogger<MaxioSubscriptionService> logger)
        : this(client, db, settings, logger, TimeProvider.System)
    {
    }

    public MaxioSubscriptionService(MaxioAdvancedBillingClient client, CatalogContext db,
        IOptions<MaxioSettings> settings, ILogger<MaxioSubscriptionService> logger, TimeProvider clock)
    {
        _client = client;
        _db = db;
        _settings = settings.Value;
        _logger = logger;
        _clock = clock;
    }

    public Task<SubscriptionPlanList> ListPlansAsync(CancellationToken cancellationToken) =>
        Bounded(async ct =>
        {
            var (products, truncated) = await ReadPlansAsync(ct);
            return new SubscriptionPlanList(products.Select(ToPlan).ToList(), truncated);
        }, cancellationToken);

    public Task<SubscribeResult> SubscribeAsync(SubscribeCommand command, CancellationToken cancellationToken) =>
        Bounded(async ct =>
        {
            if (string.IsNullOrWhiteSpace(command.UserName))
                throw new SubscriptionException("Caller identity is missing.", 401);
            if (string.IsNullOrWhiteSpace(command.PlanHandle))
                throw new SubscriptionException("planHandle is required.", 400);

            var userKey = Normalize(command.UserName);
            var planHandle = command.PlanHandle.Trim();

            // Invariant: only a plan the configured product family offers may be subscribed to.
            var (products, _) = await ReadPlansAsync(ct);
            var plan = products.FirstOrDefault(p => string.Equals(p.Handle, planHandle, StringComparison.Ordinal));
            if (plan is null)
                throw new SubscriptionException($"Unknown subscription plan '{planHandle}'.", 404);

            var customerId = await EnsureCustomerAsync(userKey, command, ct);
            var (subscriptionId, existed) = await EnsureSubscriptionAsync(userKey, plan.Handle!, customerId, ct);

            var response = await _client.Subscriptions.ReadSubscription(
                new MaxioAdvancedBilling.Requests.Subscriptions.ReadSubscriptionRequest { SubscriptionId = subscriptionId },
                cancellationToken: ct);
            var summary = ToSummary(response.Subscription
                ?? throw new SubscriptionException("Billing provider returned an incomplete response.", 502));
            _logger.LogInformation("Subscribe {Outcome}: user {User} plan {Plan} maxio subscription {SubscriptionId}",
                existed ? "already-subscribed" : "created", userKey, plan.Handle, subscriptionId);
            return new SubscribeResult(summary, existed);
        }, cancellationToken);

    public Task<IReadOnlyList<SubscriptionSummary>> ListMineAsync(string userName, CancellationToken cancellationToken) =>
        Bounded<IReadOnlyList<SubscriptionSummary>>(async ct =>
        {
            if (string.IsNullOrWhiteSpace(userName))
                throw new SubscriptionException("Caller identity is missing.", 401);

            // The customer id comes from what this application stored, never from the caller.
            var link = await _db.MaxioCustomerLinks.AsNoTracking()
                .FirstOrDefaultAsync(l => l.UserName == Normalize(userName), ct);
            if (link?.MaxioId is not int customerId)
                return Array.Empty<SubscriptionSummary>();

            var subscriptions = await _client.Customers.ListCustomerSubscriptions(
                new MaxioAdvancedBilling.Requests.Customers.ListCustomerSubscriptionsRequest { CustomerId = customerId },
                cancellationToken: ct);
            return subscriptions
                .Where(s => s.Subscription is not null)
                .Select(s => ToSummary(s.Subscription!))
                .ToList();
        }, cancellationToken);

    // ---- plans -------------------------------------------------------------------------------------------

    private async Task<(List<Product> Products, bool Truncated)> ReadPlansAsync(CancellationToken ct)
    {
        var products = new List<Product>();
        try
        {
            for (var page = 1; page <= PLAN_MAX_PAGES; page++)
            {
                var items = await _client.ProductFamilies.ListProductsForProductFamily(
                    new MaxioAdvancedBilling.Requests.ProductFamilies.ListProductsForProductFamilyRequest
                    {
                        ProductFamilyId = $"handle:{_settings.ProductFamilyHandle}",
                        Page = page,
                        PerPage = PLAN_PAGE_SIZE,
                    },
                    cancellationToken: ct);

                products.AddRange(items.Select(i => i.Product)
                    .Where(p => p is not null && p.ArchivedAt is null && !string.IsNullOrEmpty(p.Handle)));
                if (items.Count < PLAN_PAGE_SIZE)
                    return (products, false);
            }
            return (products, true); // page cap reached: the list may be incomplete
        }
        catch (ApiException<ListProductsForProductFamilyError> ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            _logger.LogError("Configured Maxio product family was not found (Maxio:ProductFamilyHandle).");
            throw new SubscriptionException("Subscription plans are not available.", 502, ex);
        }
    }

    private static SubscriptionPlan ToPlan(Product p) =>
        new(p.Handle!, p.Name ?? p.Handle!, p.Description, p.PriceInCents ?? 0, p.Interval, p.IntervalUnit?.Value);

    private static SubscriptionSummary ToSummary(Subscription s) =>
        new(s.Id ?? throw new SubscriptionException("Billing provider returned an incomplete response.", 502),
            s.Product?.Handle,
            s.Product?.Name,
            s.CurrentBillingAmountInCents ?? s.ProductPriceInCents ?? s.Product?.PriceInCents,
            s.State?.Value,
            s.NextAssessmentAt,
            s.CurrentPeriodEndsAt,
            s.Reference);

    // ---- customer ----------------------------------------------------------------------------------------

    private async Task<int> EnsureCustomerAsync(string userKey, SubscribeCommand command, CancellationToken ct)
    {
        var email = command.UserName.Trim();
        if (!email.Contains('@'))
            throw new SubscriptionException("The account's user name is not an email address, so a billing customer cannot be created.", 422);
        var local = email[..email.IndexOf('@')];

        var reference = $"eshop-user:{userKey}";
        var (id, _) = await EnsureClaimedAsync(
            _db.MaxioCustomerLinks,
            new MaxioCustomerLink { UserName = userKey, Reference = reference },
            [userKey],
            find: FindCustomerAsync,
            create: async token =>
            {
                try
                {
                    var created = await _client.Customers.CreateCustomer(
                        new MaxioAdvancedBilling.Requests.Customers.CreateCustomerOperationRequest
                        {
                            Body = new CreateCustomerRequest
                            {
                                Customer = new CreateCustomer
                                {
                                    FirstName = string.IsNullOrWhiteSpace(command.FirstName) ? local : command.FirstName.Trim(),
                                    LastName = string.IsNullOrWhiteSpace(command.LastName) ? "Customer" : command.LastName.Trim(),
                                    Email = email,
                                    Reference = reference,
                                },
                            },
                        },
                        cancellationToken: token);
                    return created.Customer.Id;
                }
                catch (ApiException<CreateCustomerError> ex) when (ex.StatusCode == HttpStatusCode.UnprocessableEntity)
                {
                    throw Rejected(ex, "customer", null);
                }
            },
            what: "customer",
            ct);
        return id;
    }

    private async Task<int?> FindCustomerAsync(string reference, CancellationToken ct)
    {
        try
        {
            var found = await _client.Customers.ReadCustomerByReference(
                new MaxioAdvancedBilling.Requests.Customers.ReadCustomerByReferenceRequest { Reference = reference },
                cancellationToken: ct);
            return found.Customer.Id;
        }
        catch (ApiException<RawError> ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    // ---- subscription ------------------------------------------------------------------------------------

    private Task<(int Id, bool Existed)> EnsureSubscriptionAsync(string userKey, string planHandle, int customerId, CancellationToken ct)
    {
        var reference = $"eshop-sub:{userKey}:{planHandle}";
        return EnsureClaimedAsync(
            _db.UserSubscriptions,
            new UserSubscription { UserName = userKey, PlanHandle = planHandle, Reference = reference },
            [userKey, planHandle],
            find: FindSubscriptionAsync,
            create: async token =>
            {
                try
                {
                    var created = await _client.Subscriptions.CreateSubscription(
                        new MaxioAdvancedBilling.Requests.Subscriptions.CreateSubscriptionOperationRequest
                        {
                            Body = new CreateSubscriptionRequest
                            {
                                Subscription = new CreateSubscription
                                {
                                    ProductHandle = planHandle,
                                    CustomerId = customerId,
                                    Reference = reference,
                                    // No card is captured at subscribe time, so bill by invoice rather than automatic card collection.
                                    PaymentCollectionMethod = MaxioAdvancedBilling.Models.Enums.CollectionMethod.Remittance,
                                },
                            },
                        },
                        cancellationToken: token);
                    return created.Subscription?.Id;
                }
                catch (ApiException<CreateSubscriptionError> ex) when (ex.StatusCode == HttpStatusCode.UnprocessableEntity)
                {
                    throw Rejected(ex, "subscription", ex.Error.TryGetErrorListResponse1(out var list) ? list.Errors : null);
                }
            },
            what: "subscription",
            ct);
    }

    private async Task<int?> FindSubscriptionAsync(string reference, CancellationToken ct)
    {
        try
        {
            var found = await _client.Subscriptions.FindSubscription(
                new MaxioAdvancedBilling.Requests.Subscriptions.FindSubscriptionRequest { Reference = reference },
                cancellationToken: ct);
            return found.Subscription?.Id;
        }
        catch (ApiException<FindSubscriptionError> ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    // ---- claim -> call -> record -------------------------------------------------------------------------

    /// <summary>
    /// Claims <paramref name="fresh"/> (the primary key refuses a second claimant), reconciles against Maxio by
    /// reference, creates only when Maxio has nothing, then records the id. Returns the Maxio id and whether it
    /// already existed.
    /// </summary>
    private async Task<(int Id, bool Existed)> EnsureClaimedAsync<T>(
        DbSet<T> set, T fresh, object[] key,
        Func<string, CancellationToken, Task<int?>> find,
        Func<CancellationToken, Task<int?>> create,
        string what, CancellationToken ct) where T : MaxioClaim
    {
        // 1. claim
        T claim;
        set.Add(fresh);
        try
        {
            await _db.SaveChangesAsync(ct);
            claim = fresh;
        }
        catch (Exception ex) when (IsDuplicateKey(ex))
        {
            _db.Entry(fresh).State = EntityState.Detached;
            var existing = await set.FindAsync(key, ct);
            if (existing is null)
                throw; // not a duplicate claim: a genuine store failure

            if (existing.MaxioId is int known)
                return (known, true);

            if (_clock.GetUtcNow() - existing.ClaimedAt <= ClaimLease)
            {
                // Another request holds the claim and has not recorded a result.
                var adopted = await find(existing.Reference, ct);
                if (adopted is int id)
                {
                    await RecordAsync(existing, id);
                    return (id, true);
                }
                throw new SubscriptionException($"A {what} request for this account is already in progress; retry shortly.", 409);
            }

            // Stale claim (crashed or unknown outcome): take it over; ClaimedAt is the concurrency token.
            try
            {
                existing.ClaimedAt = _clock.GetUtcNow();
                existing.State = ClaimState.Claimed;
                await _db.SaveChangesAsync(ct);
            }
            catch (Exception conflict) when (IsDuplicateKey(conflict))
            {
                throw new SubscriptionException($"A {what} request for this account is already in progress; retry shortly.", 409);
            }
            claim = existing;
        }

        // 2. reconcile, then call Maxio
        var writeSent = false;
        try
        {
            var present = await find(claim.Reference, ct);
            if (present is int found)
            {
                await RecordAsync(claim, found);
                return (found, true);
            }

            writeSent = true;
            if (await create(ct) is not int created)
            {
                // 2xx without an id: the write landed but we cannot read its result.
                return await SettleUnknownAsync(claim, find, what, new InvalidOperationException("Maxio response carried no id."));
            }

            await RecordAsync(claim, created);
            return (created, false);
        }
        catch (SubscriptionException)
        {
            // Maxio refused the call (or it never went out): nothing was written, release the claim.
            await ReleaseAsync(set, claim);
            throw;
        }
        catch (Exception ex) when (writeSent && IsAmbiguous(ex))
        {
            // The write may have landed. Settle by re-reading on the reference we sent.
            return await SettleUnknownAsync(claim, find, what, ex);
        }
        catch (Exception)
        {
            // Failure before the write went out, or a definite provider rejection.
            await ReleaseAsync(set, claim);
            throw;
        }
    }

    // Relational stores raise DbUpdateException; the in-memory provider surfaces the same refusal as a bare ArgumentException.
    private static bool IsDuplicateKey(Exception ex) => ex is DbUpdateException or ArgumentException;

    private static bool IsAmbiguous(Exception ex) =>
        ex is SdkConnectionException // includes SdkTimeoutException
        || ex is ResponseDeserializationException { StatusCode: >= HttpStatusCode.OK and < HttpStatusCode.MultipleChoices }
        || ex is ApiException { StatusCode: >= HttpStatusCode.InternalServerError } // a 5xx on a write does not prove nothing happened
        || ex is OperationCanceledException;

    private async Task<(int Id, bool Existed)> SettleUnknownAsync<T>(
        T claim, Func<string, CancellationToken, Task<int?>> find, string what, Exception cause) where T : MaxioClaim
    {
        _logger.LogWarning(cause, "Maxio {What} write outcome unknown; reconciling on reference {Reference}", what, claim.Reference);
        try
        {
            using var cts = new CancellationTokenSource(AttemptTimeout);
            if (await find(claim.Reference, cts.Token) is int id)
            {
                await RecordAsync(claim, id);
                return (id, false);
            }
        }
        catch (Exception reread)
        {
            _logger.LogWarning(reread, "Re-reading Maxio {What} by reference failed", what);
        }

        claim.State = ClaimState.Unknown;
        await _db.SaveChangesAsync(CancellationToken.None);
        if (cause is OperationCanceledException)
            throw cause; // the caller's own cancellation / budget; the claim stays Unknown for the next attempt
        throw new SubscriptionException(
            $"The billing provider did not confirm the {what}; its outcome is pending. Retry shortly — nothing will be duplicated.", 503, cause);
    }

    private async Task RecordAsync(MaxioClaim claim, int maxioId)
    {
        claim.MaxioId = maxioId;
        claim.State = ClaimState.Active;
        await _db.SaveChangesAsync(CancellationToken.None); // never abandon a recorded result because the caller left
    }

    private async Task ReleaseAsync<T>(DbSet<T> set, T claim) where T : MaxioClaim
    {
        try
        {
            set.Remove(claim);
            await _db.SaveChangesAsync(CancellationToken.None);
        }
        catch (Exception ex) when (IsDuplicateKey(ex))
        {
            _logger.LogWarning(ex, "Could not release claim {Reference}; it will expire after {Lease}", claim.Reference, ClaimLease);
        }
    }

    // ---- error boundary ----------------------------------------------------------------------------------

    /// <summary>One home for the total time budget and the SDK-exception -> SubscriptionException mapping.</summary>
    private async Task<T> Bounded<T>(Func<CancellationToken, Task<T>> call, CancellationToken caller)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(caller);
        cts.CancelAfter(OperationBudget);
        try
        {
            return await call(cts.Token);
        }
        catch (SubscriptionException)
        {
            throw;
        }
        catch (OperationCanceledException ex) when (!caller.IsCancellationRequested)
        {
            throw new SubscriptionException("The billing provider did not answer in time.", 504, ex);
        }
        catch (ApiException<RawError> ex)
        {
            throw FromStatus(ex);
        }
        catch (ResponseDeserializationException ex)
        {
            // 2xx: unknown outcome (handled where a write is involved); error status: detail lost, status kept.
            _logger.LogError(ex, "Unreadable Maxio response ({Status}) for {Target}", (int)ex.StatusCode, ex.TargetType);
            throw FromStatus(ex);
        }
        catch (ApiException ex)
        {
            throw FromStatus(ex);
        }
        catch (SdkTimeoutException ex)
        {
            _logger.LogError(ex, "Maxio call timed out");
            throw new SubscriptionException("The billing provider did not answer in time.", 504, ex);
        }
        catch (SdkConnectionException ex)
        {
            _logger.LogError(ex, "Maxio unreachable");
            throw new SubscriptionException("The billing provider is unreachable.", 503, ex);
        }
        catch (AuthSchemeException ex)
        {
            _logger.LogError(ex, "Maxio credentials could not be applied");
            throw new SubscriptionException("The billing provider is unavailable.", 502, ex);
        }
    }

    private SubscriptionException FromStatus(ApiException ex)
    {
        _logger.LogWarning("Maxio returned {Status} for {Method} {Uri}", (int)ex.StatusCode, ex.Method, ex.RequestUri);
        return ex.StatusCode switch
        {
            // Our credentials or quota: the caller did nothing wrong.
            HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden =>
                new SubscriptionException("The billing provider is unavailable.", 502, ex),
            HttpStatusCode.TooManyRequests =>
                new SubscriptionException("The billing provider is busy; retry shortly.", 503, ex),
            // Unmapped statuses are unknowns, not caller errors.
            _ => new SubscriptionException("The billing provider could not complete the request.", 502, ex),
        };
    }

    /// <summary>A 422 from Maxio: the caller's request was refused, and the detail is safe to return.</summary>
    private SubscriptionException Rejected(ApiException ex, string what, IReadOnlyList<string>? details)
    {
        var detail = details is { Count: > 0 } ? $" ({string.Join("; ", details)})" : string.Empty;
        _logger.LogWarning("Maxio rejected {What}{Detail}", what, detail);
        return new SubscriptionException($"The billing provider rejected the {what}{detail}.", 422, ex);
    }

    private static string Normalize(string userName) => userName.Trim().ToLowerInvariant();
}
