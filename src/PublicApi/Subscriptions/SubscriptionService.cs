using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.eShopWeb.Infrastructure.Identity;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

public record SubscriptionPlan(string Handle, string? Name, string? Description, long? PriceInCents, int? Interval, string? IntervalUnit);
public record ShopperSubscription(int Id, SubscriptionPlan Plan, long? PriceInCents, string? Currency, string? State, DateTimeOffset? NextBillingDate);
public sealed class BillingException(string code, int status) : Exception
{
    public string Code { get; } = code;
    public int Status { get; } = status;
}

public sealed class SubscriptionService(MaxioAdvancedBilling.MaxioAdvancedBillingClient client, CatalogContext db, MaxioSettings settings, ILogger<SubscriptionService> logger)
{
    private string Key(string userId, string plan) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(settings.Subdomain + "\n" + settings.BaseUrl + "\n" + userId + "\n" + plan)));
    private static BillingException InvalidResponse() => new("billing-invalid-response", 502);
    private SubscriptionPlan Plan(MaxioAdvancedBilling.Models.Product? p)
    {
        if (p == null || string.IsNullOrWhiteSpace(p.Handle) || p.Id is not > 0) throw InvalidResponse();
        return new(p.Handle, p.Name, p.Description, p.PriceInCents, p.Interval, p.IntervalUnit?.Value);
    }

    public async Task<IReadOnlyList<SubscriptionPlan>> Plans(CancellationToken ct)
    {
        var plans = new List<SubscriptionPlan>();
        for (var page = 1; page <= 100; page++)
        {
            var response = await client.ProductFamilies.ListProductsForProductFamily(new MaxioAdvancedBilling.Requests.ProductFamilies.ListProductsForProductFamilyRequest
            { ProductFamilyId = "handle:" + settings.ProductFamilyHandle, Page = page, PerPage = 200, IncludeArchived = false }, cancellationToken: ct);
            if (response == null) throw InvalidResponse();
            foreach (var item in response)
            {
                var p = item?.Product;
                if (p == null) throw InvalidResponse();
                if (p.ArchivedAt == null) plans.Add(Plan(p));
            }
            if (response.Count < 200) return plans;
        }
        throw new BillingException("catalog-incomplete", 502);
    }

    // INSERT precedes every provider POST. A pending/unknown claim only permits reconciliation.
    private async Task<(BillingClaim Claim, bool Acquired)> TryClaim(string userId, string plan, CancellationToken ct)
    {
        var key = Key(userId, plan);
        var claim = new BillingClaim { Id = key, UserId = userId, PlanHandle = plan, Reference = "eshop-" + key.ToLowerInvariant() };
        db.BillingClaims.Add(claim);
        try { await db.SaveChangesAsync(ct); return (claim, true); }
        catch (DbUpdateException) { db.Entry(claim).State = EntityState.Detached; }
        catch (ArgumentException) { db.Entry(claim).State = EntityState.Detached; }
        var prior = await db.BillingClaims.SingleOrDefaultAsync(x => x.Id == key, ct);
        if (prior == null) throw new BillingException("billing-claim-unavailable", 503);
        return (prior, false);
    }

    private async Task Complete(BillingClaim claim, int id)
    {
        claim.ProviderId = id;
        claim.State = "complete";
        using var save = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await db.SaveChangesAsync(save.Token);
        logger.LogInformation("Billing claim completed for provider ID {ProviderId}", id);
    }

    private async Task<MaxioAdvancedBilling.Models.Customer?> ReadCustomer(string reference, CancellationToken ct)
    {
        try
        {
            var r = await client.Customers.ReadCustomerByReference(new MaxioAdvancedBilling.Requests.Customers.ReadCustomerByReferenceRequest { Reference = reference }, cancellationToken: ct);
            var customer = r?.Customer;
            if (customer?.Id is not > 0 || customer.Reference != reference) throw InvalidResponse();
            return customer;
        }
        catch (MaxioAdvancedBilling.Core.Exceptions.ApiException<MaxioAdvancedBilling.Core.ErrorResponse.RawError> ex) when (ex.StatusCode == HttpStatusCode.NotFound) { return null; }
    }

    private async Task<MaxioAdvancedBilling.Models.Subscription?> ReadSubscription(string reference, int customerId, string plan, CancellationToken ct)
    {
        try
        {
            var r = await client.Subscriptions.FindSubscription(new MaxioAdvancedBilling.Requests.Subscriptions.FindSubscriptionRequest { Reference = reference }, cancellationToken: ct);
            var s = r?.Subscription;
            Validate(s, customerId, plan, reference);
            return s;
        }
        catch (MaxioAdvancedBilling.Core.Exceptions.ApiException<MaxioAdvancedBilling.Errors.FindSubscriptionError> ex)
        {
            if (ex.Error.TryGetNoContent(out _) && ex.StatusCode == HttpStatusCode.NotFound) return null;
            if (ex.Error.TryGetRawError(out _)) throw;
            throw;
        }
    }

    private void Validate(MaxioAdvancedBilling.Models.Subscription? s, int customerId, string? plan = null, string? reference = null)
    {
        if (s?.Id is not > 0 || s.Customer?.Id != customerId || (plan != null && s.Product?.Handle != plan) || (reference != null && s.Reference != reference)) throw InvalidResponse();
        Plan(s.Product);
    }

    private async Task<T> Write<T>(BillingClaim claim, Func<CancellationToken, Task<T>> post, Func<CancellationToken, Task<T?>> read, Func<T, int> id, CancellationToken ct) where T : class
    {
        try
        {
            var result = await post(ct);
            await Complete(claim, id(result));
            return result;
        }
        catch (Exception ex) when (ex is MaxioAdvancedBilling.Core.Exceptions.SdkException || ex is OperationCanceledException || ex is BillingException)
        {
            // Only a definitive 4xx releases the reservation. 408 and 5xx remain ambiguous.
            if (ex is MaxioAdvancedBilling.Core.Exceptions.ApiException api && (int)api.StatusCode >= 400 && (int)api.StatusCode < 500 && api.StatusCode != HttpStatusCode.RequestTimeout)
            {
                db.Remove(claim);
                using var release = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                await db.SaveChangesAsync(release.Token);
                throw;
            }
            claim.State = "unknown";
            using var reconcile = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            try
            {
                await db.SaveChangesAsync(reconcile.Token);
                var found = await read(reconcile.Token);
                if (found != null) { await Complete(claim, id(found)); return found; }
            }
            catch (Exception readError) when (readError is MaxioAdvancedBilling.Core.Exceptions.SdkException || readError is OperationCanceledException || readError is BillingException)
            { logger.LogWarning("Billing reconciliation remains pending"); }
            throw new BillingException("billing-outcome-unknown", 409);
        }
    }

    private async Task<int> Customer(ApplicationUser user, bool create, CancellationToken ct)
    {
        var reference = "eshop-" + Key(user.Id, "").ToLowerInvariant();
        if (!create)
        {
            var existing = await ReadCustomer(reference, ct);
            if (existing != null)
            {
                var customerClaim = await db.BillingClaims.SingleOrDefaultAsync(x => x.Id == Key(user.Id, ""), ct);
                if (customerClaim != null && customerClaim.State != "complete") await Complete(customerClaim, existing.Id!.Value);
                return existing.Id!.Value;
            }
            if (await db.BillingClaims.AnyAsync(x => x.Id == Key(user.Id, ""), ct)) throw new BillingException("billing-outcome-unknown", 409);
            return 0;
        }
        if (string.IsNullOrWhiteSpace(user.Email)) throw new BillingException("shopper-email-required", 422);
        var (claim, acquired) = await TryClaim(user.Id, "", ct);
        var found = await ReadCustomer(reference, ct);
        if (found != null) { await Complete(claim, found.Id!.Value); return found.Id.Value; }
        if (!acquired) throw new BillingException("billing-in-progress", 409);
        var customer = await Write(claim, async token =>
        {
            var r = await client.Customers.CreateCustomer(new MaxioAdvancedBilling.Requests.Customers.CreateCustomerOperationRequest
            { Body = new MaxioAdvancedBilling.Models.CreateCustomerRequest { Customer = new MaxioAdvancedBilling.Models.CreateCustomer { FirstName = "eShop", LastName = "Shopper", Email = user.Email, Reference = reference } } }, cancellationToken: token);
            if (r?.Customer?.Id is not > 0 || r.Customer.Reference != reference) throw InvalidResponse();
            return r.Customer;
        }, token => ReadCustomer(reference, token), c => c.Id!.Value, ct);
        return customer.Id!.Value;
    }

    public async Task<ShopperSubscription> Enroll(ApplicationUser user, string handle, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(handle) || !(await Plans(ct)).Any(p => p.Handle == handle)) throw new BillingException("plan-not-offered", 400);
        // No card is collected by this flow. Select the site's supported recurring invoice collection method.
        var site = (await client.Sites.ReadSite(cancellationToken: ct))?.Site;
        if (site?.RelationshipInvoicingEnabled == null) throw InvalidResponse();
        var collection = site.RelationshipInvoicingEnabled.Value
            ? MaxioAdvancedBilling.Models.Enums.CollectionMethod.Remittance
            : MaxioAdvancedBilling.Models.Enums.CollectionMethod.Invoice;
        var customerId = await Customer(user, true, ct);
        var (claim, acquired) = await TryClaim(user.Id, handle, ct);
        var existing = await ReadSubscription(claim.Reference, customerId, handle, ct);
        if (existing != null) { await Complete(claim, existing.Id!.Value); return Map(existing, customerId); }
        if (!acquired) throw new BillingException("billing-in-progress", 409);
        var subscription = await Write(claim, async token =>
        {
            var r = await client.Subscriptions.CreateSubscription(new MaxioAdvancedBilling.Requests.Subscriptions.CreateSubscriptionOperationRequest
            { Body = new MaxioAdvancedBilling.Models.CreateSubscriptionRequest { Subscription = new MaxioAdvancedBilling.Models.CreateSubscription { CustomerId = customerId, ProductHandle = handle, Reference = claim.Reference, PaymentCollectionMethod = collection } } }, cancellationToken: token);
            Validate(r?.Subscription, customerId, handle, claim.Reference);
            return r!.Subscription!;
        }, token => ReadSubscription(claim.Reference, customerId, handle, token), s => s.Id!.Value, ct);
        return Map(subscription, customerId);
    }

    private ShopperSubscription Map(MaxioAdvancedBilling.Models.Subscription s, int customerId)
    {
        Validate(s, customerId);
        return new(s.Id!.Value, Plan(s.Product), s.ProductPriceInCents, s.Currency, s.State?.Value, s.NextAssessmentAt ?? s.CurrentPeriodEndsAt);
    }

    public async Task<IReadOnlyList<ShopperSubscription>> Mine(ApplicationUser user, CancellationToken ct)
    {
        var customerId = await Customer(user, false, ct);
        if (customerId == 0) return Array.Empty<ShopperSubscription>();
        var pending = await db.BillingClaims.Where(x => x.UserId == user.Id && x.PlanHandle != "" && x.State != "complete").ToListAsync(ct);
        foreach (var claim in pending.Where(x => x.Id == Key(user.Id, x.PlanHandle)))
        {
            var found = await ReadSubscription(claim.Reference, customerId, claim.PlanHandle, ct);
            if (found == null) throw new BillingException("billing-outcome-unknown", 409);
            await Complete(claim, found.Id!.Value);
        }
        var response = await client.Customers.ListCustomerSubscriptions(new MaxioAdvancedBilling.Requests.Customers.ListCustomerSubscriptionsRequest { CustomerId = customerId }, cancellationToken: ct);
        if (response == null) throw InvalidResponse();
        return response.Select(x => x?.Subscription == null ? throw InvalidResponse() : Map(x.Subscription, customerId)).ToList();
    }
}
