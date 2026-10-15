using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Text;
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
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Subscription billing backed by Maxio Advanced Billing.
/// Every write follows: claim (insert, refused by the store on a duplicate) -> adopt-by-reference read ->
/// SDK call -> record result. A write whose outcome is unknown is settled by re-reading by reference.
/// </summary>
public class MaxioSubscriptionService : ISubscriptionService
{
    internal const int PER_PAGE = 200;
    internal const int MAX_PAGES = 25;
    internal static readonly TimeSpan SubscribeBudget = TimeSpan.FromSeconds(45);
    internal static readonly TimeSpan ReadBudget = TimeSpan.FromSeconds(30);
    internal static readonly TimeSpan ClaimLease = TimeSpan.FromMinutes(5);

    private readonly MaxioAdvancedBillingClient _client;
    private readonly MaxioSiteInfo _site;
    private readonly CatalogContext _db;
    private readonly MaxioSettings _settings;
    private readonly ILogger<MaxioSubscriptionService> _logger;
    private readonly TimeProvider _clock;

    public MaxioSubscriptionService(
        MaxioAdvancedBillingClient client,
        MaxioSiteInfo site,
        CatalogContext db,
        IOptions<MaxioSettings> settings,
        ILogger<MaxioSubscriptionService> logger)
    {
        _client = client;
        _site = site;
        _db = db;
        _settings = settings.Value;
        _logger = logger;
        _clock = TimeProvider.System;
    }

    // ---------------------------------------------------------------- plans

    public Task<PlanListResult> ListPlansAsync(CancellationToken cancellationToken = default)
        => Bounded(ReadBudget, cancellationToken, async ct =>
        {
            var (products, truncated) = await ReadProductsAsync(ct);
            return new PlanListResult(products.Select(ToPlan).ToList(), truncated);
        });

    // ------------------------------------------------------------ subscribe

    public Task<SubscribeResult> SubscribeAsync(string userName, string email, string planHandle, CancellationToken cancellationToken = default)
        => Bounded(SubscribeBudget, cancellationToken, async ct =>
        {
            var userKey = NormalizeUser(userName);
            if (userKey.Length == 0)
                throw new SubscriptionException(SubscriptionFailure.InvalidUser, "The caller has no usable identity.");
            if (string.IsNullOrWhiteSpace(email) || !email.Contains('@'))
                throw new SubscriptionException(SubscriptionFailure.InvalidUser, "The account has no email address to bill.");
            if (string.IsNullOrWhiteSpace(planHandle))
                throw new SubscriptionException(SubscriptionFailure.InvalidPlan, "A plan handle is required.");

            // Invariant: the plan must be one the configured family actually offers (fresh read, exact match).
            var (products, truncated) = await ReadProductsAsync(ct);
            var product = products.FirstOrDefault(p => string.Equals(p.Handle, planHandle, StringComparison.Ordinal));
            if (product is null)
            {
                if (truncated)
                    throw new SubscriptionException(SubscriptionFailure.Unavailable, "The plan list could not be read completely; try again later.");
                throw new SubscriptionException(SubscriptionFailure.InvalidPlan, $"'{planHandle}' is not an available plan.");
            }

            var customerReference = CustomerReference(userKey);
            var (customer, _) = await EnsureAsync(
                customerReference, MaxioClaim.KIND_CUSTOMER, null,
                find: FindCustomerAsync(customerReference),
                create: CreateCustomerAsync(customerReference, email),
                idOf: c => c.Id, ct);

            var subscriptionReference = SubscriptionReference(userKey, product.Handle!);
            var (subscription, created) = await EnsureAsync(
                subscriptionReference, MaxioClaim.KIND_SUBSCRIPTION, product.Handle,
                find: FindSubscriptionAsync(subscriptionReference),
                create: CreateSubscriptionAsync(subscriptionReference, product.Handle!, customer.Id, await _site.GetCollectionMethodAsync(ct)),
                idOf: s => s.Id, ct);

            _logger.LogInformation("Subscription {SubscriptionId} for customer {CustomerId} on plan {Plan} {Outcome}",
                subscription.Id, customer.Id, product.Handle, created ? "created" : "already existed");
            return new SubscribeResult(ToInfo(subscription, product), !created);
        });

    private Func<CancellationToken, Task<Customer?>> FindCustomerAsync(string reference) => async ct =>
    {
        try
        {
            var response = await _client.Customers.ReadCustomerByReference(
                new ReadCustomerByReferenceRequest { Reference = reference }, cancellationToken: ct);
            return response.Customer;
        }
        catch (ApiException<RawError> ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
    };

    private Func<CancellationToken, Task<Customer>> CreateCustomerAsync(string reference, string email) => async ct =>
    {
        try
        {
            var response = await _client.Customers.CreateCustomer(new CreateCustomerOperationRequest
            {
                Body = new CreateCustomerRequest
                {
                    Customer = new CreateCustomer
                    {
                        FirstName = FirstNameFrom(email),
                        LastName = "Shopper",
                        Email = email.Trim(),
                        Reference = reference
                    }
                }
            }, cancellationToken: ct);
            return response.Customer ?? throw new SubscriptionException(SubscriptionFailure.Unavailable, "Billing returned an empty customer.");
        }
        catch (ApiException<CreateCustomerError> ex) when (IsCallerFault(ex.StatusCode))
        {
            throw new MaxioRefusedException(ex.StatusCode, "The billing system rejected the customer details.", ex);
        }
    };

    private Func<CancellationToken, Task<Subscription?>> FindSubscriptionAsync(string reference) => async ct =>
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
    };

    private Func<CancellationToken, Task<Subscription>> CreateSubscriptionAsync(string reference, string planHandle, int? customerId, CollectionMethod collectionMethod) => async ct =>
    {
        try
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
                        // Plans do not require a card, so bill by invoice/remittance instead of auto-charging a payment profile.
                        PaymentCollectionMethod = collectionMethod
                    }
                }
            }, cancellationToken: ct);
            var subscription = response.Subscription
                ?? throw new SubscriptionException(SubscriptionFailure.Unavailable, "Billing returned an empty subscription.");
            if (subscription.State is not null && subscription.State.Is("failed_to_create"))
                throw new MaxioRefusedException(HttpStatusCode.UnprocessableEntity, "The billing system could not create the subscription.", null);
            return subscription;
        }
        catch (ApiException<CreateSubscriptionError> ex) when (IsCallerFault(ex.StatusCode))
        {
            var detail = ex.Error.TryGetErrorListResponse1(out var list) && list.Errors is { Count: > 0 }
                ? string.Join("; ", list.Errors)
                : "The billing system rejected the subscription.";
            throw new MaxioRefusedException(ex.StatusCode, detail, ex);
        }
    };

    // ------------------------------------------------------- my subscriptions

    public Task<MySubscriptionsResult> ListMySubscriptionsAsync(string userName, CancellationToken cancellationToken = default)
        => Bounded(ReadBudget, cancellationToken, async ct =>
        {
            var userKey = NormalizeUser(userName);
            if (userKey.Length == 0)
                throw new SubscriptionException(SubscriptionFailure.InvalidUser, "The caller has no usable identity.");

            var customer = await FindCustomerAsync(CustomerReference(userKey))(ct);
            if (customer?.Id is null)
                return new MySubscriptionsResult(Array.Empty<SubscriptionInfo>(), false);

            var result = new List<SubscriptionInfo>();
            var truncated = false;
            for (var page = 1; ; page++)
            {
                if (page > MAX_PAGES) { truncated = true; break; }
                var items = await _client.Subscriptions.ListSubscriptions(
                    new ListSubscriptionsRequest { CustomerId = customer.Id, Page = page, PerPage = PER_PAGE }, cancellationToken: ct);
                result.AddRange(items.Select(i => i.Subscription).OfType<Subscription>().Select(s => ToInfo(s, s.Product)));
                if (items.Count < PER_PAGE) break;
            }
            return new MySubscriptionsResult(result, truncated);
        });

    // ----------------------------------------------------- claim + settle core

    /// <summary>
    /// Runs one billing write exactly once per <paramref name="reference"/>. Returns the entity and whether this call created it.
    /// </summary>
    private async Task<(T Value, bool Created)> EnsureAsync<T>(
        string reference, string kind, string? planHandle,
        Func<CancellationToken, Task<T?>> find,
        Func<CancellationToken, Task<T>> create,
        Func<T, int?> idOf,
        CancellationToken ct) where T : class
    {
        if (!await TryClaimAsync(reference, kind, planHandle))
        {
            var row = await _db.MaxioClaims.AsNoTracking().FirstOrDefaultAsync(c => c.Reference == reference, ct);
            var existing = await find(ct);
            if (existing is not null)
            {
                if (row?.State != MaxioClaim.STATE_COMPLETED) await CompleteAsync(reference, idOf(existing));
                return (existing, false);
            }
            if (row is { State: MaxioClaim.STATE_COMPLETED })
                throw new SubscriptionException(SubscriptionFailure.Unavailable, "The billing record could not be read back; try again later.");
            if (row is { State: MaxioClaim.STATE_PENDING } && _clock.GetUtcNow() - row.ClaimedAt < ClaimLease)
                throw new SubscriptionException(SubscriptionFailure.InProgress, "A request for this subscription is already in progress.");

            // Stale or unknown claim, and Maxio has nothing for the reference: take the claim over.
            await ReleaseAsync(reference);
            if (!await TryClaimAsync(reference, kind, planHandle))
                throw new SubscriptionException(SubscriptionFailure.InProgress, "A request for this subscription is already in progress.");
        }

        // Adopt anything a lost local claim left behind at the provider before writing.
        T? adopted;
        try
        {
            adopted = await find(ct);
        }
        catch
        {
            await ReleaseAsync(reference);   // nothing was written yet
            throw;
        }
        if (adopted is not null)
        {
            await CompleteAsync(reference, idOf(adopted));
            return (adopted, false);
        }

        try
        {
            var created = await create(ct);
            await CompleteAsync(reference, idOf(created));
            return (created, true);
        }
        catch (MaxioRefusedException ex)
        {
            await ReleaseAsync(reference);
            _logger.LogWarning("Billing refused {Kind} write with HTTP {Status}", kind, (int)ex.StatusCode);
            throw new SubscriptionException(SubscriptionFailure.Rejected, ex.Message, ex);
        }
        catch (Exception ex) when (IsUnknownOutcome(ex))
        {
            // The write may have landed. Settle it here by re-reading with the reference we sent.
            _logger.LogWarning(ex, "Outcome of {Kind} write unknown; re-reading by reference", kind);
            T? found = null;
            try
            {
                using var settle = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                found = await find(settle.Token);
            }
            catch (Exception readEx) when (readEx is SdkException or OperationCanceledException)
            {
                _logger.LogWarning(readEx, "Re-read of {Kind} after unknown outcome failed", kind);
            }
            if (found is not null)
            {
                await CompleteAsync(reference, idOf(found));
                return (found, true);
            }
            await SetStateAsync(reference, MaxioClaim.STATE_UNKNOWN, null);
            throw new SubscriptionException(SubscriptionFailure.InProgress,
                "The billing system did not confirm the request yet; repeat it shortly to settle the outcome.", ex);
        }
        catch (SubscriptionException)
        {
            await ReleaseAsync(reference);   // provider answered but gave nothing usable
            throw;
        }
        catch (ApiException)
        {
            await ReleaseAsync(reference);   // definitive refusal that is not the caller's fault (401/403/429)
            throw;
        }
    }

    private static bool IsCallerFault(HttpStatusCode status) =>
        (int)status is >= 400 and < 500 && (int)status is not (401 or 403 or 408 or 429);

    private static bool IsUnknownOutcome(Exception ex) => ex switch
    {
        SdkConnectionException => true,                                         // includes SdkTimeoutException
        ResponseDeserializationException d => (int)d.StatusCode is >= 200 and < 300 || (int)d.StatusCode >= 500,
        ApiException a => (int)a.StatusCode >= 500 || a.StatusCode == HttpStatusCode.RequestTimeout,
        OperationCanceledException => true,                                     // budget expiry mid-write
        _ => false
    };

    private async Task<bool> TryClaimAsync(string reference, string kind, string? planHandle)
    {
        var claim = new MaxioClaim
        {
            Reference = reference,
            Kind = kind,
            PlanHandle = planHandle,
            State = MaxioClaim.STATE_PENDING,
            ClaimedAt = _clock.GetUtcNow()
        };
        _db.MaxioClaims.Add(claim);
        try
        {
            await _db.SaveChangesAsync();
            return true;
        }
        catch (Exception ex) when (ex is DbUpdateException or InvalidOperationException or ArgumentException)
        {
            // the primary key refused the second claim
            _db.Entry(claim).State = EntityState.Detached;
            return false;
        }
    }

    private async Task ReleaseAsync(string reference)
    {
        var row = await _db.MaxioClaims.FirstOrDefaultAsync(c => c.Reference == reference);
        if (row is null) return;
        _db.MaxioClaims.Remove(row);
        try { await _db.SaveChangesAsync(); }
        catch (DbUpdateException) { _db.Entry(row).State = EntityState.Detached; }   // someone else released it
    }

    private Task CompleteAsync(string reference, int? maxioId) => SetStateAsync(reference, MaxioClaim.STATE_COMPLETED, maxioId);

    private async Task SetStateAsync(string reference, string state, int? maxioId)
    {
        var row = await _db.MaxioClaims.FirstOrDefaultAsync(c => c.Reference == reference);
        if (row is null) return;
        row.State = state;
        if (maxioId.HasValue) row.MaxioId = maxioId;
        await _db.SaveChangesAsync();
    }

    // -------------------------------------------------------------- helpers

    private async Task<(List<Product> Products, bool Truncated)> ReadProductsAsync(CancellationToken ct)
    {
        var products = new List<Product>();
        for (var page = 1; ; page++)
        {
            if (page > MAX_PAGES) return (products, true);
            var items = await _client.ProductFamilies.ListProductsForProductFamily(new ListProductsForProductFamilyRequest
            {
                ProductFamilyId = "handle:" + _settings.ProductFamilyHandle,
                Page = page,
                PerPage = PER_PAGE
            }, cancellationToken: ct);
            products.AddRange(items.Select(i => i.Product).OfType<Product>()
                .Where(p => p.ArchivedAt is null && !string.IsNullOrEmpty(p.Handle)));
            if (items.Count < PER_PAGE) return (products, false);
        }
    }

    private static SubscriptionPlan ToPlan(Product p) => new(
        p.Handle!, p.Name ?? p.Handle!, p.Description, (p.PriceInCents ?? 0) / 100m,
        p.Interval, p.IntervalUnit?.Value,
        (p.TrialPriceInCents ?? 0) > 0 || (p.TrialInterval ?? 0) > 0,
        (p.InitialChargeInCents ?? 0) > 0);

    private static SubscriptionInfo ToInfo(Subscription s, Product? plan) => new(
        s.Id,
        plan?.Handle ?? s.Product?.Handle,
        plan?.Name ?? s.Product?.Name,
        (s.ProductPriceInCents ?? plan?.PriceInCents) is { } cents ? cents / 100m : null,
        s.State?.Value,
        s.NextAssessmentAt ?? s.CurrentPeriodEndsAt,
        s.CreatedAt);

    private async Task<T> Bounded<T>(TimeSpan budget, CancellationToken ct, Func<CancellationToken, Task<T>> call)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(budget);
        try
        {
            return await call(cts.Token);
        }
        catch (SubscriptionException)
        {
            throw;
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new SubscriptionException(SubscriptionFailure.Unavailable, "The billing system did not answer in time.");
        }
        catch (ApiException ex)
        {
            _logger.LogWarning(ex, "Billing call failed with HTTP {Status}", (int)ex.StatusCode);
            throw new SubscriptionException(SubscriptionFailure.Unavailable, "The billing system could not process the request.", ex);
        }
        catch (SdkException ex)
        {
            _logger.LogWarning(ex, "Billing call failed");
            throw new SubscriptionException(SubscriptionFailure.Unavailable, "The billing system is unavailable.", ex);
        }
    }

    internal static string NormalizeUser(string? userName) => (userName ?? string.Empty).Trim().ToLowerInvariant();

    internal static string CustomerReference(string userKey) => "eshop-cust-" + Hash(userKey);

    internal static string SubscriptionReference(string userKey, string planHandle) => "eshop-sub-" + Hash(userKey + "|" + planHandle);

    private static string Hash(string value)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private static string FirstNameFrom(string email)
    {
        var local = email.Trim().Split('@')[0];
        return string.IsNullOrWhiteSpace(local) ? "Shopper" : local;
    }

    /// <summary>The billing system definitively refused a write (no write happened).</summary>
    private sealed class MaxioRefusedException : Exception
    {
        public HttpStatusCode StatusCode { get; }
        public MaxioRefusedException(HttpStatusCode status, string message, Exception? inner) : base(message, inner) => StatusCode = status;
    }
}
