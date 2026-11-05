using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.Exceptions;
using MaxioAdvancedBilling.Core.ErrorResponse;
using MaxioAdvancedBilling.Errors;
using MaxioAdvancedBilling.Models;
using MaxioAdvancedBilling.Requests.Customers;
using MaxioAdvancedBilling.Requests.ProductFamilies;
using MaxioAdvancedBilling.Requests.Subscriptions;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.eShopWeb.Infrastructure.Identity;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Subscription billing on Maxio Advanced Billing.
/// Every write follows: claim (a row in our own store, refused by its primary key for a second caller)
/// -> SDK call -> record the result. An unknown outcome is settled by re-reading Maxio by our own reference.
/// </summary>
public class MaxioSubscriptionService : ISubscriptionService
{
    private const int PlanPageSize = 200;
    private const int MaxPlanPages = 5;
    private static readonly TimeSpan CallBudget = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan ReconcileBudget = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan StaleClaimAge = TimeSpan.FromMinutes(2);

    private readonly MaxioAdvancedBillingClient _client;
    private readonly CatalogContext _db;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly MaxioSettings _settings;
    private readonly ILogger<MaxioSubscriptionService> _logger;

    public MaxioSubscriptionService(
        MaxioAdvancedBillingClient client,
        CatalogContext db,
        UserManager<ApplicationUser> userManager,
        IOptions<MaxioSettings> settings,
        ILogger<MaxioSubscriptionService> logger)
    {
        _client = client;
        _db = db;
        _userManager = userManager;
        _settings = settings.Value;
        _logger = logger;
    }

    // ---------------------------------------------------------------- plans

    public async Task<SubscriptionPlanList> ListPlansAsync(CancellationToken cancellationToken)
    {
        using var budget = Budget(cancellationToken);
        try
        {
            return await ListPlansCoreAsync(budget.Token);
        }
        catch (Exception ex) when (IsSdkFailure(ex, cancellationToken))
        {
            throw Translate(ex);
        }
    }

    private async Task<SubscriptionPlanList> ListPlansCoreAsync(CancellationToken token)
    {
        var plans = new List<SubscriptionPlan>();
        var truncated = false;
        for (var page = 1; page <= MaxPlanPages; page++)
        {
            var products = await _client.ProductFamilies.ListProductsForProductFamily(
                new ListProductsForProductFamilyRequest
                {
                    ProductFamilyId = "handle:" + _settings.ProductFamilyHandle,
                    Page = page,
                    PerPage = PlanPageSize,
                },
                cancellationToken: token);

            foreach (var item in products)
            {
                var product = item.Product;
                if (product.Handle is null || product.ArchivedAt is not null)
                {
                    continue; // archived plans are not offered
                }
                plans.Add(new SubscriptionPlan(
                    product.Handle,
                    product.Name ?? product.Handle,
                    product.Description,
                    product.PriceInCents ?? 0,
                    product.Interval,
                    product.IntervalUnit?.Value));
            }

            if (products.Count < PlanPageSize)
            {
                break;
            }
            truncated = page == MaxPlanPages; // a full last page at the ceiling: there may be more
        }

        return new SubscriptionPlanList(plans, truncated);
    }

    // ---------------------------------------------------------------- subscribe

    public async Task<SubscribeResult> SubscribeAsync(string userName, string planHandle, CancellationToken cancellationToken)
    {
        using var budget = Budget(cancellationToken);
        var token = budget.Token;
        var key = NormalizeUser(userName);

        SubscriptionPlan plan;
        try
        {
            // The plan must be one this application offers (the configured family), not merely a product that exists.
            var offered = await ListPlansCoreAsync(token);
            var match = offered.Plans.FirstOrDefault(p => string.Equals(p.Handle, planHandle, StringComparison.Ordinal));
            if (match is null)
            {
                throw offered.IsTruncated
                    ? new SubscriptionException(SubscriptionFailure.Unconfirmed, "The plan could not be confirmed right now. Please try again later.")
                    : new SubscriptionException(SubscriptionFailure.InvalidPlan, $"Unknown subscription plan '{planHandle}'.");
            }
            plan = match;
        }
        catch (Exception ex) when (IsSdkFailure(ex, cancellationToken))
        {
            throw Translate(ex);
        }

        var reference = "eshop-sub-" + Hash(key + "|" + plan.Handle);

        // 1. claim (or find the earlier claim)
        MaxioSubscriptionLink? claim = null;
        for (var attempt = 0; attempt < 2 && claim is null; attempt++)
        {
            var candidate = new MaxioSubscriptionLink
            {
                UserName = key,
                PlanHandle = plan.Handle,
                Reference = reference,
                CreatedAt = DateTimeOffset.UtcNow,
            };
            if (await TryInsertAsync(candidate, token))
            {
                claim = candidate;
                break;
            }

            var existing = await _db.MaxioSubscriptionLinks.AsNoTracking()
                .FirstOrDefaultAsync(l => l.UserName == key && l.PlanHandle == plan.Handle, token);
            if (existing is null)
            {
                continue; // released in the meantime: claim again
            }

            Subscription? found;
            try
            {
                found = await FindSubscriptionAsync(existing.Reference, token);
            }
            catch (Exception ex) when (IsSdkFailure(ex, cancellationToken))
            {
                throw Translate(ex);
            }

            if (found?.Id is not null)
            {
                await RecordSubscriptionIdAsync(key, plan.Handle, found.Id.Value);
                return new SubscribeResult(ToSummary(found, plan), Created: false);
            }
            if (DateTimeOffset.UtcNow - existing.CreatedAt > StaleClaimAge)
            {
                // An old claim whose write never reached Maxio (confirmed by the re-read): release it and claim again.
                _logger.LogWarning("Releasing stale subscription claim {Reference}", reference);
                await ReleaseAsync<MaxioSubscriptionLink>(key, plan.Handle);
                continue;
            }
            throw new DuplicateException("A subscription to this plan is already being created. Please retry shortly.");
        }
        if (claim is null)
        {
            throw new SubscriptionException(SubscriptionFailure.Unavailable, "The subscription could not be started. Please try again.");
        }

        // 2. customer, 3. SDK call, 4. record
        var keepClaim = false;
        try
        {
            var customerId = await EnsureCustomerAsync(key, userName, token, cancellationToken);

            Subscription? created = null;
            Exception? failure = null;
            keepClaim = true; // from here Maxio may act: only a confirmed outcome releases the claim
            try
            {
                var response = await _client.Subscriptions.CreateSubscription(
                    new CreateSubscriptionOperationRequest
                    {
                        Body = new CreateSubscriptionRequest
                        {
                            Subscription = new CreateSubscription
                            {
                                ProductHandle = plan.Handle,
                                CustomerId = customerId,
                                Reference = reference,
                                // No payment profile is captured here (no card / 3-DS): bill by invoice. With the
                                // default (automatic) collection Maxio refuses a priced plan without a payment method.
                                PaymentCollectionMethod = MaxioAdvancedBilling.Models.Enums.CollectionMethod.Remittance,
                            },
                        },
                    },
                    cancellationToken: token);
                created = response.Subscription;
            }
            catch (ApiException<CreateSubscriptionError> ex) when (ex.StatusCode == HttpStatusCode.UnprocessableEntity)
            {
                keepClaim = false;
                var reasons = ex.Error.TryGetErrorListResponse1(out var errorList)
                    ? string.Join("; ", errorList.Errors)
                    : "unrecognised error body";
                _logger.LogWarning("Maxio rejected subscription {Reference} (HTTP 422): {Reasons}", reference, reasons);
                throw new SubscriptionException(SubscriptionFailure.Rejected, "The billing system rejected the subscription request.", ex);
            }
            catch (Exception ex) when (IsSdkFailure(ex, cancellationToken))
            {
                failure = ex;
            }

            if (created?.Id is null)
            {
                // Unknown outcome (or a 2xx without a usable subscription): settle it by our reference.
                try
                {
                    created = await FindSubscriptionAsync(reference, SettleToken(cancellationToken, out var settleCts));
                    settleCts.Dispose();
                }
                catch (Exception ex) when (IsSdkFailure(ex, cancellationToken))
                {
                    keepClaim = true; // still unknown: the claim stays, later requests settle it
                    _logger.LogError(ex, "Subscription {Reference} outcome unknown", reference);
                    throw Translate(failure ?? ex);
                }
                if (created?.Id is null)
                {
                    keepClaim = false; // Maxio confirmed there is no such subscription
                    throw failure is not null
                        ? Translate(failure)
                        : new SubscriptionException(SubscriptionFailure.Unavailable, "The billing system returned an unusable response.");
                }
                _logger.LogWarning("Subscription {Reference} settled after unknown outcome: found in Maxio", reference);
            }

            await RecordSubscriptionIdAsync(key, plan.Handle, created.Id.Value);
            _logger.LogInformation("Subscription {SubscriptionId} ({Reference}) created in state {State}", created.Id, reference, created.State?.Value);
            return new SubscribeResult(ToSummary(created, plan), Created: true);
        }
        finally
        {
            if (!keepClaim && !await IsRecordedAsync(key, plan.Handle))
            {
                await ReleaseAsync<MaxioSubscriptionLink>(key, plan.Handle);
            }
        }
    }

    private async Task<int> EnsureCustomerAsync(string key, string userName, CancellationToken token, CancellationToken callerToken)
    {
        var reference = "eshop-" + Hash(key);
        for (var attempt = 0; attempt < 2; attempt++)
        {
            var link = await _db.MaxioCustomerLinks.AsNoTracking().FirstOrDefaultAsync(l => l.UserName == key, token);
            if (link?.MaxioCustomerId is not null)
            {
                return link.MaxioCustomerId.Value;
            }

            if (link is not null)
            {
                // Someone claimed it before and never recorded an id: settle by reference.
                int? found;
                try
                {
                    found = await ReadCustomerIdAsync(reference, token);
                }
                catch (Exception ex) when (IsSdkFailure(ex, callerToken))
                {
                    throw Translate(ex);
                }
                if (found is not null)
                {
                    await RecordCustomerIdAsync(key, found.Value);
                    return found.Value;
                }
                if (DateTimeOffset.UtcNow - link.CreatedAt > StaleClaimAge)
                {
                    await ReleaseAsync<MaxioCustomerLink>(key);
                    continue;
                }
                throw new DuplicateException("Your billing profile is being created. Please retry shortly.");
            }

            var claim = new MaxioCustomerLink { UserName = key, CustomerReference = reference, CreatedAt = DateTimeOffset.UtcNow };
            if (!await TryInsertAsync(claim, token))
            {
                continue; // lost the race: reload and reuse / settle
            }

            return await CreateCustomerAsync(key, userName, reference, token, callerToken);
        }

        throw new DuplicateException("Your billing profile is being created. Please retry shortly.");
    }

    private async Task<int> CreateCustomerAsync(string key, string userName, string reference, CancellationToken token, CancellationToken callerToken)
    {
        var (email, first, last) = await ResolveIdentityAsync(userName);
        Exception? failure = null;
        int? id = null;
        try
        {
            var response = await _client.Customers.CreateCustomer(
                new CreateCustomerOperationRequest
                {
                    Body = new CreateCustomerRequest
                    {
                        Customer = new CreateCustomer { FirstName = first, LastName = last, Email = email, Reference = reference },
                    },
                },
                cancellationToken: token);
            id = response.Customer.Id;
        }
        catch (Exception ex) when (IsSdkFailure(ex, callerToken))
        {
            // 422 can mean the reference exists already (created earlier, link lost); anything else may be an
            // unknown outcome. Both are settled by the same re-read.
            failure = ex;
        }

        if (id is null)
        {
            try
            {
                id = await ReadCustomerIdAsync(reference, SettleToken(callerToken, out var settleCts));
                settleCts.Dispose();
            }
            catch (Exception ex) when (IsSdkFailure(ex, callerToken))
            {
                // Still unknown: keep the claim (null id); the next request settles it.
                _logger.LogError(ex, "Customer {Reference} outcome unknown", reference);
                throw Translate(failure ?? ex);
            }
            if (id is null)
            {
                await ReleaseAsync<MaxioCustomerLink>(key);
                if (failure is ApiException<CreateCustomerError> { StatusCode: HttpStatusCode.UnprocessableEntity })
                {
                    _logger.LogWarning("Maxio rejected customer {Reference} (HTTP 422)", reference);
                    throw new SubscriptionException(SubscriptionFailure.Rejected, "The billing system rejected the customer details.", failure);
                }
                throw failure is not null
                    ? Translate(failure)
                    : new SubscriptionException(SubscriptionFailure.Unavailable, "The billing system returned an unusable response.");
            }
        }

        await RecordCustomerIdAsync(key, id.Value);
        _logger.LogInformation("Maxio customer {CustomerId} ensured ({Reference})", id, reference);
        return id.Value;
    }

    private async Task<(string Email, string First, string Last)> ResolveIdentityAsync(string userName)
    {
        var user = await _userManager.FindByNameAsync(userName);
        var email = !string.IsNullOrWhiteSpace(user?.Email) ? user!.Email! : userName;
        var local = email.Split('@')[0];
        return (email, string.IsNullOrWhiteSpace(local) ? "eShop" : local, "Customer");
    }

    // ---------------------------------------------------------------- my subscriptions

    public async Task<IReadOnlyList<SubscriptionSummary>> GetSubscriptionsAsync(string userName, CancellationToken cancellationToken)
    {
        var key = NormalizeUser(userName);
        // The customer id comes only from our own link row.
        var link = await _db.MaxioCustomerLinks.AsNoTracking().FirstOrDefaultAsync(l => l.UserName == key, cancellationToken);
        if (link?.MaxioCustomerId is null)
        {
            return Array.Empty<SubscriptionSummary>();
        }

        using var budget = Budget(cancellationToken);
        try
        {
            var items = await _client.Customers.ListCustomerSubscriptions(
                new ListCustomerSubscriptionsRequest { CustomerId = link.MaxioCustomerId.Value },
                cancellationToken: budget.Token);

            return items
                .Select(i => i.Subscription)
                .Where(s => s?.Id is not null
                            && string.Equals(s.Product?.ProductFamily?.Handle, _settings.ProductFamilyHandle, StringComparison.Ordinal))
                .Select(s => ToSummary(s!, null))
                .ToList();
        }
        catch (Exception ex) when (IsSdkFailure(ex, cancellationToken))
        {
            throw Translate(ex);
        }
    }

    // ---------------------------------------------------------------- Maxio re-reads

    /// <summary>Null when Maxio has no such subscription (404).</summary>
    private async Task<Subscription?> FindSubscriptionAsync(string reference, CancellationToken token)
    {
        try
        {
            var response = await _client.Subscriptions.FindSubscription(new FindSubscriptionRequest { Reference = reference }, cancellationToken: token);
            return response.Subscription;
        }
        catch (ApiException<FindSubscriptionError> ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    /// <summary>Null when Maxio has no customer with that reference (404).</summary>
    private async Task<int?> ReadCustomerIdAsync(string reference, CancellationToken token)
    {
        try
        {
            var response = await _client.Customers.ReadCustomerByReference(new ReadCustomerByReferenceRequest { Reference = reference }, cancellationToken: token);
            return response.Customer.Id;
        }
        catch (ApiException<RawError> ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    // ---------------------------------------------------------------- claim store helpers

    private async Task<bool> TryInsertAsync<T>(T entity, CancellationToken token) where T : class
    {
        try
        {
            _db.Add(entity);
            await _db.SaveChangesAsync(token);
            return true;
        }
        catch (Exception ex) when (ex is DbUpdateException or InvalidOperationException or ArgumentException)
        {
            // The primary key refused a second claim (in-memory raises ArgumentException/InvalidOperationException
            // at Add or save, relational providers a DbUpdateException).
            _db.Entry(entity).State = EntityState.Detached;
            _logger.LogInformation("Claim refused for {Entity}: {Reason}", typeof(T).Name, ex.GetType().Name);
            return false;
        }
    }

    private async Task ReleaseAsync<T>(params object[] keyValues) where T : class
    {
        try
        {
            var row = await _db.Set<T>().FindAsync(keyValues);
            if (row is not null)
            {
                _db.Remove(row);
                await _db.SaveChangesAsync(CancellationToken.None);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not release {Entity} claim", typeof(T).Name);
            _db.ChangeTracker.Clear();
        }
    }

    private async Task<bool> IsRecordedAsync(string key, string planHandle)
    {
        var row = await _db.MaxioSubscriptionLinks.AsNoTracking()
            .FirstOrDefaultAsync(l => l.UserName == key && l.PlanHandle == planHandle);
        return row?.MaxioSubscriptionId is not null;
    }

    private async Task RecordSubscriptionIdAsync(string key, string planHandle, int id)
    {
        try
        {
            var row = await _db.MaxioSubscriptionLinks.FindAsync(key, planHandle);
            if (row is not null && row.MaxioSubscriptionId != id)
            {
                row.MaxioSubscriptionId = id;
                await _db.SaveChangesAsync(CancellationToken.None);
            }
        }
        catch (Exception ex)
        {
            // Maxio holds the subscription; the claim keeps its null id and is settled by reference next time.
            _logger.LogError(ex, "Could not record subscription id {SubscriptionId}", id);
        }
    }

    private async Task RecordCustomerIdAsync(string key, int id)
    {
        try
        {
            var row = await _db.MaxioCustomerLinks.FindAsync(key);
            if (row is not null && row.MaxioCustomerId != id)
            {
                row.MaxioCustomerId = id;
                await _db.SaveChangesAsync(CancellationToken.None);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Could not record customer id {CustomerId}", id);
        }
    }

    // ---------------------------------------------------------------- misc helpers

    private static CancellationTokenSource Budget(CancellationToken caller)
    {
        var cts = CancellationTokenSource.CreateLinkedTokenSource(caller);
        cts.CancelAfter(CallBudget);
        return cts;
    }

    /// <summary>A fresh short window for settling an unknown outcome, tied to the caller (not the spent budget).</summary>
    private static CancellationToken SettleToken(CancellationToken caller, out CancellationTokenSource cts)
    {
        cts = CancellationTokenSource.CreateLinkedTokenSource(caller);
        cts.CancelAfter(ReconcileBudget);
        return cts.Token;
    }

    /// <summary>An SDK failure, or our own budget expiring (the caller's cancellation is never one).</summary>
    private static bool IsSdkFailure(Exception ex, CancellationToken caller) =>
        ex is SdkException || (ex is OperationCanceledException && !caller.IsCancellationRequested);

    private SubscriptionException Translate(Exception ex)
    {
        switch (ex)
        {
            case SubscriptionException se:
                return se;
            case OperationCanceledException:
            case SdkTimeoutException:
                _logger.LogError(ex, "Maxio call timed out");
                return new SubscriptionException(SubscriptionFailure.Timeout, "The billing system did not answer in time.", ex);
            case ApiException<RawError> raw:
                _logger.LogError(ex, "Maxio returned HTTP {Status}: {Body}", (int)raw.StatusCode, Truncate(raw.Error.ReadAsString()));
                return new SubscriptionException(SubscriptionFailure.Unavailable, $"The billing system returned an error (HTTP {(int)raw.StatusCode}).", ex);
            case ApiException api:
                _logger.LogError(ex, "Maxio returned HTTP {Status}", (int)api.StatusCode);
                return new SubscriptionException(SubscriptionFailure.Unavailable, $"The billing system returned an error (HTTP {(int)api.StatusCode}).", ex);
            default:
                _logger.LogError(ex, "Maxio call failed");
                return new SubscriptionException(SubscriptionFailure.Unavailable, "The billing system is unavailable.", ex);
        }
    }

    private static string Truncate(string? s) => s is null ? "" : s.Length <= 500 ? s : s[..500];

    private static SubscriptionSummary ToSummary(Subscription s, SubscriptionPlan? plan) => new(
        s.Id!.Value,
        s.Product?.Handle ?? plan?.Handle ?? string.Empty,
        s.Product?.Name ?? plan?.Name ?? string.Empty,
        s.ProductPriceInCents ?? s.Product?.PriceInCents ?? plan?.PriceInCents ?? 0,
        s.State?.Value ?? "unknown",
        s.NextAssessmentAt ?? s.CurrentPeriodEndsAt);

    private static string NormalizeUser(string userName) => userName.Trim().ToUpperInvariant();

    private static string Hash(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
}
