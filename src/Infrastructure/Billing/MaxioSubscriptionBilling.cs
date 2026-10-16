using System;
using System.Collections.Generic;
using System.IO;
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
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Billing;

public sealed class MaxioSubscriptionBilling(
    MaxioAdvancedBilling.MaxioAdvancedBillingClient client, CatalogContext db,
    IOptions<MaxioSettings> options, ILogger<MaxioSubscriptionBilling> logger) : ISubscriptionBilling
{
    private readonly MaxioSettings _settings = options.Value;
    private string Scope => Hash((_settings.BaseUrl ?? _settings.Subdomain) + "\n" + _settings.ProductFamilyHandle);
    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    // Every SDK call in a foreground flow shares this deadline, including paged reads.
    private async Task<T> Bounded<T>(Func<CancellationToken, Task<T>> action, CancellationToken ct)
    {
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(ct);
        budget.CancelAfter(TimeSpan.FromSeconds(30));
        var correlation = Guid.NewGuid();
        var started = System.Diagnostics.Stopwatch.StartNew();
        try { return await action(budget.Token); }
        catch (BillingException) { throw; }
        catch (MaxioAdvancedBilling.Core.Exceptions.ApiException ex)
        {
            logger.LogWarning("Billing request {Correlation} received provider status {StatusCode}.", correlation, (int)ex.StatusCode);
            throw new BillingException("The billing provider could not process this request. Please try again later.", SafeStatus(ex));
        }
        catch (Exception ex) when (ex is MaxioAdvancedBilling.Core.Exceptions.SdkException or OperationCanceledException or InvalidDataException)
        {
            // SDK exception messages can contain URLs or personal data. Never expose/log them.
            logger.LogWarning("Billing request {Correlation} could not be processed ({FailureType}).", correlation, ex.GetType().Name);
            throw new BillingException("Billing is temporarily unavailable. Please try again later.");
        }
        finally { logger.LogInformation("Billing request {Correlation} finished in {ElapsedMs}ms.", correlation, started.ElapsedMilliseconds); }
    }

    public Task<SubscriptionPlans> ListPlansAsync(CancellationToken ct) => Bounded(token => ListPlansCore(token, ct), ct);

    private async Task<SubscriptionPlans> ListPlansCore(CancellationToken ct, CancellationToken callerToken)
    {
        var result = new List<SubscriptionPlan>();
        var seen = new HashSet<int>();
        for (var page = 1; page <= 50; page++)
        {
            System.Collections.Generic.IReadOnlyList<MaxioAdvancedBilling.Models.ProductResponse> products;
            try
            {
                products = await client.Products.ListProducts(new MaxioAdvancedBilling.Requests.Products.ListProductsRequest
                { Page = page, PerPage = 200, IncludeArchived = false }, cancellationToken: ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested && !callerToken.IsCancellationRequested)
            { return new SubscriptionPlans(result, true, page); }
            if (products == null) throw new InvalidDataException();
            var progress = false;
            foreach (var envelope in products)
            {
                var p = envelope?.Product;
                if (p?.Id is not > 0) throw new InvalidDataException();
                if (!seen.Add(p.Id.Value)) continue;
                progress = true;
                if (!Offered(p)) continue;
                if (string.IsNullOrWhiteSpace(p.Handle) || string.IsNullOrWhiteSpace(p.Name) || p.PriceInCents is not >= 0 || p.Interval is not > 0 || p.IntervalUnit == null)
                    throw new InvalidDataException();
                result.Add(new SubscriptionPlan(p.Handle, p.Name, p.Description, p.PriceInCents.Value, p.Interval.Value, p.IntervalUnit.Value, p.RequireCreditCard ?? false,
                    MaxioAdvancedBilling.Models.Enums.CollectionMethod.Remittance.Value));
            }
            if (products.Count < 200) return new SubscriptionPlans(result, false, null);
            if (!progress || page == 50) return new SubscriptionPlans(result, true, page + 1);
        }
        throw new InvalidDataException();
    }

    private bool Offered(MaxioAdvancedBilling.Models.Product p) =>
        p.ArchivedAt == null && p.ProductFamily?.ArchivedAt == null && p.ProductFamily?.Handle == _settings.ProductFamilyHandle;

    private async Task<string> CustomerReference(BillingShopper shopper, CancellationToken ct)
    {
        var stored = await db.BillingClaims.AsNoTracking().Where(x => x.Scope == Scope && x.UserId == shopper.UserId)
            .Select(x => x.CustomerReference).FirstOrDefaultAsync(ct);
        return stored ?? "eshop-c-" + Hash(Scope + "\n" + shopper.NormalizedUsername);
    }

    public Task<SubscriptionEnrollment> SubscribeAsync(BillingShopper shopper, string productHandle, string firstName, string lastName, CancellationToken ct) =>
        Bounded(async token =>
        {
            var catalog = await ListPlansCore(token, ct);
            if (catalog.IsPartial) throw new BillingException("The plan catalog is incomplete. Please try again later.", 503);
            if (!catalog.Plans.Any(p => p.Handle == productHandle)) throw new BillingException("Choose an available subscription plan.", 400);
            var reference = await CustomerReference(shopper, token);
            var customer = await EnsureCustomer(shopper, reference, firstName, lastName, token);
            if (customer == null) return new SubscriptionEnrollment("pending", null);
            var claim = NewClaim(shopper, reference, productHandle);
            claim.CustomerId = customer.Id;
            var acquired = await TryClaimAsync(claim, token);
            if (!acquired)
            {
                var existing = await db.BillingClaims.SingleAsync(x => x.Id == claim.Id, token);
                var current = await ReconcileClaimAsync(existing, token);
                return new SubscriptionEnrollment(current == null ? "pending" : "completed", current);
            }
            // Read before create also recovers after an in-memory restart.
            ShopperSubscription? found;
            try { found = await FindSubscription(claim, token); token.ThrowIfCancellationRequested(); }
            catch { db.Remove(claim); await db.SaveChangesAsync(CancellationToken.None); throw; }
            if (found != null)
            {
                await Complete(claim, found.Id, token);
                return new SubscriptionEnrollment("completed", found);
            }
            try
            {
                var response = await client.Subscriptions.CreateSubscription(new MaxioAdvancedBilling.Requests.Subscriptions.CreateSubscriptionOperationRequest
                {
                    Body = new MaxioAdvancedBilling.Models.CreateSubscriptionRequest
                    {
                        Subscription = new MaxioAdvancedBilling.Models.CreateSubscription
                        {
                            ProductHandle = productHandle, CustomerId = customer.Id, Reference = claim.Reference,
                            // This no-card flow bills by invoice; it does not attempt automatic card collection.
                            PaymentCollectionMethod = MaxioAdvancedBilling.Models.Enums.CollectionMethod.Remittance
                        }
                    }
                }, cancellationToken: token);
                var subscription = Map(response?.Subscription, customer.Id!.Value, claim.Reference, productHandle);
                await Complete(claim, subscription.Id, CancellationToken.None);
                return new SubscriptionEnrollment("completed", subscription, true);
            }
            catch (Exception ex) when (WriteFailure(ex))
            {
                var recovered = await RecoverWrite(claim, ex);
                return new SubscriptionEnrollment(recovered == null ? "pending" : "completed", recovered);
            }
        }, ct);

    private BillingClaim NewClaim(BillingShopper shopper, string customerReference, string? handle = null)
    {
        var reference = handle == null ? customerReference : "eshop-s-" + Hash(customerReference + "\n" + handle);
        return new BillingClaim { Id = Hash(Scope + "\n" + reference), Scope = Scope, UserId = shopper.UserId, CustomerReference = customerReference, Reference = reference, ProductHandle = handle };
    }

    public async Task<bool> TryClaimAsync(BillingClaim claim, CancellationToken ct)
    {
        db.BillingClaims.Add(claim);
        try { await db.SaveChangesAsync(ct); return true; }
        catch (Exception ex) when (ex is DbUpdateException or ArgumentException)
        {
            db.Entry(claim).State = EntityState.Detached;
            // Only a real stored winner makes this a duplicate; propagate unrelated failures.
            if (!await db.BillingClaims.AsNoTracking().AnyAsync(x => x.Id == claim.Id, ct)) throw;
            return false;
        }
    }

    private async Task<MaxioAdvancedBilling.Models.Customer?> EnsureCustomer(BillingShopper shopper, string reference, string first, string last, CancellationToken ct)
    {
        var claim = NewClaim(shopper, reference);
        var acquired = await TryClaimAsync(claim, ct);
        if (!acquired) claim = await db.BillingClaims.SingleAsync(x => x.Id == claim.Id, ct);
        MaxioAdvancedBilling.Models.Customer? found;
        try { found = await ReadCustomer(reference, ct); ct.ThrowIfCancellationRequested(); }
        catch
        {
            if (acquired) { db.Remove(claim); await db.SaveChangesAsync(CancellationToken.None); }
            throw;
        }
        if (found != null) { await Complete(claim, found.Id!.Value, ct); return found; }
        if (!acquired) return null;
        try
        {
            var response = await client.Customers.CreateCustomer(new MaxioAdvancedBilling.Requests.Customers.CreateCustomerOperationRequest
            {
                Body = new MaxioAdvancedBilling.Models.CreateCustomerRequest
                {
                    Customer = new MaxioAdvancedBilling.Models.CreateCustomer { FirstName = first, LastName = last, Email = shopper.Email, Reference = reference }
                }
            }, cancellationToken: ct);
            var customer = ValidateCustomer(response?.Customer, reference);
            await Complete(claim, customer.Id!.Value, CancellationToken.None);
            return customer;
        }
        catch (Exception ex) when (WriteFailure(ex))
        {
            await RecoverWrite(claim, ex);
            if (claim.State == "Completed") return new MaxioAdvancedBilling.Models.Customer { Id = claim.ProviderId, Reference = reference };
            return null;
        }
    }

    private static bool WriteFailure(Exception ex) => ex is MaxioAdvancedBilling.Core.Exceptions.SdkException or OperationCanceledException or InvalidDataException;
    private static bool DefiniteRefusal(Exception ex) => ex is MaxioAdvancedBilling.Core.Exceptions.ApiException api &&
        (int)api.StatusCode >= 400 && (int)api.StatusCode < 500 && api.StatusCode != HttpStatusCode.RequestTimeout && (int)api.StatusCode != 429;
    private static int SafeStatus(MaxioAdvancedBilling.Core.Exceptions.ApiException api) =>
        (int)api.StatusCode == 429 ? 503 : api.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden ? 502 :
        (int)api.StatusCode >= 400 && (int)api.StatusCode < 500 ? (int)api.StatusCode : 502;

    private async Task<ShopperSubscription?> RecoverWrite(BillingClaim claim, Exception failure)
    {
        claim.State = "Unknown";
        claim.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(CancellationToken.None);
        logger.LogWarning("Billing claim {ClaimId} is Unknown; reconciling by reference.", claim.Id);
        using var recovery = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        try
        {
            var found = await ReconcileClaimAsync(claim, recovery.Token);
            if (claim.State == "Completed") return found;
            if (DefiniteRefusal(failure))
            {
                db.Remove(claim);
                await db.SaveChangesAsync(recovery.Token);
                throw new BillingException("The billing provider declined enrollment. Check the plan requirements and your customer details.", SafeStatus((MaxioAdvancedBilling.Core.Exceptions.ApiException)failure));
            }
        }
        catch (BillingException) { throw; }
        catch (Exception ex) when (WriteFailure(ex)) { /* Retain Unknown. A failed read is never absence. */ }
        return null;
    }

    private async Task Complete(BillingClaim claim, int id, CancellationToken ct)
    {
        claim.ProviderId = id;
        if (claim.ProductHandle == null) claim.CustomerId = id;
        claim.State = "Completed";
        claim.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        logger.LogInformation("Billing claim {ClaimId} completed with provider ID {ProviderId}.", claim.Id, id);
    }

    private async Task<MaxioAdvancedBilling.Models.Customer?> ReadCustomer(string reference, CancellationToken ct)
    {
        try
        {
            var response = await client.Customers.ReadCustomerByReference(new MaxioAdvancedBilling.Requests.Customers.ReadCustomerByReferenceRequest { Reference = reference }, cancellationToken: ct);
            return ValidateCustomer(response?.Customer, reference);
        }
        catch (MaxioAdvancedBilling.Core.Exceptions.ApiException ex) when (ex is not MaxioAdvancedBilling.Core.Exceptions.ResponseDeserializationException && ex.StatusCode == HttpStatusCode.NotFound) { return null; }
    }

    private static MaxioAdvancedBilling.Models.Customer ValidateCustomer(MaxioAdvancedBilling.Models.Customer? c, string reference)
    {
        if (c?.Id is not > 0 || c.Reference != reference) throw new InvalidDataException();
        return c;
    }

    private async Task<ShopperSubscription?> FindSubscription(BillingClaim claim, CancellationToken ct)
    {
        try
        {
            var response = await client.Subscriptions.FindSubscription(new MaxioAdvancedBilling.Requests.Subscriptions.FindSubscriptionRequest { Reference = claim.Reference }, cancellationToken: ct);
            return Map(response?.Subscription, claim.CustomerId!.Value, claim.Reference, claim.ProductHandle!);
        }
        catch (MaxioAdvancedBilling.Core.Exceptions.ApiException ex) when (ex is not MaxioAdvancedBilling.Core.Exceptions.ResponseDeserializationException && ex.StatusCode == HttpStatusCode.NotFound) { return null; }
    }

    public async Task<ShopperSubscription?> ReconcileClaimAsync(BillingClaim claim, CancellationToken ct)
    {
        if (claim.ProductHandle == null)
        {
            var customer = await ReadCustomer(claim.Reference, ct);
            if (customer != null) await Complete(claim, customer.Id!.Value, ct);
            return null;
        }
        var subscription = await FindSubscription(claim, ct);
        if (subscription != null) await Complete(claim, subscription.Id, ct);
        return subscription;
    }

    public async Task ReconcilePendingAsync(CancellationToken ct)
    {
        // Never posts. Delay Claimed rows so another host's normal enrollment can finish first.
        var cutoff = DateTimeOffset.UtcNow.AddMinutes(-1);
        var candidates = await db.BillingClaims.Where(x => x.Scope == Scope && (x.State == "Unknown" || (x.State == "Claimed" && x.UpdatedAt < cutoff)))
            .OrderBy(x => x.UpdatedAt).Take(100).ToListAsync(ct);
        foreach (var claim in candidates)
        {
            ct.ThrowIfCancellationRequested();
            // Rotate unresolved claims through bounded sweeps so older failures cannot starve later work.
            claim.UpdatedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(ct);
            try { await ReconcileClaimAsync(claim, ct); }
            catch (Exception ex) when (WriteFailure(ex)) { logger.LogWarning("Billing claim {ClaimId} remains unresolved.", claim.Id); }
        }
    }

    public Task<ShopperSubscriptions> ListMineAsync(BillingShopper shopper, CancellationToken ct) => Bounded(async token =>
    {
        var reference = await CustomerReference(shopper, token);
        var customer = await ReadCustomer(reference, token);
        var claims = await db.BillingClaims.Where(x => x.Scope == Scope && x.UserId == shopper.UserId && x.State != "Completed").ToListAsync(token);
        foreach (var claim in claims) await ReconcileClaimAsync(claim, token);
        if (customer == null) return new ShopperSubscriptions(Array.Empty<ShopperSubscription>(), claims.Any(x => x.State != "Completed"));
        var response = await client.Customers.ListCustomerSubscriptions(new MaxioAdvancedBilling.Requests.Customers.ListCustomerSubscriptionsRequest { CustomerId = customer.Id!.Value }, cancellationToken: token);
        if (response == null) throw new InvalidDataException();
        var result = new List<ShopperSubscription>();
        foreach (var envelope in response)
        {
            var sub = envelope?.Subscription;
            if (sub?.Customer?.Id != customer.Id) throw new InvalidDataException();
            if (sub.Product?.ProductFamily?.Handle != _settings.ProductFamilyHandle) continue;
            result.Add(Map(sub, customer.Id.Value));
        }
        return new ShopperSubscriptions(result, claims.Any(x => x.State != "Completed"));
    }, ct);

    private ShopperSubscription Map(MaxioAdvancedBilling.Models.Subscription? s, int customerId, string? reference = null, string? handle = null)
    {
        if (s?.Id is not > 0 || s.Customer?.Id != customerId || (reference != null && s.Reference != reference) ||
            s.Product?.ProductFamily?.Handle != _settings.ProductFamilyHandle || string.IsNullOrWhiteSpace(s.Product.Handle) ||
            (handle != null && s.Product.Handle != handle) || string.IsNullOrWhiteSpace(s.Product.Name) ||
            s.ProductPriceInCents is not >= 0 || s.State == null) throw new InvalidDataException();
        return new ShopperSubscription(s.Id.Value, s.Product.Handle, s.Product.Name, s.ProductPriceInCents.Value, s.Currency, s.State.Value,
            s.NextAssessmentAt ?? s.CurrentPeriodEndsAt, s.NextAssessmentAt != null ? "next_assessment_at" : s.CurrentPeriodEndsAt != null ? "current_period_ends_at" : null,
            s.PaymentCollectionMethod?.Value);
    }
}
