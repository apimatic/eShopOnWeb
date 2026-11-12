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
using MaxioAdvancedBilling.Models.Enums;
using MaxioAdvancedBilling.Requests.Customers;
using MaxioAdvancedBilling.Requests.ProductFamilies;
using MaxioAdvancedBilling.Requests.Subscriptions;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.eShopWeb.Infrastructure.Identity;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

public class SubscriptionBillingService : ISubscriptionBillingService
{
    private const int PerPage = 200;          // the list operation's documented maximum
    private const int MaxPlanPages = 10;      // safety cap on the page walk; hitting it is reported as IsComplete = false
    private static readonly TimeSpan CallBudget = TimeSpan.FromSeconds(30);  // whole-operation budget
    private static readonly TimeSpan ClaimTtl = TimeSpan.FromMinutes(2);     // after this an unsettled claim may be taken over

    private readonly MaxioAdvancedBillingClient _client;
    private readonly MaxioSettings _settings;
    private readonly CatalogContext _db;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly ILogger<SubscriptionBillingService> _logger;

    public SubscriptionBillingService(
        MaxioAdvancedBillingClient client,
        IOptions<MaxioSettings> settings,
        CatalogContext db,
        UserManager<ApplicationUser> userManager,
        ILogger<SubscriptionBillingService> logger)
    {
        _client = client;
        _settings = settings.Value;
        _db = db;
        _userManager = userManager;
        _logger = logger;
    }

    // ---------------------------------------------------------------- plans

    public Task<PlanList> ListPlansAsync(CancellationToken ct) => Bounded(WalkPlansAsync, ct);

    private async Task<PlanList> WalkPlansAsync(CancellationToken ct)
    {
        var result = new PlanList { IsComplete = false };
        for (var page = 1; page <= MaxPlanPages; page++)
        {
            var currentPage = page;
            var items = await Call(() => _client.ProductFamilies.ListProductsForProductFamily(
                new ListProductsForProductFamilyRequest
                {
                    ProductFamilyId = "handle:" + _settings.ProductFamilyHandle,
                    Page = currentPage,
                    PerPage = PerPage,
                }, cancellationToken: ct));

            foreach (var item in items)
            {
                var p = item.Product;
                if (string.IsNullOrEmpty(p.Handle) || p.ArchivedAt is not null)
                {
                    continue;
                }
                result.Plans.Add(new PlanDto
                {
                    Handle = p.Handle,
                    Name = p.Name,
                    Description = p.Description,
                    PriceInCents = p.PriceInCents,
                    Interval = p.Interval,
                    IntervalUnit = p.IntervalUnit?.Value,
                });
            }

            if (items.Count < PerPage)
            {
                result.IsComplete = true;
                break;
            }
        }
        return result;
    }

    // ------------------------------------------------------------ subscribe

    public Task<SubscribeResult> SubscribeAsync(string userName, string planHandle, CancellationToken ct) =>
        Bounded(token => SubscribeCoreAsync(userName, planHandle, token), ct);

    private async Task<SubscribeResult> SubscribeCoreAsync(string userName, string planHandle, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(planHandle))
        {
            throw BillingException.BadRequest("planHandle is required.");
        }

        var user = await ResolveUserAsync(userName);

        // Invariant: only a plan the product family currently offers may be subscribed to.
        var offered = await WalkPlansAsync(ct);
        var plan = offered.Plans.FirstOrDefault(p => string.Equals(p.Handle, planHandle, StringComparison.Ordinal));
        if (plan is null)
        {
            if (!offered.IsComplete)
            {
                throw BillingException.Unavailable("The plan list could not be read completely; try again shortly.");
            }
            throw BillingException.BadRequest($"Plan '{planHandle}' is not offered.");
        }

        var customerReference = CustomerReferenceFor(user);
        await EnsureCustomerAsync(user, customerReference, ct);
        return await EnsureSubscriptionAsync(user.Id, customerReference, plan, ct);
    }

    private async Task<ApplicationUser> ResolveUserAsync(string userName)
    {
        var user = string.IsNullOrWhiteSpace(userName) ? null : await _userManager.FindByNameAsync(userName);
        if (user is null)
        {
            throw new BillingException(HttpStatusCode.Unauthorized, "The authenticated user does not exist.");
        }
        return user;
    }

    // The Identity user id is stable even if the user name or email changes.
    private static string CustomerReferenceFor(ApplicationUser user) => $"eshop-user-{user.Id}";

    private async Task EnsureCustomerAsync(ApplicationUser user, string reference, CancellationToken ct)
    {
        for (var attempt = 0; attempt < 3; attempt++)
        {
            // CLAIM: the primary key on UserId admits exactly one claimant before any Maxio call is made.
            var (link, won) = await ClaimCustomerAsync(user.Id, reference, ct);
            if (link.MaxioCustomerId.HasValue)
            {
                return;
            }

            // Either we hold the claim or an earlier claim never recorded its outcome: ask Maxio first.
            var existing = await FindCustomerIdAsync(reference, ct);
            if (existing.HasValue)
            {
                link.MaxioCustomerId = existing;
                await _db.SaveChangesAsync(ct);
                return;
            }

            if (won)
            {
                await CreateCustomerAsync(user, reference, link, ct);
                return;
            }

            if (DateTimeOffset.UtcNow - link.ClaimedAt > ClaimTtl)
            {
                // Stale claim whose outcome never landed and which Maxio does not know: release and re-claim.
                await ReleaseAsync(link);
                continue;
            }
            throw BillingException.Conflict("Your billing account is being set up; retry shortly.");
        }
        throw BillingException.Conflict("Your billing account is being set up; retry shortly.");
    }

    private async Task<(MaxioCustomerLink Link, bool Won)> ClaimCustomerAsync(string userId, string reference, CancellationToken ct)
    {
        for (var attempt = 0; attempt < 3; attempt++)
        {
            var claim = new MaxioCustomerLink { UserId = userId, CustomerReference = reference, ClaimedAt = DateTimeOffset.UtcNow };
            _db.MaxioCustomerLinks.Add(claim);
            try
            {
                await _db.SaveChangesAsync(ct);
                return (claim, true);
            }
            catch (Exception ex) when (IsDuplicateClaim(ex))   // the store refused the second claim
            {
                _db.Entry(claim).State = EntityState.Detached;
                var existing = await _db.MaxioCustomerLinks.FirstOrDefaultAsync(l => l.UserId == userId, ct);
                if (existing is not null)
                {
                    return (existing, false);
                }
            }
        }
        throw BillingException.Conflict("Your billing account is being set up; retry shortly.");
    }

    private async Task CreateCustomerAsync(ApplicationUser user, string reference, MaxioCustomerLink link, CancellationToken ct)
    {
        var email = user.Email ?? user.UserName ?? string.Empty;
        var local = email.Contains('@') ? email[..email.IndexOf('@')] : email;
        try
        {
            var response = await Call(() => _client.Customers.CreateCustomer(
                new CreateCustomerOperationRequest
                {
                    Body = new CreateCustomerRequest
                    {
                        Customer = new CreateCustomer
                        {
                            FirstName = string.IsNullOrWhiteSpace(local) ? "Shopper" : local,
                            LastName = "Customer",   // eShopOnWeb identities carry no name; Maxio requires one
                            Email = email,
                            Reference = reference,
                        },
                    },
                }, cancellationToken: ct));
            link.MaxioCustomerId = response.Customer.Id;
            await _db.SaveChangesAsync(ct);
        }
        catch (OutcomeUnknownException ex)
        {
            // The create may have landed: settle by reading back with the reference we sent.
            var found = await TryFindCustomerAfterUnknownAsync(reference, ex);
            if (!found.HasValue)
            {
                // Outcome still unknown: the claim stays (id null) and the next request's read-back settles it.
                throw;
            }
            link.MaxioCustomerId = found;
            await _db.SaveChangesAsync(CancellationToken.None);
        }
        catch (BillingException)
        {
            await ReleaseAsync(link);   // Maxio refused the create: free the claim
            throw;
        }
    }

    private async Task<int?> TryFindCustomerAfterUnknownAsync(string reference, Exception cause)
    {
        _logger.LogWarning(cause, "CreateCustomer outcome unknown for {Reference}; reading back.", reference);
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            return await FindCustomerIdAsync(reference, cts.Token);
        }
        catch (BillingException)
        {
            return null;
        }
    }

    private async Task<int?> FindCustomerIdAsync(string reference, CancellationToken ct)
    {
        var response = await Call<CustomerResponse?>(async () =>
        {
            try
            {
                return await _client.Customers.ReadCustomerByReference(
                    new ReadCustomerByReferenceRequest { Reference = reference }, cancellationToken: ct);
            }
            catch (ApiException<RawError> ex) when (ex.StatusCode == HttpStatusCode.NotFound)
            {
                return null;   // a genuine miss; an unreadable body is NOT absence (it falls through to Call)
            }
        });
        return response?.Customer.Id;
    }

    private async Task<SubscribeResult> EnsureSubscriptionAsync(
        string userId, string customerReference, PlanDto plan, CancellationToken ct)
    {
        for (var attempt = 0; attempt < 3; attempt++)
        {
            // CLAIM: the composite key (user, plan) admits exactly one claimant before CreateSubscription is called.
            var (row, won) = await ClaimSubscriptionAsync(userId, plan.Handle, ct);

            if (!won)
            {
                if (!row.MaxioSubscriptionId.HasValue)
                {
                    var found = await FindSubscriptionAsync(row.SubscriptionReference, ct);
                    if (found?.Id is not null)
                    {
                        Apply(row, found, plan);
                        await _db.SaveChangesAsync(ct);
                    }
                    else if (DateTimeOffset.UtcNow - row.ClaimedAt > ClaimTtl)
                    {
                        await ReleaseAsync(row);
                        continue;
                    }
                    else
                    {
                        throw BillingException.Conflict("This subscription is already being created; retry shortly.");
                    }
                }
                return new SubscribeResult(await RefreshAsync(row, ct), Created: false);
            }

            Subscription created;
            try
            {
                var response = await Call(() => CreateSubscriptionAsync(customerReference, plan.Handle, row.SubscriptionReference, ct));
                created = response.Subscription
                    ?? throw BillingException.BadGateway("The billing provider returned no subscription.");
            }
            catch (OutcomeUnknownException ex)
            {
                _logger.LogWarning(ex, "CreateSubscription outcome unknown for {Reference}; reading back.", row.SubscriptionReference);
                Subscription? found = null;
                try
                {
                    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                    found = await FindSubscriptionAsync(row.SubscriptionReference, cts.Token);
                }
                catch (BillingException)
                {
                    // read-back failed too: still unknown
                }
                if (found?.Id is null)
                {
                    // Claim stays, id null = "pending"; the next subscribe or my-subscriptions read settles it.
                    throw;
                }
                created = found;
            }
            catch (BillingException)
            {
                await ReleaseAsync(row);   // Maxio refused: free the claim
                throw;
            }

            Apply(row, created, plan);
            await _db.SaveChangesAsync(ct);
            _logger.LogInformation("Subscribed user {UserId} to {Plan} (Maxio subscription {SubscriptionId}).",
                userId, plan.Handle, row.MaxioSubscriptionId);
            return new SubscribeResult(ToDto(row), Created: true);
        }
        throw BillingException.Conflict("This subscription is already being created; retry shortly.");
    }

    private async Task<SubscriptionResponse> CreateSubscriptionAsync(
        string customerReference, string planHandle, string subscriptionReference, CancellationToken ct)
    {
        try
        {
            return await _client.Subscriptions.CreateSubscription(
                new CreateSubscriptionOperationRequest
                {
                    Body = new CreateSubscriptionRequest
                    {
                        Subscription = new CreateSubscription
                        {
                            ProductHandle = planHandle,
                            CustomerReference = customerReference,
                            Reference = subscriptionReference,
                            // Plans take no card: bill by remittance (invoice) instead of automatic card collection.
                            PaymentCollectionMethod = CollectionMethod.Remittance,
                        },
                    },
                }, cancellationToken: ct);
        }
        catch (ApiException<CreateSubscriptionError> ex) when (ex.Error.TryGetErrorListResponse1(out var errors))
        {
            // Provider validation (422): the caller can act on it.
            throw BillingException.BadRequest("Maxio rejected the subscription: " + string.Join("; ", errors.Errors), ex);
        }
    }

    private async Task<(UserSubscription Row, bool Won)> ClaimSubscriptionAsync(string userId, string planHandle, CancellationToken ct)
    {
        for (var attempt = 0; attempt < 3; attempt++)
        {
            var claim = new UserSubscription
            {
                UserId = userId,
                PlanHandle = planHandle,
                SubscriptionReference = $"eshop-sub-{Guid.NewGuid():N}",
                ClaimedAt = DateTimeOffset.UtcNow,
            };
            _db.UserSubscriptions.Add(claim);
            try
            {
                await _db.SaveChangesAsync(ct);
                return (claim, true);
            }
            catch (Exception ex) when (IsDuplicateClaim(ex))   // the store refused the second claim
            {
                _db.Entry(claim).State = EntityState.Detached;
                var existing = await _db.UserSubscriptions.FirstOrDefaultAsync(
                    s => s.UserId == userId && s.PlanHandle == planHandle, ct);
                if (existing is not null)
                {
                    return (existing, false);
                }
            }
        }
        throw BillingException.Conflict("This subscription is already being created; retry shortly.");
    }

    private async Task<Subscription?> FindSubscriptionAsync(string reference, CancellationToken ct)
    {
        var response = await Call<SubscriptionResponse?>(async () =>
        {
            try
            {
                return await _client.Subscriptions.FindSubscription(
                    new FindSubscriptionRequest { Reference = reference }, cancellationToken: ct);
            }
            catch (ApiException<FindSubscriptionError> ex) when (ex.Error.TryGetNoContent(out _))
            {
                return null;   // a genuine miss
            }
        });
        return response?.Subscription;
    }

    // ------------------------------------------------------ my-subscriptions

    public Task<IReadOnlyList<SubscriptionDto>> GetMySubscriptionsAsync(string userName, CancellationToken ct) =>
        Bounded(token => GetMineCoreAsync(userName, token), ct);

    private async Task<IReadOnlyList<SubscriptionDto>> GetMineCoreAsync(string userName, CancellationToken ct)
    {
        var user = await ResolveUserAsync(userName);
        var rows = await _db.UserSubscriptions.Where(s => s.UserId == user.Id).OrderBy(s => s.ClaimedAt).ToListAsync(ct);
        var result = new List<SubscriptionDto>(rows.Count);
        foreach (var row in rows)
        {
            result.Add(await RefreshAsync(row, ct));
        }
        return result;
    }

    /// <summary>Re-reads the row's subscription from Maxio; falls back to the stored copy if Maxio cannot answer.</summary>
    private async Task<SubscriptionDto> RefreshAsync(UserSubscription row, CancellationToken ct)
    {
        try
        {
            Subscription? live;
            if (row.MaxioSubscriptionId.HasValue)
            {
                var id = row.MaxioSubscriptionId.Value;
                live = (await Call(() => _client.Subscriptions.ReadSubscription(
                    new ReadSubscriptionRequest { SubscriptionId = id }, cancellationToken: ct))).Subscription;
            }
            else
            {
                // A claim whose create outcome was never recorded: settle it from Maxio.
                live = await FindSubscriptionAsync(row.SubscriptionReference, ct);
            }

            if (live is not null)
            {
                Apply(row, live, null);
                await _db.SaveChangesAsync(ct);
            }
        }
        catch (BillingException ex)
        {
            _logger.LogWarning(ex, "Could not refresh subscription for plan {Plan}; serving the stored copy.", row.PlanHandle);
            var stale = ToDto(row);
            stale.IsStale = true;
            return stale;
        }
        return ToDto(row);
    }

    // --------------------------------------------------------------- mapping

    private static void Apply(UserSubscription row, Subscription s, PlanDto? plan)
    {
        row.MaxioSubscriptionId = s.Id ?? row.MaxioSubscriptionId;
        row.State = s.State?.Value ?? row.State;
        row.PlanName = s.Product?.Name ?? plan?.Name ?? row.PlanName;
        row.PriceInCents = s.Product?.PriceInCents ?? s.ProductPriceInCents ?? plan?.PriceInCents ?? row.PriceInCents;
        row.Interval = s.Product?.Interval ?? plan?.Interval ?? row.Interval;
        row.IntervalUnit = s.Product?.IntervalUnit?.Value ?? plan?.IntervalUnit ?? row.IntervalUnit;
        row.NextBillingAt = s.NextAssessmentAt ?? s.CurrentPeriodEndsAt ?? row.NextBillingAt;
    }

    private static SubscriptionDto ToDto(UserSubscription row) => new()
    {
        SubscriptionId = row.MaxioSubscriptionId,
        PlanHandle = row.PlanHandle,
        PlanName = row.PlanName,
        PriceInCents = row.PriceInCents,
        Interval = row.Interval,
        IntervalUnit = row.IntervalUnit,
        State = row.MaxioSubscriptionId.HasValue ? row.State : "pending",
        NextBillingDate = row.NextBillingAt,
    };

    // ------------------------------------------------------------ plumbing

    // SQL Server surfaces a key violation as DbUpdateException; the in-memory provider throws ArgumentException.
    private static bool IsDuplicateClaim(Exception ex) => ex is DbUpdateException or ArgumentException;

    private async Task ReleaseAsync<T>(T claim) where T : class
    {
        try
        {
            _db.Remove(claim);
            await _db.SaveChangesAsync(CancellationToken.None);
        }
        catch (DbUpdateException ex)
        {
            _logger.LogWarning(ex, "Could not release a billing claim.");
            _db.Entry(claim).State = EntityState.Detached;
        }
    }

    /// <summary>Applies one total budget to a whole operation, whatever number of SDK calls it makes.</summary>
    private static async Task<T> Bounded<T>(Func<CancellationToken, Task<T>> operation, CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(CallBudget);
        try
        {
            return await operation(cts.Token);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw BillingException.Unavailable("The billing provider did not answer in time.");
        }
    }

    /// <summary>
    /// The one place SDK failures become <see cref="BillingException"/>s. A transport failure, or an unreadable 2xx
    /// body, is an <see cref="OutcomeUnknownException"/>: for a write the provider may already have acted.
    /// </summary>
    private async Task<T> Call<T>(Func<Task<T>> call)
    {
        try
        {
            return await call();
        }
        catch (ResponseDeserializationException ex)
        {
            var success = (int)ex.StatusCode is >= 200 and < 300;
            _logger.LogError(ex, "Maxio returned an unreadable body (HTTP {Status}).", (int)ex.StatusCode);
            throw success
                ? new OutcomeUnknownException("The billing provider's answer could not be read.", ex)
                : Map(ex.StatusCode, ex);
        }
        catch (ApiException ex)
        {
            _logger.LogWarning("Maxio call failed with HTTP {Status}.", (int)ex.StatusCode);
            throw Map(ex.StatusCode, ex);
        }
        catch (SdkConnectionException ex)   // includes SdkTimeoutException
        {
            _logger.LogWarning(ex, "Maxio call failed without a usable response.");
            throw new OutcomeUnknownException("The billing provider did not answer.", ex);
        }
        catch (AuthSchemeException ex)
        {
            _logger.LogError(ex, "Maxio credentials are missing or could not be applied.");
            throw BillingException.BadGateway("The billing provider is misconfigured.", ex);
        }
    }

    private static BillingException Map(HttpStatusCode status, Exception inner)
    {
        var code = (int)status;
        return code switch
        {
            401 or 403 => BillingException.BadGateway("The billing provider is unavailable.", inner),   // our credentials, not the caller's fault
            429 => BillingException.Unavailable("The billing provider is busy; try again shortly.", inner),
            >= 500 => BillingException.Unavailable("The billing provider is unavailable.", inner),
            >= 400 => BillingException.BadRequest("The billing provider rejected the request.", inner),
            _ => BillingException.BadGateway("Unexpected billing provider response.", inner),
        };
    }

    /// <summary>The write may or may not have reached the provider.</summary>
    private sealed class OutcomeUnknownException : BillingException
    {
        public OutcomeUnknownException(string message, Exception inner)
            : base(HttpStatusCode.ServiceUnavailable, message + " The result is being confirmed; retry shortly.", inner)
        {
        }
    }
}
