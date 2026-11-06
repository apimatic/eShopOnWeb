using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.Exceptions;
using MaxioAdvancedBilling.Errors;
using MaxioAdvancedBilling.Requests.Customers;
using MaxioAdvancedBilling.Requests.ProductFamilies;
using MaxioAdvancedBilling.Requests.Subscriptions;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.eShopWeb.Infrastructure.Identity;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Sdk = MaxioAdvancedBilling.Models;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Subscription capability backed by Maxio Advanced Billing (the system of record).
/// Writes follow: claim (local store, primary-key refusal) -> SDK call -> record the result.
/// </summary>
public class MaxioSubscriptionService : ISubscriptionService
{
    private const int PlanPageSize = 20;
    private const int PlanMaxPages = 25;
    /// <summary>Total budget for everything one request does against Maxio.</summary>
    private static readonly TimeSpan CallBudget = TimeSpan.FromSeconds(30);
    /// <summary>A claim left in "claiming" longer than this is treated as abandoned.</summary>
    private static readonly TimeSpan ClaimTtl = TimeSpan.FromMinutes(2);

    private readonly MaxioAdvancedBillingClient _client;
    private readonly MaxioSettings _settings;
    private readonly CatalogContext _db;
    private readonly UserManager<ApplicationUser> _users;
    private readonly ILogger<MaxioSubscriptionService> _logger;

    public MaxioSubscriptionService(
        MaxioAdvancedBillingClient client,
        IOptions<MaxioSettings> settings,
        CatalogContext db,
        UserManager<ApplicationUser> users,
        ILogger<MaxioSubscriptionService> logger)
    {
        _client = client;
        _settings = settings.Value;
        _db = db;
        _users = users;
        _logger = logger;
    }

    // ---------------------------------------------------------------- plans

    public async Task<PlanListResult> ListPlansAsync(CancellationToken cancellationToken)
    {
        using var cts = Budget(cancellationToken);
        try { return await ListPlansCoreAsync(cts.Token); }
        catch (ProviderException p) { throw Translate(p); }
    }

    private async Task<PlanListResult> ListPlansCoreAsync(CancellationToken ct)
    {
        var plans = new List<PlanInfo>();
        var truncated = false;

        for (var page = 1; page <= PlanMaxPages; page++)
        {
            var products = await Call(c => _client.ProductFamilies.ListProductsForProductFamily(
                new ListProductsForProductFamilyRequest
                {
                    ProductFamilyId = "handle:" + _settings.ProductFamilyHandle,
                    Page = page,
                    PerPage = PlanPageSize,
                }, cancellationToken: c), ct, "list plans");

            foreach (var item in products)
            {
                var p = item.Product;
                if (p.ArchivedAt != null || string.IsNullOrEmpty(p.Handle))
                    continue;
                var cents = p.PriceInCents ?? 0;
                plans.Add(new PlanInfo(p.Handle, p.Name ?? p.Handle, p.Description, cents, cents / 100m,
                    p.Interval, p.IntervalUnit?.Value));
            }

            if (products.Count < PlanPageSize)
                break;
            if (page == PlanMaxPages)
                truncated = true; // cap hit on a full page: the caller is told via PlanListResult.Truncated
        }

        return new PlanListResult(plans, truncated);
    }

    // ------------------------------------------------------------ subscribe

    public async Task<SubscriptionInfo> SubscribeAsync(string userName, string planHandle, CancellationToken cancellationToken)
    {
        try { return await SubscribeCoreAsync(userName, planHandle, cancellationToken); }
        catch (ProviderException p) { throw Translate(p); }
    }

    private async Task<SubscriptionInfo> SubscribeCoreAsync(string userName, string planHandle, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(userName))
            throw new SubscriptionException(401, "Caller identity is missing.");
        planHandle = (planHandle ?? string.Empty).Trim();
        if (planHandle.Length == 0)
            throw new SubscriptionException(400, "A plan handle is required.");

        using var cts = Budget(cancellationToken);
        var ct = cts.Token;

        // Invariant: the plan must be one this application offers (the configured family's list).
        var offered = await ListPlansCoreAsync(ct);
        var plan = offered.Plans.FirstOrDefault(p => string.Equals(p.Handle, planHandle, StringComparison.Ordinal));
        if (plan is null)
        {
            if (offered.Truncated)
                throw new SubscriptionException(503, "The plan catalog is too large to verify the requested plan right now.");
            throw new SubscriptionException(400, $"Plan '{planHandle}' is not offered.");
        }

        var customerId = await EnsureCustomerAsync(userName, ct);
        return await EnsureSubscriptionAsync(userName, plan, customerId, ct);
    }

    private async Task<int> EnsureCustomerAsync(string userName, CancellationToken ct)
    {
        // The local row is only a claim; the billing system is asked first, by the caller's own reference.
        var existing = await FindCustomerIdAsync(userName, ct);
        if (existing is not null)
            return existing.Value;

        var link = await _db.MaxioCustomerLinks.FirstOrDefaultAsync(l => l.UserName == userName, ct);
        if (link is null)
        {
            link = new MaxioCustomerLink { UserName = userName };
            _db.MaxioCustomerLinks.Add(link);
            try
            {
                await _db.SaveChangesAsync(ct); // claim: primary key refuses a second caller
            }
            catch (Exception ex) when (ex is DbUpdateException or InvalidOperationException or ArgumentException) // in-memory raises ArgumentException for a duplicate key
            {
                _db.ChangeTracker.Clear();
                _logger.LogWarning("Customer claim conflict for {User}", userName);
                // The winner may have finished already.
                var created = await FindCustomerIdAsync(userName, ct);
                if (created is not null)
                    return created.Value;
                throw new SubscriptionException(409, "Customer setup is already in progress; retry shortly.");
            }
        }
        else
        {
            if (link.Status == ClaimStatus.Claiming && DateTimeOffset.UtcNow - link.UpdatedAt < ClaimTtl)
                throw new SubscriptionException(409, "Customer setup is already in progress; retry shortly.");
            // Abandoned, unknown or stale (Maxio confirmed no such customer above): take it over.
            link.Status = ClaimStatus.Claiming;
            link.UpdatedAt = DateTimeOffset.UtcNow;
            await _db.SaveChangesAsync(ct);
        }

        var body = await BuildCustomerAsync(userName);
        try
        {
            var response = await CreateCustomerCall(body, ct);
            var id = response.Customer.Id ?? throw new ProviderException(null, "customer id missing", outcomeUnknown: true);
            link.MaxioCustomerId = id;
            link.Status = ClaimStatus.Completed;
            link.UpdatedAt = DateTimeOffset.UtcNow;
            await _db.SaveChangesAsync(ct);
            _logger.LogInformation("Created Maxio customer {CustomerId} for {User}", id, userName);
            return id;
        }
        catch (ProviderException p) when (!p.OutcomeUnknown)
        {
            await ReleaseAsync(link); // refused: release the claim
            throw Translate(p);
        }
        catch (ProviderException p)
        {
            // Unknown outcome: settle by re-reading the customer by the reference we sent.
            _logger.LogWarning(p, "Customer create outcome unknown for {User}; reconciling", userName);
            int? found = null;
            try { found = await FindCustomerIdAsync(userName, ct); }
            catch (ProviderException) { /* still unknown */ }

            if (found is not null)
            {
                link.MaxioCustomerId = found;
                link.Status = ClaimStatus.Completed;
                link.UpdatedAt = DateTimeOffset.UtcNow;
                await _db.SaveChangesAsync(CancellationToken.None);
                return found.Value;
            }

            link.Status = ClaimStatus.Unknown;
            link.UpdatedAt = DateTimeOffset.UtcNow;
            await _db.SaveChangesAsync(CancellationToken.None);
            throw Translate(p);
        }
    }

    private async Task<SubscriptionInfo> EnsureSubscriptionAsync(string userName, PlanInfo plan, int customerId, CancellationToken ct)
    {
        var reference = SubscriptionReference(userName, plan.Handle);
        var claim = await _db.SubscriptionClaims.FirstOrDefaultAsync(c => c.UserName == userName && c.PlanHandle == plan.Handle, ct);

        if (claim is null)
        {
            claim = new SubscriptionClaim { UserName = userName, PlanHandle = plan.Handle, Reference = reference };
            _db.SubscriptionClaims.Add(claim);
            try
            {
                await _db.SaveChangesAsync(ct); // claim: composite primary key refuses a second caller
            }
            catch (Exception ex) when (ex is DbUpdateException or InvalidOperationException or ArgumentException) // in-memory raises ArgumentException for a duplicate key
            {
                _db.ChangeTracker.Clear();
                _logger.LogWarning("Subscription claim conflict for {User}/{Plan}", userName, plan.Handle);
                var winner = await FindSubscriptionAsync(reference, ct);
                if (winner is not null)
                    return ToInfo(winner);
                throw new SubscriptionException(409, "A subscription to this plan is already being created; retry shortly.");
            }
        }
        else
        {
            var already = await FindSubscriptionAsync(reference, ct);
            if (already is not null)
            {
                await Complete(claim, already);
                return ToInfo(already);
            }
            if (claim.Status == ClaimStatus.Claiming && DateTimeOffset.UtcNow - claim.UpdatedAt < ClaimTtl)
                throw new SubscriptionException(409, "A subscription to this plan is already being created; retry shortly.");
            claim.Status = ClaimStatus.Claiming;
            claim.UpdatedAt = DateTimeOffset.UtcNow;
            await _db.SaveChangesAsync(ct);
        }

        // Covers a lost local store (restart): Maxio may already hold this subscription.
        var present = await FindSubscriptionAsync(reference, ct);
        if (present is not null)
        {
            await Complete(claim, present);
            return ToInfo(present);
        }

        try
        {
            var response = await CreateSubscriptionCall(customerId, plan.Handle, reference, ct);
            var sub = response.Subscription;
            if (sub?.Id is null)
                throw new ProviderException(null, "subscription missing from response", outcomeUnknown: true);
            await Complete(claim, sub);
            _logger.LogInformation("Created Maxio subscription {SubscriptionId} for {User} on {Plan}", sub.Id, userName, plan.Handle);
            return ToInfo(sub);
        }
        catch (ProviderException p) when (!p.OutcomeUnknown)
        {
            await ReleaseAsync(claim); // refused: release the claim
            throw Translate(p);
        }
        catch (ProviderException p)
        {
            _logger.LogWarning(p, "Subscription create outcome unknown for {User}/{Plan}; reconciling", userName, plan.Handle);
            Sdk.Subscription? found = null;
            try { found = await FindSubscriptionAsync(reference, ct); }
            catch (ProviderException) { /* still unknown */ }

            if (found is not null)
            {
                await Complete(claim, found);
                return ToInfo(found);
            }

            claim.Status = ClaimStatus.Unknown;
            claim.UpdatedAt = DateTimeOffset.UtcNow;
            await _db.SaveChangesAsync(CancellationToken.None);
            throw Translate(p);
        }
    }

    private async Task ReleaseAsync(object claim)
    {
        _db.Remove(claim);
        try { await _db.SaveChangesAsync(CancellationToken.None); }
        catch (DbUpdateException) { _db.ChangeTracker.Clear(); /* already released by a concurrent caller */ }
    }

    private async Task Complete(SubscriptionClaim claim, Sdk.Subscription sub)
    {
        claim.MaxioSubscriptionId = sub.Id;
        claim.Status = ClaimStatus.Completed;
        claim.UpdatedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(CancellationToken.None);
    }

    // ------------------------------------------------------- my subscriptions

    public async Task<SubscriptionListResult> ListSubscriptionsAsync(string userName, CancellationToken cancellationToken)
    {
        try { return await ListSubscriptionsCoreAsync(userName, cancellationToken); }
        catch (ProviderException p) { throw Translate(p); }
    }

    private async Task<SubscriptionListResult> ListSubscriptionsCoreAsync(string userName, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(userName))
            throw new SubscriptionException(401, "Caller identity is missing.");

        using var cts = Budget(cancellationToken);
        var ct = cts.Token;

        // Look the caller up at Maxio by their own reference; only a Maxio 404 means "no customer".
        var customerId = await FindCustomerIdAsync(userName, ct);
        if (customerId is null)
            return new SubscriptionListResult(Array.Empty<SubscriptionInfo>());

        var all = await Call(c => _client.Customers.ListCustomerSubscriptions(
            new ListCustomerSubscriptionsRequest { CustomerId = customerId.Value }, cancellationToken: c), ct, "list subscriptions");

        var result = all
            .Select(r => r.Subscription)
            .Where(s => s?.Id is not null)
            .Where(s => s!.Product?.ProductFamily?.Handle is not { } family
                        || string.Equals(family, _settings.ProductFamilyHandle, StringComparison.OrdinalIgnoreCase))
            .Select(s => ToInfo(s!))
            .ToList();
        return new SubscriptionListResult(result);
    }

    // ------------------------------------------------------------ SDK calls

    private async Task<int?> FindCustomerIdAsync(string reference, CancellationToken ct)
    {
        try
        {
            var response = await Call(c => _client.Customers.ReadCustomerByReference(
                new ReadCustomerByReferenceRequest { Reference = reference }, cancellationToken: c), ct, "read customer");
            return response.Customer.Id;
        }
        catch (ProviderException p) when (p.NotFound)
        {
            return null;
        }
    }

    private async Task<Sdk.Subscription?> FindSubscriptionAsync(string reference, CancellationToken ct)
    {
        try
        {
            var response = await Call(c => _client.Subscriptions.FindSubscription(
                new FindSubscriptionRequest { Reference = reference }, cancellationToken: c), ct, "find subscription");
            return response.Subscription;
        }
        catch (ProviderException p) when (p.NotFound)
        {
            return null;
        }
    }

    private async Task<Sdk.CustomerResponse> CreateCustomerCall(Sdk.CreateCustomerRequest body, CancellationToken ct)
    {
        try
        {
            return await Call(c => _client.Customers.CreateCustomer(
                new CreateCustomerOperationRequest { Body = body }, cancellationToken: c), ct, "create customer");
        }
        catch (ProviderException p) when (p.Status == HttpStatusCode.UnprocessableEntity)
        {
            throw new ProviderException(HttpStatusCode.UnprocessableEntity, "Maxio rejected the customer details.", false, p);
        }
    }

    private async Task<Sdk.SubscriptionResponse> CreateSubscriptionCall(int customerId, string planHandle, string reference, CancellationToken ct)
    {
        var body = new Sdk.CreateSubscriptionRequest
        {
            Subscription = new Sdk.CreateSubscription
            {
                ProductHandle = planHandle,
                CustomerId = customerId,
                Reference = reference,
                // No card capture in this flow: bill by remittance so no payment profile is needed.
                PaymentCollectionMethod = MaxioAdvancedBilling.Models.Enums.CollectionMethod.Remittance,
            },
        };
        return await Call(c => _client.Subscriptions.CreateSubscription(
            new CreateSubscriptionOperationRequest { Body = body }, cancellationToken: c), ct, "create subscription");
    }

    /// <summary>
    /// The one place SDK failures are translated. Typed bodies are read here (inside the per-operation
    /// catch) so the provider's error text is logged; callers only ever see <see cref="ProviderException"/>.
    /// </summary>
    private async Task<T> Call<T>(Func<CancellationToken, Task<T>> call, CancellationToken ct, string what)
    {
        try
        {
            return await call(ct);
        }
        catch (ApiException<CreateCustomerError> ex)
        {
            var detail = ex.Error.TryGetCustomerErrorResponse1(out var body) ? System.Text.Json.JsonSerializer.Serialize(body) : "(unrecognised body)";
            _logger.LogWarning("Maxio {What} refused ({Status}): {Detail}", what, (int)ex.StatusCode, detail);
            throw new ProviderException(ex.StatusCode, "Maxio rejected the request.", false, ex);
        }
        catch (ApiException<CreateSubscriptionError> ex)
        {
            if (ex.Error.TryGetErrorListResponse1(out var list))
                _logger.LogWarning("Maxio {What} refused ({Status}): {Errors}", what, (int)ex.StatusCode, string.Join("; ", list.Errors));
            else
                _logger.LogWarning("Maxio {What} refused ({Status})", what, (int)ex.StatusCode);
            throw new ProviderException(ex.StatusCode, "Maxio rejected the request.", false, ex);
        }
        catch (ResponseDeserializationException ex)
        {
            // 2xx => the outcome is unknown; otherwise the provider refused and only the detail is lost.
            var ok = (int)ex.StatusCode is >= 200 and < 300;
            _logger.LogWarning(ex, "Maxio {What} returned an unreadable body ({Status})", what, (int)ex.StatusCode);
            throw new ProviderException(ex.StatusCode, "Maxio returned an unreadable response.", ok, ex, unreadable: true);
        }
        catch (ApiException ex)
        {
            if (ex.StatusCode == HttpStatusCode.NotFound)
                _logger.LogDebug("Maxio {What}: not found", what);
            else
                _logger.LogWarning("Maxio {What} failed with HTTP {Status}", what, (int)ex.StatusCode);
            throw new ProviderException(ex.StatusCode, "Maxio request failed.", false, ex);
        }
        catch (SdkConnectionException ex) // includes SdkTimeoutException; a write's outcome is unknown
        {
            _logger.LogError(ex, "Maxio {What} could not be completed (connection/timeout)", what);
            throw new ProviderException(null, "Maxio did not answer.", true, ex);
        }
        catch (AuthSchemeException ex)
        {
            _logger.LogError(ex, "Maxio credentials are missing or could not be applied");
            throw new ProviderException(null, "Billing credentials are not usable.", false, ex);
        }
    }

    // --------------------------------------------------------------- helpers

    private CancellationTokenSource Budget(CancellationToken requestAborted)
    {
        var cts = CancellationTokenSource.CreateLinkedTokenSource(requestAborted);
        cts.CancelAfter(CallBudget);
        return cts;
    }

    private static string SubscriptionReference(string userName, string planHandle) => $"eshop:{userName}:{planHandle}";

    private async Task<Sdk.CreateCustomerRequest> BuildCustomerAsync(string userName)
    {
        var user = await _users.FindByNameAsync(userName);
        var email = user?.Email ?? (userName.Contains('@') ? userName : null);
        if (string.IsNullOrWhiteSpace(email))
            throw new SubscriptionException(422, "The account has no email address to bill.");

        var first = email.Split('@')[0];
        return new Sdk.CreateCustomerRequest
        {
            Customer = new Sdk.CreateCustomer
            {
                FirstName = first,
                LastName = "eShop",
                Email = email,
                Reference = userName,
            },
        };
    }

    private static SubscriptionInfo ToInfo(Sdk.Subscription s)
    {
        var cents = s.ProductPriceInCents ?? s.Product?.PriceInCents;
        return new SubscriptionInfo(
            s.Id ?? 0,
            s.Reference,
            s.Product?.Handle,
            s.Product?.Name,
            cents,
            cents / 100m,
            s.State?.Value,
            s.NextAssessmentAt ?? s.CurrentPeriodEndsAt,
            s.ActivatedAt);
    }

    /// <summary>Maps a provider failure to a caller-safe failure; 401/403/429 are our problem, not the caller's.</summary>
    private static SubscriptionException Translate(ProviderException p)
    {
        if (p.OutcomeUnknown)
            return new SubscriptionException(503, "The billing system did not confirm the request; check your subscriptions before retrying.", p);
        return (int?)p.Status switch
        {
            422 => new SubscriptionException(422, p.Message, p),
            401 or 403 => new SubscriptionException(502, "Billing system unavailable.", p),
            429 => new SubscriptionException(503, "Billing system is busy; retry shortly.", p),
            _ => new SubscriptionException(502, "Billing system request failed.", p),
        };
    }

    private sealed class ProviderException : Exception
    {
        public HttpStatusCode? Status { get; }
        public bool OutcomeUnknown { get; }
        public bool Unreadable { get; }
        public bool NotFound => Status == HttpStatusCode.NotFound && !Unreadable;

        public ProviderException(HttpStatusCode? status, string message, bool outcomeUnknown, Exception? inner = null, bool unreadable = false)
            : base(message, inner)
        {
            Status = status;
            OutcomeUnknown = outcomeUnknown;
            Unreadable = unreadable;
        }
    }
}
