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
using MaxioAdvancedBilling.Requests.Customers;
using MaxioAdvancedBilling.Requests.ProductFamilies;
using MaxioAdvancedBilling.Requests.Subscriptions;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.eShopWeb.Infrastructure.Identity;
using Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

/// <summary>
/// Plans / subscribe / my-subscriptions against Maxio. Every Maxio call goes through <see cref="CallAsync{T}"/>
/// under one shared time budget per request, and every SDK failure leaves as <see cref="SubscriptionException"/>.
/// </summary>
public class SubscriptionService
{
    private const int PlanPageSize = 200;
    private const int PlanPageCap = 5;
    private static readonly TimeSpan CallBudgetTotal = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan SettleBudgetTotal = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan ClaimExpiry = TimeSpan.FromMinutes(2);

    private readonly CatalogContext _db;
    private readonly UserManager<ApplicationUser> _users;
    private readonly MaxioAdvancedBillingClient _client;
    private readonly MaxioOptions _options;
    private readonly ILogger<SubscriptionService> _logger;

    public SubscriptionService(CatalogContext db, UserManager<ApplicationUser> users, MaxioAdvancedBillingClient client,
        IOptions<MaxioOptions> options, ILogger<SubscriptionService> logger)
    {
        _db = db;
        _users = users;
        _client = client;
        _options = options.Value;
        _logger = logger;
    }

    // ---- plans ---------------------------------------------------------------------------------------------

    public async Task<SubscriptionPlanList> ListPlansAsync(CancellationToken ct)
    {
        using var budget = new CallBudget(ct, CallBudgetTotal);
        return await ListPlansAsync(budget);
    }

    private async Task<SubscriptionPlanList> ListPlansAsync(CallBudget budget)
    {
        var plans = new List<SubscriptionPlanDto>();
        for (var page = 1; page <= PlanPageCap; page++)
        {
            var current = page;
            var products = (await CallAsync(budget, t => _client.ProductFamilies.ListProductsForProductFamily(
                new ListProductsForProductFamilyRequest
                {
                    ProductFamilyId = "handle:" + _options.ProductFamilyHandle,
                    Page = current,
                    PerPage = PlanPageSize,
                }, cancellationToken: t)))!;

            foreach (var p in products.Select(r => r.Product).Where(p => p is { Handle: not null, ArchivedAt: null }))
            {
                plans.Add(new SubscriptionPlanDto
                {
                    Handle = p!.Handle!,
                    Name = p.Name,
                    Description = p.Description,
                    PriceInCents = p.PriceInCents ?? 0,
                    Interval = p.Interval,
                    IntervalUnit = p.IntervalUnit?.Value,
                });
            }

            if (products.Count < PlanPageSize)
                return new SubscriptionPlanList { Plans = plans, IsComplete = true };
        }

        _logger.LogWarning("Plan list hit the {Cap}-page cap; result is partial", PlanPageCap);
        return new SubscriptionPlanList { Plans = plans, IsComplete = false };
    }

    // ---- subscribe -----------------------------------------------------------------------------------------

    public async Task<(SubscriptionDto Subscription, bool Created)> SubscribeAsync(string? userName, string? planHandle, CancellationToken ct)
    {
        var userKey = UserKey(userName);
        if (string.IsNullOrWhiteSpace(planHandle))
            throw new SubscriptionException(400, "planHandle is required.");

        using var budget = new CallBudget(ct, CallBudgetTotal);

        // Invariant: the handle must be one this application offers (the plan list of the configured family).
        var offered = await ListPlansAsync(budget);
        var plan = offered.Plans.FirstOrDefault(p => string.Equals(p.Handle, planHandle.Trim(), StringComparison.OrdinalIgnoreCase));
        if (plan == null)
        {
            throw offered.IsComplete
                ? new SubscriptionException(400, "Unknown plan. See GET /api/subscription-plans.")
                : new SubscriptionException(503, "Could not confirm the plan is offered; try again.");
        }

        var reference = SubscriptionReference(userKey, plan.Handle);

        // Claim first (store-enforced), then call Maxio, then record the result.
        var claim = new SubscriptionClaim { UserKey = userKey, PlanHandle = plan.Handle, Reference = reference };
        _db.SubscriptionClaims.Add(claim);
        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (Exception e) when (IsDuplicateKey(e))
        {
            _db.Entry(claim).State = EntityState.Detached;
            _logger.LogInformation("Subscription claim for plan {Plan} refused: already exists", plan.Handle);
            return (await ResolveExistingClaimAsync(userKey, plan.Handle, budget), false);
        }

        _logger.LogInformation("Subscription claim taken for plan {Plan}", plan.Handle);
        try
        {
            var customerId = await EnsureCustomerAsync(userKey, userName!, budget);
            claim.MaxioCustomerId = customerId;

            // Adopt a subscription that already carries our reference (e.g. the local store was reset since).
            var response = await CallAsync(budget, t => _client.Subscriptions.FindSubscription(
                new FindSubscriptionRequest { Reference = reference }, cancellationToken: t), notFoundIsNull: true);

            if (response == null)
            {
                try
                {
                    response = (await CallAsync(budget, t => _client.Subscriptions.CreateSubscription(
                        new CreateSubscriptionOperationRequest
                        {
                            Body = new CreateSubscriptionRequest
                            {
                                Subscription = new CreateSubscription
                                {
                                    ProductHandle = plan.Handle,
                                    CustomerId = customerId,
                                    PaymentCollectionMethod = MaxioAdvancedBilling.Models.Enums.CollectionMethod.Remittance,
                                    Reference = reference,
                                },
                            },
                        }, cancellationToken: t)))!;
                }
                catch (SubscriptionException ex) when (ex.OutcomeUnknown)
                {
                    // The write may have landed: settle by reference before reporting anything.
                    response = await TryFindAsync(reference);
                    if (response == null)
                    {
                        claim.Status = SubscriptionClaim.Unknown;
                        await _db.SaveChangesAsync(CancellationToken.None);
                        _logger.LogWarning("CreateSubscription outcome unknown for plan {Plan}; claim marked Unknown", plan.Handle);
                        throw new SubscriptionException(503,
                            "The billing system did not confirm the subscription. It will be reconciled; check GET /api/my-subscriptions.", ex);
                    }
                    _logger.LogInformation("CreateSubscription outcome settled by reference for plan {Plan}", plan.Handle);
                }
            }

            var sub = response.Subscription
                ?? throw new SubscriptionException(502, "The billing system returned an empty subscription.");
            claim.Status = SubscriptionClaim.Active;
            claim.MaxioSubscriptionId = sub.Id;
            await _db.SaveChangesAsync(CancellationToken.None);
            _logger.LogInformation("Subscribed to plan {Plan}: Maxio subscription {SubscriptionId}", plan.Handle, sub.Id);
            return (ToDto(sub, plan.Handle), true);
        }
        catch (SubscriptionException ex) when (claim.Status == SubscriptionClaim.Pending)
        {
            // Nothing reached Maxio (or it refused): release the claim so the shopper can retry.
            _logger.LogWarning("Releasing subscription claim for plan {Plan}: {Status} {Message}", plan.Handle, ex.StatusCode, ex.Message);
            _db.SubscriptionClaims.Remove(claim);
            await _db.SaveChangesAsync(CancellationToken.None);
            throw;
        }
    }

    private async Task<SubscriptionDto> ResolveExistingClaimAsync(string userKey, string planHandle, CallBudget budget)
    {
        var existing = await _db.SubscriptionClaims.FirstOrDefaultAsync(c => c.UserKey == userKey && c.PlanHandle == planHandle, budget.Caller)
            ?? throw new SubscriptionException(409, "A subscription request for this plan is in progress; try again.");

        if (existing.Status == SubscriptionClaim.Active && existing.MaxioSubscriptionId is int id)
        {
            var read = await CallAsync(budget, t => _client.Subscriptions.ReadSubscription(
                new ReadSubscriptionRequest { SubscriptionId = id }, cancellationToken: t));
            return ToDto(read!.Subscription ?? throw new SubscriptionException(502, "The billing system returned an empty subscription."), planHandle);
        }

        var inProgress = existing.Status == SubscriptionClaim.Pending && existing.CreatedUtc > DateTime.UtcNow - ClaimExpiry;
        if (inProgress)
            throw new SubscriptionException(409, "A subscription request for this plan is in progress; try again.");

        // Unknown, or a Pending claim that outlived its budget: settle by reference.
        var found = await CallAsync(budget, t => _client.Subscriptions.FindSubscription(
            new FindSubscriptionRequest { Reference = existing.Reference }, cancellationToken: t), notFoundIsNull: true);
        if (found?.Subscription is { } sub)
        {
            existing.Status = SubscriptionClaim.Active;
            existing.MaxioSubscriptionId = sub.Id;
            await _db.SaveChangesAsync(CancellationToken.None);
            return ToDto(sub, planHandle);
        }

        _db.SubscriptionClaims.Remove(existing);
        await _db.SaveChangesAsync(CancellationToken.None);
        throw new SubscriptionException(409, "The earlier attempt did not complete and was cleared; please subscribe again.");
    }

    private async Task<int> EnsureCustomerAsync(string userKey, string userName, CallBudget budget)
    {
        var link = new MaxioCustomerLink { UserKey = userKey };
        _db.MaxioCustomerLinks.Add(link);
        try
        {
            await _db.SaveChangesAsync(budget.Caller);
        }
        catch (Exception e) when (IsDuplicateKey(e))
        {
            _db.Entry(link).State = EntityState.Detached;
            var winner = await _db.MaxioCustomerLinks.FirstAsync(l => l.UserKey == userKey, budget.Caller);
            if (winner.MaxioCustomerId is int known)
                return known;

            var found = await ReadCustomerAsync(budget, CustomerReference(userKey));
            if (found is int foundId)
            {
                winner.MaxioCustomerId = foundId;
                await _db.SaveChangesAsync(CancellationToken.None);
                return foundId;
            }

            if (winner.CreatedUtc > DateTime.UtcNow - ClaimExpiry)
                throw new SubscriptionException(409, "Your billing profile is being created; try again.");
            link = winner; // stale claim from an attempt that never finished: take it over
        }

        try
        {
            var customerId = await ReadCustomerAsync(budget, CustomerReference(userKey));
            if (customerId == null)
            {
                var email = (await _users.FindByNameAsync(userName))?.Email;
                if (string.IsNullOrWhiteSpace(email))
                    throw new SubscriptionException(422, "Your account has no email address, which billing requires.");

                try
                {
                    var created = await CallAsync(budget, t => _client.Customers.CreateCustomer(
                        new CreateCustomerOperationRequest
                        {
                            Body = new CreateCustomerRequest
                            {
                                Customer = new CreateCustomer
                                {
                                    FirstName = email.Split('@')[0],
                                    LastName = "Shopper",
                                    Email = email,
                                    Reference = CustomerReference(userKey),
                                },
                            },
                        }, cancellationToken: t));
                    customerId = created!.Customer?.Id;
                }
                catch (SubscriptionException ex) when (ex.OutcomeUnknown)
                {
                    // The write may have landed: settle by the reference we sent.
                    customerId = await TryReadCustomerAsync(CustomerReference(userKey));
                    if (customerId == null) throw;
                }
            }

            if (customerId == null)
                throw new SubscriptionException(502, "The billing system returned no customer id.");
            link.MaxioCustomerId = customerId;
            await _db.SaveChangesAsync(CancellationToken.None);
            return customerId.Value;
        }
        catch (SubscriptionException ex) when (!ex.OutcomeUnknown)
        {
            _logger.LogWarning("Releasing customer link: {Status} {Message}", ex.StatusCode, ex.Message);
            _db.MaxioCustomerLinks.Remove(link);
            await _db.SaveChangesAsync(CancellationToken.None);
            throw;
        }
        // Unknown outcome: the link stays with a null id and is settled by reference on the next request.
    }

    private async Task<int?> ReadCustomerAsync(CallBudget budget, string reference)
    {
        var response = await CallAsync(budget, t => _client.Customers.ReadCustomerByReference(
            new ReadCustomerByReferenceRequest { Reference = reference }, cancellationToken: t), notFoundIsNull: true);
        return response?.Customer?.Id;
    }

    private async Task<int?> TryReadCustomerAsync(string reference)
    {
        try
        {
            using var settle = new CallBudget(CancellationToken.None, SettleBudgetTotal);
            return await ReadCustomerAsync(settle, reference);
        }
        catch (SubscriptionException) { return null; }
    }

    private async Task<SubscriptionResponse?> TryFindAsync(string reference)
    {
        try
        {
            using var settle = new CallBudget(CancellationToken.None, SettleBudgetTotal);
            return await CallAsync(settle, t => _client.Subscriptions.FindSubscription(
                new FindSubscriptionRequest { Reference = reference }, cancellationToken: t), notFoundIsNull: true);
        }
        catch (SubscriptionException) { return null; }
    }

    // ---- my subscriptions ----------------------------------------------------------------------------------

    public async Task<IReadOnlyList<SubscriptionDto>> ListMineAsync(string? userName, CancellationToken ct)
    {
        var userKey = UserKey(userName);
        using var budget = new CallBudget(ct, CallBudgetTotal);

        var claims = await _db.SubscriptionClaims.Where(c => c.UserKey == userKey).OrderBy(c => c.CreatedUtc).ToListAsync(ct);
        var result = new List<SubscriptionDto>();
        foreach (var claim in claims)
        {
            if (claim.Status == SubscriptionClaim.Pending && claim.CreatedUtc > DateTime.UtcNow - ClaimExpiry)
                continue; // still being created

            try
            {
                SubscriptionResponse? response;
                if (claim.MaxioSubscriptionId is int id)
                {
                    response = await CallAsync(budget, t => _client.Subscriptions.ReadSubscription(
                        new ReadSubscriptionRequest { SubscriptionId = id }, cancellationToken: t));
                }
                else
                {
                    response = await CallAsync(budget, t => _client.Subscriptions.FindSubscription(
                        new FindSubscriptionRequest { Reference = claim.Reference }, cancellationToken: t), notFoundIsNull: true);
                    if (response?.Subscription != null)
                    {
                        claim.Status = SubscriptionClaim.Active;
                        claim.MaxioSubscriptionId = response.Subscription.Id;
                        await _db.SaveChangesAsync(CancellationToken.None);
                    }
                }

                result.Add(response?.Subscription != null
                    ? ToDto(response.Subscription, claim.PlanHandle)
                    : new SubscriptionDto { PlanHandle = claim.PlanHandle, Reference = claim.Reference });
            }
            catch (SubscriptionException ex)
            {
                // Degrade for this row only: keep what we stored, state unknown.
                _logger.LogWarning("Could not refresh subscription for plan {Plan}: {Status} {Message}", claim.PlanHandle, ex.StatusCode, ex.Message);
                result.Add(new SubscriptionDto
                {
                    SubscriptionId = claim.MaxioSubscriptionId,
                    PlanHandle = claim.PlanHandle,
                    Reference = claim.Reference,
                    CustomerId = claim.MaxioCustomerId,
                });
            }
        }
        return result;
    }

    // ---- helpers -------------------------------------------------------------------------------------------

    private static string UserKey(string? userName)
    {
        if (string.IsNullOrWhiteSpace(userName))
            throw new SubscriptionException(401, "Caller identity is missing.");
        return userName.Trim().ToUpperInvariant();
    }

    private static string CustomerReference(string userKey) => $"eshop-user:{userKey}";
    private static string SubscriptionReference(string userKey, string planHandle) => $"sub:{userKey}:{planHandle}";

    private static bool IsDuplicateKey(Exception e) => e is DbUpdateException or ArgumentException; // ArgumentException: raised by the in-memory provider

    private static SubscriptionDto ToDto(Subscription sub, string fallbackPlanHandle) => new()
    {
        SubscriptionId = sub.Id,
        PlanHandle = sub.Product?.Handle ?? fallbackPlanHandle,
        PlanName = sub.Product?.Name,
        PriceInCents = sub.ProductPriceInCents,
        State = sub.State?.Value,
        NextBillingDate = sub.NextAssessmentAt ?? sub.CurrentPeriodEndsAt,
        Reference = sub.Reference,
        CustomerId = sub.Customer?.Id,
    };

    private sealed class CallBudget : IDisposable
    {
        private readonly CancellationTokenSource _cts;
        public CancellationToken Caller { get; }
        public CancellationToken Token => _cts.Token;

        public CallBudget(CancellationToken caller, TimeSpan total)
        {
            Caller = caller;
            _cts = CancellationTokenSource.CreateLinkedTokenSource(caller);
            _cts.CancelAfter(total);
        }

        public void Dispose() => _cts.Dispose();
    }

    /// <summary>Runs one SDK call under the budget and translates every SDK failure.</summary>
    private async Task<T?> CallAsync<T>(CallBudget budget, Func<CancellationToken, Task<T>> call, bool notFoundIsNull = false) where T : class
    {
        try
        {
            return await call(budget.Token);
        }
        catch (ApiException ex) when (notFoundIsNull && ex.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
        catch (ApiException ex)
        {
            throw Translate(ex);
        }
        catch (SdkException ex) // connection, timeout, auth
        {
            throw Translate(ex);
        }
        catch (OperationCanceledException ex) when (!budget.Caller.IsCancellationRequested)
        {
            throw new SubscriptionException(504, "The billing system did not answer in time.", ex, outcomeUnknown: true);
        }
    }

    private SubscriptionException Translate(Exception ex)
    {
        switch (ex)
        {
            case ApiException api:
                var status = (int)api.StatusCode;
                IReadOnlyList<string>? details = null;
                if (api is ApiException<CreateSubscriptionError> sub && sub.Error.TryGetErrorListResponse1(out var list))
                    details = list.Errors;

                _logger.LogWarning("Maxio {Method} answered {Status} ({ExceptionType})", api.Method, status, api.GetType().Name);
                // A body we could not read on a 2xx means the write may have happened.
                var unknown = status >= 500 || (api is ResponseDeserializationException && status is >= 200 and < 300);
                if (status == 422)
                    return new SubscriptionException(422, "The billing system rejected the request.", ex, details);
                // 401/403/429/404/other: our credentials, quota or request; not the shopper's to fix.
                return new SubscriptionException(status == 429 ? 503 : 502, "The billing system is unavailable.", ex, outcomeUnknown: unknown);
            case SdkTimeoutException:
                return new SubscriptionException(504, "The billing system did not answer in time.", ex, outcomeUnknown: true);
            case SdkConnectionException:
                return new SubscriptionException(503, "The billing system is unreachable.", ex, outcomeUnknown: true);
            default: // AuthSchemeException and anything else from the SDK
                _logger.LogError(ex, "Maxio credentials could not be applied");
                return new SubscriptionException(502, "The billing system is unavailable.", ex);
        }
    }
}
