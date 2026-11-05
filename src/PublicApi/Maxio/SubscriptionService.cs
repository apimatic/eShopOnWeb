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
using MaxioAdvancedBilling.Requests.Customers;
using MaxioAdvancedBilling.Requests.ProductFamilies;
using MaxioAdvancedBilling.Requests.Subscriptions;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Entities.MaxioAggregate;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.eShopWeb.Infrastructure.Identity;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

/// <summary>
/// Subscribe flow against Maxio. Every write follows: claim (primary-key row in the app store) -> SDK call -> record the result.
/// </summary>
public class SubscriptionService
{
    private const int PER_PAGE = 200;
    private const int MAX_PAGES = 10;
    private static readonly TimeSpan CallBudget = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan SettleBudget = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan ClaimLease = TimeSpan.FromSeconds(30);

    private readonly MaxioAdvancedBillingClient _client;
    private readonly MaxioSettings _settings;
    private readonly CatalogContext _db;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly ILogger<SubscriptionService> _logger;

    public SubscriptionService(MaxioAdvancedBillingClient client, MaxioSettings settings, CatalogContext db,
        UserManager<ApplicationUser> userManager, ILogger<SubscriptionService> logger)
    {
        _client = client;
        _settings = settings;
        _db = db;
        _userManager = userManager;
        _logger = logger;
    }

    private string FamilyId => "handle:" + _settings.ProductFamilyHandle;

    private static string UserKeyOf(string userName) => userName.Trim().ToUpperInvariant();
    private static string SubscriptionReference(string userKey, string planHandle) => $"eshop:{userKey}:{planHandle}";

    // ---------------------------------------------------------------- plans

    public Task<PlanList> ListPlansAsync(CancellationToken ct) =>
        Bounded(token => ListPlansCoreAsync(token), ct);

    private async Task<PlanList> ListPlansCoreAsync(CancellationToken ct)
    {
        var plans = new List<PlanDto>();
        var truncated = false;
        try
        {
            for (var page = 1; page <= MAX_PAGES; page++)
            {
                var batch = await _client.ProductFamilies.ListProductsForProductFamily(
                    new ListProductsForProductFamilyRequest { ProductFamilyId = FamilyId, Page = page, PerPage = PER_PAGE },
                    cancellationToken: ct);

                foreach (var item in batch)
                {
                    var p = item.Product;
                    if (p is null || string.IsNullOrWhiteSpace(p.Handle) || p.ArchivedAt is not null)
                    {
                        continue;
                    }
                    plans.Add(new PlanDto
                    {
                        Handle = p.Handle,
                        Name = p.Name,
                        Description = p.Description,
                        PriceInCents = p.PriceInCents,
                        Price = p.PriceInCents / 100m,
                        Interval = p.Interval,
                        IntervalUnit = p.IntervalUnit?.Value
                    });
                }

                if (batch.Count < PER_PAGE)
                {
                    break;
                }
                if (page == MAX_PAGES)
                {
                    truncated = true;
                }
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException and not SubscriptionException)
        {
            throw Translate(ex, "list plans");
        }
        return new PlanList(plans, truncated);
    }

    // ---------------------------------------------------------------- subscribe

    public Task<(SubscriptionDto Subscription, bool Created)> SubscribeAsync(string userName, string? planHandle, CancellationToken ct) =>
        Bounded(token => SubscribeCoreAsync(userName, planHandle, token), ct);

    private async Task<(SubscriptionDto, bool)> SubscribeCoreAsync(string userName, string? planHandle, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(planHandle))
        {
            throw new SubscriptionException(400, "planHandle is required.");
        }

        // Invariant: only a plan this application offers (the family's plan list) can be subscribed to.
        var offered = await ListPlansCoreAsync(ct);
        var plan = offered.Plans.FirstOrDefault(p => string.Equals(p.Handle, planHandle, StringComparison.Ordinal));
        if (plan is null)
        {
            if (offered.Truncated)
            {
                throw new SubscriptionException(503, "The plan list is temporarily incomplete; try again later.");
            }
            throw new SubscriptionException(400, $"'{planHandle}' is not an available plan.");
        }

        var userKey = UserKeyOf(userName);
        var customerId = await EnsureCustomerAsync(userKey, userName, ct);
        var (subscription, created) = await EnsureSubscriptionAsync(userKey, customerId, plan.Handle, ct);

        _logger.LogInformation("Subscribe {Outcome}: user {UserKey} plan {Plan} maxio subscription {SubscriptionId}",
            created ? "created" : "existing", userKey, plan.Handle, subscription.Id);
        return (ToDto(subscription), created);
    }

    // ---------------------------------------------------------------- my subscriptions

    public Task<IReadOnlyList<SubscriptionDto>> ListMySubscriptionsAsync(string userName, CancellationToken ct) =>
        Bounded(token => ListMySubscriptionsCoreAsync(userName, token), ct);

    private async Task<IReadOnlyList<SubscriptionDto>> ListMySubscriptionsCoreAsync(string userName, CancellationToken ct)
    {
        var userKey = UserKeyOf(userName);
        var link = await _db.MaxioCustomerLinks.AsNoTracking().FirstOrDefaultAsync(l => l.UserKey == userKey, ct);
        int? customerId = link is { Status: MaxioLinkStatus.Created } ? link.MaxioCustomerId : null;
        if (customerId is null)
        {
            // No local mapping (new user, or the store was reset): the reference is the source of truth in Maxio.
            customerId = await FindCustomerIdAsync(userKey, ct);
            if (customerId is null)
            {
                return Array.Empty<SubscriptionDto>();
            }
            await RecordCustomerAsync(userKey, customerId.Value);
        }

        List<SubscriptionResponse> all;
        try
        {
            all = (await _client.Customers.ListCustomerSubscriptions(
                new ListCustomerSubscriptionsRequest { CustomerId = customerId.Value }, cancellationToken: ct)).ToList();
        }
        catch (Exception ex) when (ex is not OperationCanceledException and not SubscriptionException)
        {
            throw Translate(ex, "list subscriptions");
        }

        // Only subscriptions to plans of the configured family.
        var plans = await ListPlansCoreAsync(ct);
        var handles = plans.Plans.Select(p => p.Handle).ToHashSet(StringComparer.Ordinal);
        return all.Where(r => r.Subscription is not null)
            .Select(r => r.Subscription!)
            .Where(s => plans.Truncated || (s.Product?.Handle is not null && handles.Contains(s.Product.Handle)))
            .Select(ToDto)
            .ToList();
    }

    // ---------------------------------------------------------------- customer

    private async Task<int> EnsureCustomerAsync(string userKey, string userName, CancellationToken ct)
    {
        for (var attempt = 0; attempt < 3; attempt++)
        {
            var link = await _db.MaxioCustomerLinks.FirstOrDefaultAsync(l => l.UserKey == userKey, ct);
            if (link is not null)
            {
                if (link.Status == MaxioLinkStatus.Created && link.MaxioCustomerId is not null)
                {
                    return link.MaxioCustomerId.Value;
                }
                if (link.Status == MaxioLinkStatus.Claimed && DateTimeOffset.UtcNow - link.UpdatedAt < ClaimLease)
                {
                    throw new SubscriptionException(409, "Your account is already being set up; try again shortly.");
                }

                // Unknown (or an abandoned claim): settle it from Maxio before doing anything else.
                var existing = await FindCustomerIdAsync(userKey, ct);
                if (existing is not null)
                {
                    link.MaxioCustomerId = existing;
                    link.Status = MaxioLinkStatus.Created;
                    link.UpdatedAt = DateTimeOffset.UtcNow;
                    await _db.SaveChangesAsync(CancellationToken.None);
                    return existing.Value;
                }
                await ReleaseCustomerClaimAsync(userKey);
                continue;
            }

            if (!await TryClaimAsync(new MaxioCustomerLink
            {
                UserKey = userKey,
                Status = MaxioLinkStatus.Claimed,
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            }))
            {
                continue; // lost the race: re-read the winner's row
            }

            // Claim held. Adopt a customer that already exists in Maxio for this reference, else create one.
            int? found;
            try
            {
                found = await FindCustomerIdAsync(userKey, ct);
            }
            catch
            {
                await ReleaseCustomerClaimAsync(userKey); // nothing was written to Maxio
                throw;
            }

            int customerId;
            if (found is not null)
            {
                customerId = found.Value;
            }
            else
            {
                string first, last, email;
                try
                {
                    (first, last, email) = await DescribeUserAsync(userName);
                }
                catch
                {
                    await ReleaseCustomerClaimAsync(userKey);
                    throw;
                }
                customerId = await WriteAsync(
                    "create customer",
                    async () =>
                    {
                        var created = await _client.Customers.CreateCustomer(new CreateCustomerOperationRequest
                        {
                            Body = new CreateCustomerRequest
                            {
                                Customer = new CreateCustomer
                                {
                                    FirstName = first,
                                    LastName = last,
                                    Email = email,
                                    Reference = userKey
                                }
                            }
                        }, cancellationToken: ct);
                        return created.Customer?.Id ?? throw new SubscriptionException(502, "Billing system returned no customer id.");
                    },
                    async token =>
                    {
                        var id = await FindCustomerIdAsync(userKey, token);
                        return (id is not null, id.GetValueOrDefault());
                    },
                    () => ReleaseCustomerClaimAsync(userKey),
                    () => MarkCustomerUnknownAsync(userKey));
            }

            await RecordCustomerAsync(userKey, customerId);
            return customerId;
        }
        throw new SubscriptionException(409, "Your account is already being set up; try again shortly.");
    }

    private async Task<int?> FindCustomerIdAsync(string userKey, CancellationToken ct)
    {
        try
        {
            var response = await _client.Customers.ReadCustomerByReference(
                new ReadCustomerByReferenceRequest { Reference = userKey }, cancellationToken: ct);
            return response.Customer?.Id;
        }
        catch (ApiException<RawError> ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return null; // a definite "no such customer"; any other failure is NOT an absence
        }
        catch (Exception ex) when (ex is not OperationCanceledException and not SubscriptionException)
        {
            throw Translate(ex, "look up customer");
        }
    }

    private async Task RecordCustomerAsync(string userKey, int customerId)
    {
        var link = await _db.MaxioCustomerLinks.FirstOrDefaultAsync(l => l.UserKey == userKey);
        if (link is null)
        {
            await TryClaimAsync(new MaxioCustomerLink
            {
                UserKey = userKey,
                MaxioCustomerId = customerId,
                Status = MaxioLinkStatus.Created,
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            });
            return;
        }
        link.MaxioCustomerId = customerId;
        link.Status = MaxioLinkStatus.Created;
        link.UpdatedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(CancellationToken.None);
    }

    private Task ReleaseCustomerClaimAsync(string userKey) =>
        DeleteQuietlyAsync(() => _db.MaxioCustomerLinks.Where(l => l.UserKey == userKey && l.Status != MaxioLinkStatus.Created).ToListAsync());

    private async Task MarkCustomerUnknownAsync(string userKey)
    {
        var link = await _db.MaxioCustomerLinks.FirstOrDefaultAsync(l => l.UserKey == userKey);
        if (link is not null && link.Status != MaxioLinkStatus.Created)
        {
            link.Status = MaxioLinkStatus.Unknown;
            link.UpdatedAt = DateTimeOffset.UtcNow;
            await _db.SaveChangesAsync(CancellationToken.None);
        }
    }

    private async Task<(string First, string Last, string Email)> DescribeUserAsync(string userName)
    {
        // The JWT carries only the user name; eShopOnWeb user names are e-mail addresses.
        var user = await _userManager.FindByNameAsync(userName);
        var email = user?.Email;
        if (string.IsNullOrWhiteSpace(email))
        {
            email = userName.Contains('@') ? userName : throw new SubscriptionException(422, "Your account has no e-mail address; it is required to subscribe.");
        }
        var parts = email.Split('@')[0].Split(new[] { '.', '_', '-' }, StringSplitOptions.RemoveEmptyEntries);
        static string Cap(string s) => char.ToUpperInvariant(s[0]) + s[1..];
        var first = parts.Length > 0 ? Cap(parts[0]) : "Shopper";
        var last = parts.Length > 1 ? Cap(parts[^1]) : "Customer";
        return (first, last, email);
    }

    // ---------------------------------------------------------------- subscription

    private async Task<(Subscription, bool)> EnsureSubscriptionAsync(string userKey, int customerId, string planHandle, CancellationToken ct)
    {
        var reference = SubscriptionReference(userKey, planHandle);
        for (var attempt = 0; attempt < 3; attempt++)
        {
            var link = await _db.MaxioSubscriptionLinks.FirstOrDefaultAsync(l => l.UserKey == userKey && l.PlanHandle == planHandle, ct);
            if (link is not null)
            {
                if (link.Status == MaxioLinkStatus.Claimed && DateTimeOffset.UtcNow - link.UpdatedAt < ClaimLease)
                {
                    throw new SubscriptionException(409, "This subscription is already being created; try again shortly.");
                }

                var existing = await FindSubscriptionAsync(reference, ct);
                if (existing is not null)
                {
                    link.MaxioSubscriptionId = existing.Id;
                    link.Status = MaxioLinkStatus.Created;
                    link.UpdatedAt = DateTimeOffset.UtcNow;
                    await _db.SaveChangesAsync(CancellationToken.None);
                    return (existing, false);
                }
                if (link.Status == MaxioLinkStatus.Created)
                {
                    throw new SubscriptionException(502, "Your subscription could not be found in the billing system.");
                }
                await ReleaseSubscriptionClaimAsync(userKey, planHandle);
                continue;
            }

            if (!await TryClaimAsync(new MaxioSubscriptionLink
            {
                UserKey = userKey,
                PlanHandle = planHandle,
                Reference = reference,
                Status = MaxioLinkStatus.Claimed,
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            }))
            {
                continue;
            }

            Subscription? adopted;
            try
            {
                adopted = await FindSubscriptionAsync(reference, ct);
            }
            catch
            {
                await ReleaseSubscriptionClaimAsync(userKey, planHandle);
                throw;
            }

            var created = adopted is null;
            var subscription = adopted ?? await WriteAsync(
                "create subscription",
                async () =>
                {
                    var response = await _client.Subscriptions.CreateSubscription(new CreateSubscriptionOperationRequest
                    {
                        Body = new CreateSubscriptionRequest
                        {
                            Subscription = new CreateSubscription
                            {
                                ProductHandle = planHandle,
                                CustomerId = customerId,
                                Reference = reference,
                                // Plans need no payment method on file: bill by invoice instead of card capture.
                                PaymentCollectionMethod = MaxioAdvancedBilling.Models.Enums.CollectionMethod.Remittance
                            }
                        }
                    }, cancellationToken: ct);
                    return response.Subscription ?? throw new SubscriptionException(502, "Billing system returned no subscription.");
                },
                async token =>
                {
                    var s = await FindSubscriptionAsync(reference, token);
                    return (s is not null, s!);
                },
                () => ReleaseSubscriptionClaimAsync(userKey, planHandle),
                () => MarkSubscriptionUnknownAsync(userKey, planHandle));

            var row = await _db.MaxioSubscriptionLinks.FirstOrDefaultAsync(l => l.UserKey == userKey && l.PlanHandle == planHandle);
            if (row is not null)
            {
                row.MaxioSubscriptionId = subscription.Id;
                row.Status = MaxioLinkStatus.Created;
                row.UpdatedAt = DateTimeOffset.UtcNow;
                await _db.SaveChangesAsync(CancellationToken.None);
            }
            return (subscription, created);
        }
        throw new SubscriptionException(409, "This subscription is already being created; try again shortly.");
    }

    private async Task<Subscription?> FindSubscriptionAsync(string reference, CancellationToken ct)
    {
        try
        {
            var response = await _client.Subscriptions.FindSubscription(
                new FindSubscriptionRequest { Reference = reference }, cancellationToken: ct);
            return response.Subscription;
        }
        catch (ApiException<FindSubscriptionError> ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
        catch (Exception ex) when (ex is not OperationCanceledException and not SubscriptionException)
        {
            throw Translate(ex, "look up subscription");
        }
    }

    private Task ReleaseSubscriptionClaimAsync(string userKey, string planHandle) =>
        DeleteQuietlyAsync(() => _db.MaxioSubscriptionLinks
            .Where(l => l.UserKey == userKey && l.PlanHandle == planHandle && l.Status != MaxioLinkStatus.Created).ToListAsync());

    private async Task MarkSubscriptionUnknownAsync(string userKey, string planHandle)
    {
        var link = await _db.MaxioSubscriptionLinks.FirstOrDefaultAsync(l => l.UserKey == userKey && l.PlanHandle == planHandle);
        if (link is not null && link.Status != MaxioLinkStatus.Created)
        {
            link.Status = MaxioLinkStatus.Unknown;
            link.UpdatedAt = DateTimeOffset.UtcNow;
            await _db.SaveChangesAsync(CancellationToken.None);
        }
    }

    // ---------------------------------------------------------------- shared machinery

    /// <summary>The claim: insert a row whose primary key a second caller cannot also insert.</summary>
    private async Task<bool> TryClaimAsync<TLink>(TLink link) where TLink : class
    {
        try
        {
            _db.Add(link);
            await _db.SaveChangesAsync(CancellationToken.None);
            return true;
        }
        catch (Exception ex) when (ex is DbUpdateException or InvalidOperationException or ArgumentException)
        {
            _db.ChangeTracker.Clear();
            return false;
        }
    }

    private async Task DeleteQuietlyAsync<TLink>(Func<Task<List<TLink>>> load) where TLink : class
    {
        try
        {
            var rows = await load();
            _db.RemoveRange(rows);
            await _db.SaveChangesAsync(CancellationToken.None);
        }
        catch (DbUpdateException)
        {
            _db.ChangeTracker.Clear(); // somebody else already removed it
        }
    }

    /// <summary>
    /// Runs one Maxio write whose claim is already held. A definite refusal releases the claim; a failure after
    /// which Maxio may have acted (connection, timeout, unreadable 2xx, cancellation) is settled by re-reading
    /// Maxio by the reference that was sent, or recorded as Unknown when that re-read fails too.
    /// </summary>
    private async Task<T> WriteAsync<T>(string what, Func<Task<T>> write, Func<CancellationToken, Task<(bool Found, T Value)>> reread,
        Func<Task> release, Func<Task> markUnknown)
    {
        Exception unknown;
        try
        {
            return await write();
        }
        catch (ResponseDeserializationException ex) when ((int)ex.StatusCode is >= 200 and < 300)
        {
            unknown = ex;
        }
        catch (SdkConnectionException ex)
        {
            unknown = ex;
        }
        catch (OperationCanceledException ex)
        {
            unknown = ex;
        }
        catch (SubscriptionException)
        {
            await markUnknown(); // 2xx without the id we need: Maxio acted
            throw;
        }
        catch (Exception ex) when (ex is ApiException or AuthSchemeException)
        {
            await release(); // refused, or never sent
            throw Translate(ex, what);
        }

        _logger.LogWarning(unknown, "Outcome of '{What}' is unknown; re-reading from Maxio", what);
        bool found;
        T value;
        try
        {
            using var cts = new CancellationTokenSource(SettleBudget);
            (found, value) = await reread(cts.Token);
        }
        catch (Exception rex)
        {
            await markUnknown();
            _logger.LogWarning(rex, "Could not settle '{What}'; recorded as unknown", what);
            throw new SubscriptionException(502, "The billing system did not confirm the request; its outcome will be reconciled on your next attempt.", unknown);
        }

        if (found)
        {
            return value;
        }
        await release(); // Maxio definitively has nothing: the write did not land
        throw new SubscriptionException(502, "The billing system did not complete the request; please try again.", unknown);
    }

    private async Task<T> Bounded<T>(Func<CancellationToken, Task<T>> call, CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(CallBudget);
        try
        {
            return await call(cts.Token);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new SubscriptionException(504, "The billing system took too long to respond.");
        }
    }

    private SubscriptionException Translate(Exception ex, string what)
    {
        switch (ex)
        {
            case ResponseDeserializationException rde:
                _logger.LogError(ex, "Maxio '{What}' returned an unreadable body (HTTP {Status})", what, (int)rde.StatusCode);
                return new SubscriptionException(502, "The billing system returned a response that could not be processed.", ex);
            case ApiException<CreateSubscriptionError> cse when cse.Error.TryGetErrorListResponse1(out var list):
                var reasons = string.Join("; ", list.Errors);
                _logger.LogWarning(ex, "Maxio '{What}' rejected with HTTP {Status}: {Reasons}", what, (int)cse.StatusCode, reasons);
                return new SubscriptionException(422, "The billing system rejected the subscription: " + reasons, ex);
            case ApiException api:
                var status = (int)api.StatusCode;
                _logger.LogWarning(ex, "Maxio '{What}' failed with HTTP {Status}", what, status);
                return status switch
                {
                    422 => new SubscriptionException(422, "The billing system rejected the request.", ex),
                    429 => new SubscriptionException(503, "The billing system is temporarily unavailable.", ex),
                    _ => new SubscriptionException(502, "The billing system is unavailable.", ex)
                };
            case SdkTimeoutException:
                _logger.LogWarning(ex, "Maxio '{What}' timed out", what);
                return new SubscriptionException(504, "The billing system took too long to respond.", ex);
            case SdkConnectionException:
                _logger.LogWarning(ex, "Maxio '{What}' unreachable", what);
                return new SubscriptionException(502, "The billing system is unreachable.", ex);
            case AuthSchemeException:
                _logger.LogError(ex, "Maxio '{What}': credentials missing or rejected", what);
                return new SubscriptionException(502, "The billing system is unavailable.", ex);
            default:
                _logger.LogError(ex, "Unexpected failure in '{What}'", what);
                return new SubscriptionException(500, "Unexpected error.", ex);
        }
    }

    private static SubscriptionDto ToDto(Subscription s) => new()
    {
        Id = s.Id,
        Reference = s.Reference,
        PlanHandle = s.Product?.Handle,
        PlanName = s.Product?.Name,
        PriceInCents = s.Product?.PriceInCents,
        Price = s.Product?.PriceInCents / 100m,
        Interval = s.Product?.Interval,
        IntervalUnit = s.Product?.IntervalUnit?.Value,
        State = s.State?.Value,
        NextBillingAt = s.NextAssessmentAt
    };
}
