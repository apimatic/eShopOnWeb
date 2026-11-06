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
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.eShopWeb.Infrastructure.Identity;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

/// <summary>
/// Subscription billing backed by Maxio Advanced Billing. Maxio is the system of record: the local
/// store only holds short-lived claims that stop a double submit reaching Maxio twice.
/// The caller's eShopOnWeb user name is the Maxio customer reference.
/// </summary>
public class SubscriptionService : ISubscriptionService
{
    private const int PlansPerPage = 200;
    private const int MaxPlanPages = 5;
    private static readonly TimeSpan CallBudget = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan StaleClaimAge = TimeSpan.FromMinutes(2);

    private readonly MaxioAdvancedBillingClient _client;
    private readonly MaxioSettings _settings;
    private readonly CatalogContext _db;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly ILogger<SubscriptionService> _logger;

    public SubscriptionService(MaxioAdvancedBillingClient client, IOptions<MaxioSettings> settings, CatalogContext db,
        UserManager<ApplicationUser> userManager, ILogger<SubscriptionService> logger)
    {
        _client = client;
        _settings = settings.Value;
        _db = db;
        _userManager = userManager;
        _logger = logger;
    }

    // ---- public operations ----

    public async Task<PlansResult> GetPlansAsync(CancellationToken cancellationToken)
    {
        using var cts = Budget(cancellationToken);
        try
        {
            return await ReadPlansAsync(cts.Token);
        }
        catch (ProviderException ex)
        {
            throw ToCallerError(ex);
        }
    }

    public async Task<SubscribeResult> SubscribeAsync(string userName, string planHandle, CancellationToken cancellationToken)
    {
        using var cts = Budget(cancellationToken);
        var ct = cts.Token;
        try
        {
            // Invariant: only plans this application offers (the configured family) may be subscribed to.
            var offered = await ReadPlansAsync(ct);
            if (!offered.Plans.Any(p => string.Equals(p.Handle, planHandle, StringComparison.Ordinal)))
            {
                throw offered.Truncated
                    ? new SubscriptionException(503, "The plan list could not be read completely; please retry.")
                    : new SubscriptionException(400, $"'{planHandle}' is not an available subscription plan.");
            }

            var customerId = await EnsureCustomerAsync(userName, ct);

            var claimKey = $"sub:{userName}:{planHandle}";
            if (!await TryClaimAsync(claimKey, ct))
            {
                // A concurrent request for the same user and plan is in flight: report what it produced.
                var existing = await FindLiveSubscriptionAsync(customerId, planHandle, ct);
                if (existing != null)
                {
                    return new SubscribeResult(existing, true);
                }
                throw new DuplicateException($"A subscription to '{planHandle}' is already being created for this user.");
            }

            try
            {
                // Durable guard that survives a restart (claims are local): never subscribe twice to the same live plan.
                var already = await FindLiveSubscriptionAsync(customerId, planHandle, ct);
                if (already != null)
                {
                    return new SubscribeResult(already, true);
                }

                try
                {
                    var response = await Call(() => _client.Subscriptions.CreateSubscription(
                        new CreateSubscriptionOperationRequest
                        {
                            Body = new CreateSubscriptionRequest
                            {
                                Subscription = new CreateSubscription
                                {
                                    ProductHandle = planHandle,
                                    CustomerId = customerId,
                                    // Plans need no card on file: bill by invoice instead of an automatic charge.
                                    PaymentCollectionMethod = CollectionMethod.Remittance,
                                }
                            }
                        }, cancellationToken: ct));
                    var created = response.Subscription
                        ?? throw new ProviderException(HttpStatusCode.OK, true, "Maxio returned no subscription.");
                    _logger.LogInformation("Created Maxio subscription {SubscriptionId} for plan {Plan}", created.Id, planHandle);
                    return new SubscribeResult(ToDto(created), false);
                }
                catch (ProviderException ex) when (ex.OutcomeUnknown)
                {
                    // The write may have landed: settle it by re-reading Maxio before answering.
                    _logger.LogWarning(ex, "Outcome of subscription create for plan {Plan} unknown; reconciling", planHandle);
                    SubscriptionDto? found = null;
                    try
                    {
                        found = await FindLiveSubscriptionAsync(customerId, planHandle, CancellationToken.None);
                    }
                    catch (ProviderException)
                    {
                        // Still unknown; answered below.
                    }
                    if (found != null)
                    {
                        return new SubscribeResult(found, false);
                    }
                    throw new SubscriptionException(503, "The subscription could not be confirmed. Check your subscriptions before retrying.", ex);
                }
            }
            finally
            {
                await ReleaseClaimAsync(claimKey);
            }
        }
        catch (ProviderException ex)
        {
            throw ToCallerError(ex);
        }
    }

    public async Task<IReadOnlyList<SubscriptionDto>> GetSubscriptionsAsync(string userName, CancellationToken cancellationToken)
    {
        using var cts = Budget(cancellationToken);
        try
        {
            // Always asked of Maxio by the user's own reference: a local row is never required.
            var customer = await FindCustomerIdAsync(userName, cts.Token);
            if (customer == null)
            {
                return Array.Empty<SubscriptionDto>();
            }
            return await ListSubscriptionsAsync(customer.Value, cts.Token);
        }
        catch (ProviderException ex)
        {
            throw ToCallerError(ex);
        }
    }

    // ---- Maxio reads ----

    private async Task<PlansResult> ReadPlansAsync(CancellationToken ct)
    {
        var plans = new List<PlanDto>();
        var truncated = false;
        for (var page = 1; page <= MaxPlanPages; page++)
        {
            var products = await Call(() => _client.ProductFamilies.ListProductsForProductFamily(
                new ListProductsForProductFamilyRequest
                {
                    ProductFamilyId = "handle:" + _settings.ProductFamilyHandle,
                    Page = page,
                    PerPage = PlansPerPage,
                }, cancellationToken: ct));

            plans.AddRange(products.Select(p => p.Product).Where(p => p.Handle != null).Select(ToPlan));
            if (products.Count < PlansPerPage)
            {
                break;
            }
            truncated = page == MaxPlanPages;
        }
        return new PlansResult(plans, truncated);
    }

    private async Task<int?> FindCustomerIdAsync(string reference, CancellationToken ct)
    {
        try
        {
            var response = await Call(() => _client.Customers.ReadCustomerByReference(
                new ReadCustomerByReferenceRequest { Reference = reference }, cancellationToken: ct));
            return response.Customer.Id;
        }
        catch (ProviderException ex) when (ex.ProviderStatus == HttpStatusCode.NotFound)
        {
            return null; // Maxio confirmed there is no such customer
        }
    }

    private async Task<IReadOnlyList<SubscriptionDto>> ListSubscriptionsAsync(int customerId, CancellationToken ct)
    {
        var subs = await Call(() => _client.Customers.ListCustomerSubscriptions(
            new ListCustomerSubscriptionsRequest { CustomerId = customerId }, cancellationToken: ct));
        return subs.Where(s => s.Subscription != null).Select(s => ToDto(s.Subscription!)).ToList();
    }

    private async Task<SubscriptionDto?> FindLiveSubscriptionAsync(int customerId, string planHandle, CancellationToken ct)
    {
        var subs = await ListSubscriptionsAsync(customerId, ct);
        return subs.FirstOrDefault(s => s.PlanHandle == planHandle && IsLive(s.State));
    }

    private static bool IsLive(string? state) =>
        state != SubscriptionState.Canceled.Value && state != SubscriptionState.Expired.Value
        && state != SubscriptionState.FailedToCreate.Value;

    // ---- customer ensure ----

    private async Task<int> EnsureCustomerAsync(string userName, CancellationToken ct)
    {
        var id = await FindCustomerIdAsync(userName, ct);
        if (id != null)
        {
            return id.Value;
        }

        var (email, first, last) = await ResolveIdentityAsync(userName);

        var claimKey = $"cust:{userName}";
        if (!await TryClaimAsync(claimKey, ct))
        {
            // Another request is creating this customer: wait for it to show up at Maxio.
            for (var attempt = 0; attempt < 6; attempt++)
            {
                await Task.Delay(500, ct);
                id = await FindCustomerIdAsync(userName, ct);
                if (id != null)
                {
                    return id.Value;
                }
            }
            throw new DuplicateException("The billing account for this user is already being created; please retry shortly.");
        }

        try
        {
            var response = await Call(() => _client.Customers.CreateCustomer(
                new CreateCustomerOperationRequest
                {
                    Body = new CreateCustomerRequest
                    {
                        Customer = new CreateCustomer
                        {
                            FirstName = first,
                            LastName = last,
                            Email = email,
                            Reference = userName,
                        }
                    }
                }, cancellationToken: ct));
            _logger.LogInformation("Created Maxio customer {CustomerId}", response.Customer.Id);
            return response.Customer.Id ?? throw new ProviderException(HttpStatusCode.OK, true, "Maxio returned no customer id.");
        }
        catch (ProviderException ex)
        {
            // Unknown outcome, or a rejection caused by a race: settle by reference before giving up.
            _logger.LogWarning(ex, "Customer create did not confirm; reconciling by reference");
            int? found = null;
            try
            {
                found = await FindCustomerIdAsync(userName, CancellationToken.None);
            }
            catch (ProviderException)
            {
                // Could not confirm either way; the original failure is reported.
            }
            if (found != null)
            {
                return found.Value;
            }
            throw;
        }
        finally
        {
            await ReleaseClaimAsync(claimKey);
        }
    }

    private async Task<(string Email, string First, string Last)> ResolveIdentityAsync(string userName)
    {
        var user = await _userManager.FindByNameAsync(userName);
        var email = !string.IsNullOrWhiteSpace(user?.Email) ? user!.Email! : (userName.Contains('@') ? userName : null);
        if (email == null)
        {
            throw new SubscriptionException(422, "This account has no email address, which billing requires.");
        }

        var tokens = email.Split('@')[0]
            .Split(new[] { '.', '_', '-', '+' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(t => char.ToUpperInvariant(t[0]) + t.Substring(1))
            .ToArray();
        var first = tokens.Length > 0 ? tokens[0] : "Customer";
        var last = tokens.Length > 1 ? tokens[^1] : first;
        return (email, first, last);
    }

    // ---- claims (store-enforced uniqueness; the primary key refuses the second claim) ----

    private async Task<bool> TryClaimAsync(string key, CancellationToken ct)
    {
        for (var attempt = 0; attempt < 2; attempt++)
        {
            var claim = new SubscriptionClaim(key);
            _db.SubscriptionClaims.Add(claim);
            try
            {
                await _db.SaveChangesAsync(ct);
                return true;
            }
            catch (Exception ex) when (IsDuplicateKey(ex))
            {
                _db.Entry(claim).State = EntityState.Detached;
                // A claim left behind by a crashed attempt must not block the user forever.
                var existing = await _db.SubscriptionClaims.AsNoTracking().FirstOrDefaultAsync(c => c.Key == key, ct);
                if (existing == null || DateTime.UtcNow - existing.CreatedAtUtc <= StaleClaimAge)
                {
                    return false;
                }
                await ReleaseClaimAsync(key);
            }
        }
        return false;
    }

    // SQL providers raise DbUpdateException; the in-memory provider raises ArgumentException for a repeated key.
    private static bool IsDuplicateKey(Exception ex) => ex is DbUpdateException or ArgumentException;

    private async Task ReleaseClaimAsync(string key)
    {
        try
        {
            var stub = new SubscriptionClaim(key);
            _db.Attach(stub).State = EntityState.Deleted;
            await _db.SaveChangesAsync(CancellationToken.None);
        }
        catch (Exception ex) when (ex is DbUpdateException or InvalidOperationException)
        {
            _logger.LogWarning(ex, "Could not release claim {ClaimKey}", key);
        }
    }

    // ---- SDK boundary: one catch ladder, SDK failures become ProviderException ----

    private static CancellationTokenSource Budget(CancellationToken ct)
    {
        var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(CallBudget);
        return cts;
    }

    private static async Task<T> Call<T>(Func<Task<T>> call)
    {
        try
        {
            return await call();
        }
        catch (ResponseDeserializationException ex)
        {
            // 2xx: the call may have succeeded but cannot be read (unknown). Non-2xx: rejected, detail lost.
            throw new ProviderException(ex.StatusCode, (int)ex.StatusCode is >= 200 and < 300, "Unreadable response from Maxio.", ex);
        }
        catch (ApiException ex)
        {
            throw new ProviderException(ex.StatusCode, false, Describe(ex), ex);
        }
        catch (SdkConnectionException ex) // includes SdkTimeoutException
        {
            throw new ProviderException(null, true, "Maxio did not answer.", ex);
        }
        catch (AuthSchemeException ex)
        {
            throw new ProviderException(HttpStatusCode.Unauthorized, false, "Maxio credentials are not usable.", ex);
        }
    }

    private static string Describe(ApiException ex)
    {
        if (ex is ApiException<CreateSubscriptionError> sub && sub.Error.TryGetErrorListResponse1(out var list))
        {
            return string.Join("; ", list.Errors);
        }
        return $"Maxio returned {(int)ex.StatusCode}.";
    }

    private SubscriptionException ToCallerError(ProviderException ex)
    {
        _logger.LogError(ex, "Maxio call failed (status {Status}, unknown outcome {Unknown})", (int?)ex.ProviderStatus, ex.OutcomeUnknown);
        return ex.ProviderStatus switch
        {
            // Our credentials or our quota, not the caller's problem.
            HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => new SubscriptionException(502, "The billing provider is unavailable.", ex),
            HttpStatusCode.TooManyRequests => new SubscriptionException(503, "The billing provider is busy; please retry.", ex),
            HttpStatusCode.UnprocessableEntity => new SubscriptionException(422, ex.Message, ex),
            null => new SubscriptionException(503, "The billing provider did not respond; please retry.", ex),
            _ => new SubscriptionException(502, "The billing provider returned an unexpected response.", ex),
        };
    }

    // ---- mapping ----

    private static PlanDto ToPlan(Product p) => new()
    {
        Handle = p.Handle,
        Name = p.Name,
        Description = p.Description,
        PriceInCents = p.PriceInCents,
        Price = p.PriceInCents / 100m,
        Interval = p.Interval,
        IntervalUnit = p.IntervalUnit?.Value,
    };

    private static SubscriptionDto ToDto(Subscription s) => new()
    {
        Id = s.Id,
        PlanHandle = s.Product?.Handle,
        PlanName = s.Product?.Name,
        PriceInCents = s.ProductPriceInCents,
        Price = s.ProductPriceInCents / 100m,
        State = s.State?.Value,
        NextBillingDate = s.NextAssessmentAt,
        CurrentPeriodEndsAt = s.CurrentPeriodEndsAt,
        CreatedAt = s.CreatedAt,
    };

    private sealed class ProviderException : Exception
    {
        public HttpStatusCode? ProviderStatus { get; }
        public bool OutcomeUnknown { get; }

        public ProviderException(HttpStatusCode? status, bool outcomeUnknown, string message, Exception? inner = null)
            : base(message, inner)
        {
            ProviderStatus = status;
            OutcomeUnknown = outcomeUnknown;
        }
    }
}
