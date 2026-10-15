using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Hosting;

namespace Microsoft.eShopWeb.Infrastructure.Billing;

public sealed class SubscriptionBillingService
    : ISubscriptionBilling
{
    private readonly MaxioAdvancedBilling.MaxioAdvancedBillingClient _client;
    private readonly CatalogContext _db;
    private readonly ILogger<SubscriptionBillingService> _logger;
    private readonly MaxioSettings _settings;
    private readonly string _site;
    private readonly IHostEnvironment? _environment;

    public SubscriptionBillingService(MaxioAdvancedBilling.MaxioAdvancedBillingClient client, CatalogContext db,
        MaxioSettings settings, ILogger<SubscriptionBillingService> logger, IHostEnvironment? environment = null)
    {
        _client = client; _db = db; _settings = settings; _logger = logger;
        _environment = environment;
        _site = Hash(settings.BaseUrl ?? $"https://{settings.Subdomain}.chargify.com");
    }

    public Task<IReadOnlyList<SubscriptionPlan>> GetPlansAsync(CancellationToken cancellationToken) =>
        Bounded(async ct =>
        {
            var site = await ReadSite(ct);
            return (IReadOnlyList<SubscriptionPlan>)(await ReadPlans(ct)).Select(p => ToPlan(p) with { Currency = site.Currency }).ToList();
        }, cancellationToken);

    private async Task<MaxioAdvancedBilling.Models.Site> ReadSite(CancellationToken ct)
    {
        var site = (await _client.Sites.ReadSite(cancellationToken: ct))?.Site ?? throw InvalidResponse();
        if (site.RelationshipInvoicingEnabled == null || string.IsNullOrWhiteSpace(site.Currency)) throw InvalidResponse();
        if (_environment?.IsDevelopment() == true && site.Test != true)
            throw new BillingException(502, "Development billing requires a Maxio test site.");
        return site;
    }

    private async Task<List<MaxioAdvancedBilling.Models.Product>> ReadPlans(CancellationToken ct)
    {
        var products = new List<MaxioAdvancedBilling.Models.Product>();
        for (var page = 1; page <= 100; page++)
        {
            var response = await _client.ProductFamilies.ListProductsForProductFamily(
                new MaxioAdvancedBilling.Requests.ProductFamilies.ListProductsForProductFamilyRequest
                { ProductFamilyId = "handle:" + _settings.ProductFamilyHandle, Page = page, PerPage = 100, IncludeArchived = false }, cancellationToken: ct);
            if (response == null) throw InvalidResponse();
            foreach (var envelope in response)
            {
                var p = envelope?.Product ?? throw InvalidResponse();
                if (p.ArchivedAt != null) continue;
                // This flow has no payment capture. Offer only plans that explicitly allow it.
                if (p.RequireCreditCard != false) continue;
                if (p.ProductFamily?.Handle != _settings.ProductFamilyHandle) throw InvalidResponse();
                _ = ToPlan(p);
                products.Add(p);
            }
            if (response.Count < 100) return products;
        }
        throw new BillingException(503, "The plan catalog is too large to load completely. Enrollment is temporarily unavailable.");
    }

    public Task<SubscriptionEnrollment> SubscribeAsync(string userId, string email, string productHandle, CancellationToken cancellationToken) =>
        Bounded(async ct =>
        {
            if (string.IsNullOrWhiteSpace(productHandle) || productHandle.Length > 255)
                throw new BillingException(400, "A productHandle from subscription-plans is required.");
            var site = await ReadSite(ct);
            if (!(await ReadPlans(ct)).Any(p => p.Handle == productHandle))
                throw new BillingException(400, "Choose an available productHandle from subscription-plans.");

            var customer = await EnsureCustomer(userId, email, ct);
            if (customer == null) return new SubscriptionEnrollment("pending", null);
            var (claim, owned) = await TryClaim(userId, "Subscription", productHandle, ct);
            if (!owned)
            {
                var prior = await LookupSubscription(claim.Reference, ct);
                if (prior == null) return new SubscriptionEnrollment("pending", null);
                ValidateSubscription(prior, customer.Value, productHandle, claim.Reference);
                await Complete(claim, prior.Id!.Value);
                return new SubscriptionEnrollment("complete", ToDetails(prior));
            }
            // Lookup also recovers completed writes after loss of the local development store.
            var existing = await BeforeWriteLookup(claim, () => LookupSubscription(claim.Reference, ct));
            if (existing != null)
            {
                ValidateSubscription(existing, customer.Value, productHandle, claim.Reference);
                await Complete(claim, existing.Id!.Value);
                return new SubscriptionEnrollment("complete", ToDetails(existing));
            }
            var subscription = await CreateAndReconcile(claim, async token =>
            {
                var result = await _client.Subscriptions.CreateSubscription(
                    new MaxioAdvancedBilling.Requests.Subscriptions.CreateSubscriptionOperationRequest
                    {
                        Body = new MaxioAdvancedBilling.Models.CreateSubscriptionRequest
                        {
                            Subscription = new MaxioAdvancedBilling.Models.CreateSubscription
                            {
                                CustomerId = customer, ProductHandle = productHandle, Reference = claim.Reference,
                                PaymentCollectionMethod = site.RelationshipInvoicingEnabled == true
                                    ? MaxioAdvancedBilling.Models.Enums.CollectionMethod.Remittance
                                    : MaxioAdvancedBilling.Models.Enums.CollectionMethod.Invoice
                            }
                        }
                    }, cancellationToken: token);
                var s = result?.Subscription ?? throw InvalidResponse();
                ValidateSubscription(s, customer.Value, productHandle, claim.Reference);
                _ = ToDetails(s);
                return s;
            }, async token =>
            {
                var found = await LookupSubscription(claim.Reference, token);
                if (found != null)
                {
                    ValidateSubscription(found, customer.Value, productHandle, claim.Reference);
                    _ = ToDetails(found);
                }
                return found;
            }, s => s.Id, ct);
            if (subscription == null) return new SubscriptionEnrollment("pending", null);
            ValidateSubscription(subscription, customer.Value, productHandle, claim.Reference);
            return new SubscriptionEnrollment("complete", ToDetails(subscription));
        }, cancellationToken);

    private async Task<int?> EnsureCustomer(string userId, string email, CancellationToken ct)
    {
        var (claim, owned) = await TryClaim(userId, "Customer", null, ct);
        var existing = owned ? await BeforeWriteLookup(claim, () => LookupCustomer(claim.Reference, ct)) : await LookupCustomer(claim.Reference, ct);
        if (existing != null)
        {
            ValidateCustomer(existing, claim.Reference);
            await Complete(claim, existing.Id!.Value);
            return existing.Id;
        }
        if (!owned) return null;
        var customer = await CreateAndReconcile(claim, async token =>
        {
            var response = await _client.Customers.CreateCustomer(
                new MaxioAdvancedBilling.Requests.Customers.CreateCustomerOperationRequest
                {
                    Body = new MaxioAdvancedBilling.Models.CreateCustomerRequest
                    {
                        Customer = new MaxioAdvancedBilling.Models.CreateCustomer
                        { FirstName = "eShop", LastName = "Shopper", Email = email, Reference = claim.Reference }
                    }
                }, cancellationToken: token);
            var c = response?.Customer ?? throw InvalidResponse();
            ValidateCustomer(c, claim.Reference);
            return c;
        }, token => LookupCustomer(claim.Reference, token), c => c.Id, ct);
        return customer?.Id;
    }

    private async Task<T?> CreateAndReconcile<T>(BillingClaim claim, Func<CancellationToken, Task<T>> create,
        Func<CancellationToken, Task<T?>> lookup, Func<T, int?> id, CancellationToken ct) where T : class
    {
        T? result;
        try
        {
            result = await create(ct);
        }
        catch (Exception ex) when (ex is MaxioAdvancedBilling.Core.Exceptions.SdkException or OperationCanceledException or BillingException)
        {
            // Use a fresh deadline: the request token may already have expired or disconnected.
            using var recovery = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            try { result = await lookup(recovery.Token); }
            catch (Exception re) when (re is MaxioAdvancedBilling.Core.Exceptions.SdkException or OperationCanceledException or BillingException)
            { result = null; }
            if (result == null)
            {
                // Only an explicit rejection can release the claim. 5xx/transport/malformed 2xx are unknown.
                if (ex is MaxioAdvancedBilling.Core.Exceptions.ApiException api && (int)api.StatusCode is >= 400 and < 500)
                {
                    _db.BillingClaims.Remove(claim);
                    await _db.SaveChangesAsync(CancellationToken.None);
                    throw;
                }
                claim.State = "Unknown";
                claim.UpdatedUtc = DateTime.UtcNow;
                await _db.SaveChangesAsync(CancellationToken.None);
                _logger.LogWarning("Billing {Kind} outcome unknown; claim {ClaimId} retained for reconciliation", claim.Kind, claim.Id);
                return null;
            }
        }
        if (id(result) is not int providerId || providerId <= 0) throw InvalidResponse();
        await Complete(claim, providerId);
        return result;
    }

    private async Task<T?> BeforeWriteLookup<T>(BillingClaim claim, Func<Task<T?>> lookup) where T : class
    {
        try { return await lookup(); }
        catch
        {
            // No create has run yet: a failed preliminary read can safely release this new claim.
            _db.BillingClaims.Remove(claim);
            await _db.SaveChangesAsync(CancellationToken.None);
            throw;
        }
    }

    private async Task<(BillingClaim Claim, bool Owned)> TryClaim(string userId, string kind, string? product, CancellationToken ct)
    {
        var key = Hash(System.Text.Json.JsonSerializer.Serialize(new[] { _site, userId, kind, product }));
        var existing = await _db.BillingClaims.SingleOrDefaultAsync(x => x.Id == key, ct);
        if (existing != null) return (existing, false);
        var claim = new BillingClaim { Id = key, Site = _site, UserId = userId, Kind = kind,
            ProductHandle = product, Reference = "eshop-" + key };
        _db.BillingClaims.Add(claim);
        try { await _db.SaveChangesAsync(ct); return (claim, true); }
        catch (DbUpdateException ex) when (ex.InnerException is Microsoft.Data.SqlClient.SqlException sql && sql.Number is 2601 or 2627)
        {
            _db.Entry(claim).State = EntityState.Detached;
            return (await _db.BillingClaims.SingleAsync(x => x.Id == key, ct), false);
        }
        catch (ArgumentException)
        {
            // EF InMemory enforces the PK with a dictionary rather than DbUpdateException.
            _db.Entry(claim).State = EntityState.Detached;
            var prior = await _db.BillingClaims.SingleOrDefaultAsync(x => x.Id == key, ct);
            if (prior == null) throw;
            return (prior, false);
        }
    }

    private async Task Complete(BillingClaim claim, int providerId)
    {
        claim.ProviderId = providerId; claim.State = "Complete"; claim.UpdatedUtc = DateTime.UtcNow;
        await _db.SaveChangesAsync(CancellationToken.None);
        _logger.LogInformation("Billing {Kind} completed; claim {ClaimId}", claim.Kind, claim.Id);
    }

    private async Task<MaxioAdvancedBilling.Models.Customer?> LookupCustomer(string reference, CancellationToken ct)
    {
        try
        {
            var response = await _client.Customers.ReadCustomerByReference(
                new MaxioAdvancedBilling.Requests.Customers.ReadCustomerByReferenceRequest { Reference = reference }, cancellationToken: ct);
            var customer = response?.Customer ?? throw InvalidResponse();
            ValidateCustomer(customer, reference);
            return customer;
        }
        catch (MaxioAdvancedBilling.Core.Exceptions.ApiException<MaxioAdvancedBilling.Core.ErrorResponse.RawError> ex)
            when (ex.StatusCode == HttpStatusCode.NotFound) { return null; }
    }

    private async Task<MaxioAdvancedBilling.Models.Subscription?> LookupSubscription(string reference, CancellationToken ct)
    {
        try
        {
            var response = await _client.Subscriptions.FindSubscription(
                new MaxioAdvancedBilling.Requests.Subscriptions.FindSubscriptionRequest { Reference = reference }, cancellationToken: ct);
            return response?.Subscription ?? throw InvalidResponse();
        }
        catch (MaxioAdvancedBilling.Core.Exceptions.ApiException<MaxioAdvancedBilling.Errors.FindSubscriptionError> ex)
            when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            if (ex.Error.TryGetNoContent(out _) || ex.Error.TryGetRawError(out _)) return null;
            throw;
        }
    }

    public Task<SubscriptionAccount> GetSubscriptionsAsync(string userId, CancellationToken cancellationToken) => Bounded(async ct =>
    {
        var reference = "eshop-" + Hash(System.Text.Json.JsonSerializer.Serialize(new string?[] { _site, userId, "Customer", null }));
        var customer = await LookupCustomer(reference, ct);
        var pending = await _db.BillingClaims.AnyAsync(x => x.Site == _site && x.UserId == userId && x.State != "Complete", ct);
        if (customer == null) return new SubscriptionAccount(Array.Empty<SubscriptionDetails>(), pending);
        var rows = await _client.Customers.ListCustomerSubscriptions(
            new MaxioAdvancedBilling.Requests.Customers.ListCustomerSubscriptionsRequest { CustomerId = customer.Id!.Value }, cancellationToken: ct);
        if (rows == null) throw InvalidResponse();
        var details = new List<SubscriptionDetails>();
        foreach (var row in rows)
        {
            var s = row?.Subscription ?? throw InvalidResponse();
            if (s.Customer?.Id != customer.Id) throw InvalidResponse();
            if (s.Product?.ProductFamily?.Handle == _settings.ProductFamilyHandle) details.Add(ToDetails(s));
        }
        return new SubscriptionAccount(details, pending);
    }, cancellationToken);

    // Read-only recovery, including claims left Pending by a process crash. Never resends creates.
    public Task<int> ReconcileAsync(CancellationToken cancellationToken) => Bounded(async ct =>
    {
        var claims = await _db.BillingClaims.Where(x => x.Site == _site && x.State != "Complete")
            .OrderBy(x => x.UpdatedUtc).Take(100).ToListAsync(ct);
        foreach (var claim in claims)
        {
            try
            {
                int? id;
                if (claim.Kind == "Customer") id = (await LookupCustomer(claim.Reference, ct))?.Id;
                else
                {
                    var s = await LookupSubscription(claim.Reference, ct);
                    if (s != null)
                    {
                        var customerRef = "eshop-" + Hash(System.Text.Json.JsonSerializer.Serialize(new string?[] { _site, claim.UserId, "Customer", null }));
                        var customer = await LookupCustomer(customerRef, ct) ?? throw InvalidResponse();
                        ValidateSubscription(s, customer.Id!.Value, claim.ProductHandle!, claim.Reference);
                        _ = ToDetails(s);
                    }
                    id = s?.Id;
                }
                if (id is > 0) await Complete(claim, id.Value);
                else
                {
                    claim.UpdatedUtc = DateTime.UtcNow; // rotate unresolved claims so later rows are not starved
                    await _db.SaveChangesAsync(ct);
                }
            }
            catch (Exception ex) when (ex is MaxioAdvancedBilling.Core.Exceptions.SdkException or BillingException)
            {
                // One corrupt or unavailable record must not starve every other claim.
                claim.UpdatedUtc = DateTime.UtcNow;
                await _db.SaveChangesAsync(ct);
                _logger.LogWarning("Billing reconciliation deferred for claim {ClaimId}", claim.Id);
            }
        }
        return claims.Count;
    }, cancellationToken);

    private async Task<T> Bounded<T>(Func<CancellationToken, Task<T>> action, CancellationToken ct)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(TimeSpan.FromSeconds(30));
        try { return await action(deadline.Token); }
        catch (MaxioAdvancedBilling.Core.Exceptions.ApiException<MaxioAdvancedBilling.Errors.CreateCustomerError> ex)
        {
            if (ex.Error.TryGetCustomerErrorResponse1(out var body))
            {
                if (body.Errors != null) { _ = body.Errors.TryGetCustomerError(out _); _ = body.Errors.TryGetListOfString(out _); }
            }
            else _ = ex.Error.TryGetRawError(out _);
            throw ProviderError(ex);
        }
        catch (MaxioAdvancedBilling.Core.Exceptions.ApiException<MaxioAdvancedBilling.Errors.CreateSubscriptionError> ex)
        {
            if (ex.Error.TryGetErrorListResponse1(out var validation))
                _logger.LogWarning("Billing subscription validation: {Validation}", string.Join("; ", validation.Errors ?? Array.Empty<string>()));
            else _ = ex.Error.TryGetRawError(out _);
            throw ProviderError(ex);
        }
        catch (MaxioAdvancedBilling.Core.Exceptions.ApiException<MaxioAdvancedBilling.Errors.ListProductsForProductFamilyError> ex)
        { if (!ex.Error.TryGetString(out _)) _ = ex.Error.TryGetRawError(out _); throw ProviderError(ex); }
        catch (MaxioAdvancedBilling.Core.Exceptions.ApiException<MaxioAdvancedBilling.Errors.FindSubscriptionError> ex)
        { if (!ex.Error.TryGetNoContent(out _)) _ = ex.Error.TryGetRawError(out _); throw ProviderError(ex); }
        catch (MaxioAdvancedBilling.Core.Exceptions.ApiException<MaxioAdvancedBilling.Core.ErrorResponse.RawError> ex) { throw ProviderError(ex); }
        catch (MaxioAdvancedBilling.Core.Exceptions.ResponseDeserializationException ex) { throw ProviderError(ex); }
        catch (MaxioAdvancedBilling.Core.Exceptions.ApiException ex) { throw ProviderError(ex); }
        catch (MaxioAdvancedBilling.Core.Exceptions.SdkException)
        { throw new BillingException(502, "The billing provider is temporarily unavailable."); }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        { throw new BillingException(504, "The billing request exceeded its time budget."); }
    }

    private BillingException ProviderError(MaxioAdvancedBilling.Core.Exceptions.ApiException ex)
    {
        var status = (int)ex.StatusCode;
        _logger.LogWarning("Billing provider returned HTTP {StatusCode}", status);
        return new BillingException(status switch { 429 => 503, 401 or 403 => 502, >= 400 and < 500 => status, _ => 502 },
            status == 422 ? "The billing provider rejected enrollment. Check that the plan permits subscription without payment details." : "The billing provider could not complete the request.");
    }

    private static void ValidateCustomer(MaxioAdvancedBilling.Models.Customer c, string reference)
    { if (c.Id is not > 0 || c.Reference != reference) throw InvalidResponse(); }
    private static void ValidateSubscription(MaxioAdvancedBilling.Models.Subscription s, int customerId, string product, string reference)
    { if (s.Id is not > 0 || s.Reference != reference || s.Customer?.Id != customerId || s.Product?.Handle != product) throw InvalidResponse(); }
    private static SubscriptionPlan ToPlan(MaxioAdvancedBilling.Models.Product p)
    {
        if (string.IsNullOrWhiteSpace(p.Handle) || string.IsNullOrWhiteSpace(p.Name) || p.PriceInCents is null || p.Interval is null || p.IntervalUnit is null) throw InvalidResponse();
        return new(p.Handle, p.Name, p.Description, p.PriceInCents.Value, p.Interval.Value, p.IntervalUnit.Value);
    }
    private static SubscriptionDetails ToDetails(MaxioAdvancedBilling.Models.Subscription s)
    {
        if (s.Id is not > 0 || s.Product == null || s.State == null || s.ProductPriceInCents == null) throw InvalidResponse();
        var p = ToPlan(s.Product);
        return new(s.Id.Value, p.Handle, p.Name, s.ProductPriceInCents.Value, s.Currency, p.Interval, p.IntervalUnit, s.State.Value, s.NextAssessmentAt ?? s.CurrentPeriodEndsAt);
    }
    private static BillingException InvalidResponse() => new(502, "The billing provider returned incomplete or inconsistent data.");
    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
}
