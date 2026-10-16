using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.Infrastructure.Billing;

public sealed class SubscriptionBillingService : ISubscriptionBilling
{
    private readonly CatalogContext _db;
    private readonly MaxioGateway _gateway;
    private readonly MaxioSettings _settings;
    private readonly ILogger<SubscriptionBillingService> _logger;
    private readonly string _scope;

    public SubscriptionBillingService(CatalogContext db, MaxioGateway gateway, MaxioSettings settings,
        ILogger<SubscriptionBillingService> logger)
    {
        _db = db; _gateway = gateway; _settings = settings; _logger = logger;
        _scope = Hash(settings.BaseUrl ?? "https://" + settings.Subdomain + ".chargify.com");
    }

    private static string Hash(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();

    // A single deadline bounds all catalog pages/customer/subscription calls in an interactive operation.
    private static async Task<T> Bounded<T>(Func<CancellationToken, Task<T>> action, CancellationToken ct)
    {
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(ct);
        budget.CancelAfter(TimeSpan.FromSeconds(30));
        try { return await action(budget.Token); }
        catch (MaxioFailure ex) { throw MaxioGateway.PublicError(ex); }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        { throw new BillingException(504, "Billing took too long to respond."); }
    }

    public Task<SubscriptionPlans> GetPlansAsync(CancellationToken ct) => Bounded(LoadPlansAsync, ct);

    private async Task<SubscriptionPlans> LoadPlansAsync(CancellationToken ct)
    {
        var plans = new Dictionary<string, SubscriptionPlan>(StringComparer.Ordinal);
        for (var page = 1; page <= 50; page++)
        {
            var products = await _gateway.ProductsAsync(_settings.ProductFamilyHandle, page, ct);
            foreach (var envelope in products)
            {
                var p = envelope.Product;
                if (p == null) throw new MaxioFailure(null, false);
                if (p.ArchivedAt != null) continue;
                if (p.ProductFamily?.Handle != _settings.ProductFamilyHandle) throw new MaxioFailure(null, false);
                if (string.IsNullOrWhiteSpace(p.Handle) || string.IsNullOrWhiteSpace(p.Name) ||
                    p.PriceInCents is not >= 0 || p.Interval is not > 0 || p.IntervalUnit == null) continue;
                plans[p.Handle] = new SubscriptionPlan(p.Handle, p.Name, p.Description, p.PriceInCents.Value,
                    p.Interval.Value, p.IntervalUnit.Value, p.RequireCreditCard);
            }
            if (products.Count < 200) return new SubscriptionPlans(plans.Values.OrderBy(x => x.PriceInCents).ToList(), false);
        }
        return new SubscriptionPlans(plans.Values.OrderBy(x => x.PriceInCents).ToList(), true);
    }

    public Task<SubscribeResult> SubscribeAsync(BillingShopper shopper, string productHandle, CancellationToken ct) => Bounded(async token =>
    {
        if (string.IsNullOrWhiteSpace(productHandle) || productHandle.Length > 255)
            throw new BillingException(400, "A productHandle returned by subscription-plans is required.");
        var catalog = await LoadPlansAsync(token);
        if (catalog.IsTruncated) throw new BillingException(503, "The plan catalog could not be fully loaded.");
        if (!catalog.Plans.Any(p => p.Handle == productHandle))
            throw new BillingException(400, "Select a plan offered by subscription-plans.");

        var customer = await EnsureCustomerAsync(shopper, token);
        if (customer == null) return new SubscribeResult(null, true);
        var claim = NewClaim(shopper.UserId, "subscription", productHandle);
        claim.CustomerId = customer.Id;
        var owner = await TryClaimAsync(claim, token);
        if (!owner)
        {
            var found = await ReconcileSubscriptionAsync(claim, token);
            return new SubscribeResult(found == null ? null : Project(found, customer.Id!.Value), found == null);
        }

        var sent = false;
        try
        {
            var found = await ReconcileSubscriptionAsync(claim, token);
            if (found != null) return new SubscribeResult(Project(found, customer.Id!.Value), false);
            sent = true;
            var subscription = await _gateway.CreateSubscriptionAsync(claim.Reference, customer.Id!.Value, productHandle, token);
            ValidateSubscription(subscription, claim);
            await CompleteAsync(claim, subscription.Id!.Value, token);
            return new SubscribeResult(Project(subscription, customer.Id.Value), false);
        }
        catch (Exception ex) when (ex is MaxioFailure or OperationCanceledException)
        {
            // Reconciliation uses its own short budget even if the shopper disconnected or the original budget elapsed.
            if (!sent) { await ReleaseAsync(claim); throw; }
            var recovered = await RecoverSubscriptionAsync(claim);
            if (recovered != null) return new SubscribeResult(Project(recovered, customer.Id!.Value), false);
            if (ex is MaxioFailure failure && !failure.IsAmbiguous) { await ReleaseAsync(claim); throw; }
            _logger.LogWarning("Subscription outcome pending; claim {ClaimId}", claim.Id);
            return new SubscribeResult(null, true);
        }
    }, ct);

    private async Task<MaxioAdvancedBilling.Models.Customer?> EnsureCustomerAsync(BillingShopper shopper, CancellationToken ct)
    {
        var claim = NewClaim(shopper.UserId, "customer", null);
        var owner = await TryClaimAsync(claim, ct);
        if (!owner) return await ReconcileCustomerAsync(claim, ct);
        var sent = false;
        try
        {
            var found = await ReconcileCustomerAsync(claim, ct);
            if (found != null) return found;
            sent = true;
            var customer = await _gateway.CreateCustomerAsync(claim.Reference, shopper.Email, ct);
            ValidateCustomer(customer, claim);
            await CompleteAsync(claim, customer.Id!.Value, ct);
            return customer;
        }
        catch (Exception ex) when (ex is MaxioFailure or OperationCanceledException)
        {
            if (!sent) { await ReleaseAsync(claim); throw; }
            using var recovery = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            try
            {
                var customer = await ReconcileCustomerAsync(claim, recovery.Token);
                if (customer != null) return customer;
            }
            catch (Exception recoveryError) when (recoveryError is MaxioFailure or OperationCanceledException) { }
            if (ex is MaxioFailure failure && !failure.IsAmbiguous) { await ReleaseAsync(claim); throw; }
            _logger.LogWarning("Customer outcome pending; claim {ClaimId}", claim.Id);
            return null;
        }
    }

    public Task<MySubscriptions> GetMineAsync(BillingShopper shopper, CancellationToken ct) => Bounded(async token =>
    {
        var customerClaim = NewClaim(shopper.UserId, "customer", null);
        var customer = await _gateway.FindCustomerAsync(customerClaim.Reference, token);
        var pending = await _db.BillingClaims.AsNoTracking().AnyAsync(x => x.Scope == _scope && x.UserId == shopper.UserId && x.RemoteId == null, token);
        if (customer == null) return new MySubscriptions(Array.Empty<ShopperSubscription>(), pending);
        ValidateCustomer(customer, customerClaim);
        var results = await _gateway.CustomerSubscriptionsAsync(customer.Id!.Value, token);
        var list = new List<ShopperSubscription>();
        foreach (var envelope in results)
        {
            var s = envelope.Subscription ?? throw new MaxioFailure(null, false);
            if (s.Customer?.Id != customer.Id || string.IsNullOrWhiteSpace(s.Product?.ProductFamily?.Handle))
                throw new MaxioFailure(null, false);
            if (s.Product?.ProductFamily?.Handle != _settings.ProductFamilyHandle) continue;
            list.Add(Project(s, customer.Id.Value));
        }
        return new MySubscriptions(list, pending);
    }, ct);

    private BillingClaim NewClaim(string userId, string kind, string? handle)
    {
        var id = "eshop-" + (kind == "customer" ? "c-" : "s-") + Hash(_scope + "\n" + userId + "\n" + kind + "\n" + handle);
        return new BillingClaim { Id = id, Reference = id, Scope = _scope, UserId = userId, Kind = kind, ProductHandle = handle };
    }

    public async Task<bool> TryClaimAsync(BillingClaim claim, CancellationToken ct)
    {
        _db.BillingClaims.Add(claim);
        try { await _db.SaveChangesAsync(ct); return true; }
        catch (Exception ex) when (ex is DbUpdateException or ArgumentException)
        {
            _db.Entry(claim).State = EntityState.Detached;
            // Only classify an actual persisted competing claim as a collision; other database failures still fail.
            var existing = await _db.BillingClaims.AsNoTracking().SingleOrDefaultAsync(x => x.Id == claim.Id, ct);
            if (existing == null) throw;
            claim.RemoteId = existing.RemoteId;
            return false;
        }
    }

    private async Task CompleteAsync(BillingClaim claim, int id, CancellationToken ct)
    {
        var row = await _db.BillingClaims.FindAsync(new object[] { claim.Id }, ct);
        if (row == null) throw new InvalidOperationException("The billing claim no longer exists.");
        row.RemoteId = id;
        await _db.SaveChangesAsync(ct);
        _logger.LogInformation("Billing {Operation} settled with provider ID {ProviderId}", claim.Kind, id);
    }

    private async Task ReleaseAsync(BillingClaim claim)
    {
        using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        var row = await _db.BillingClaims.FindAsync(new object[] { claim.Id }, cleanup.Token);
        if (row != null) { _db.BillingClaims.Remove(row); await _db.SaveChangesAsync(cleanup.Token); }
    }

    private static void ValidateCustomer(MaxioAdvancedBilling.Models.Customer customer, BillingClaim claim)
    {
        if (customer.Id is not > 0 || customer.Reference != claim.Reference) throw new MaxioFailure(null, true);
    }

    private static void ValidateSubscription(MaxioAdvancedBilling.Models.Subscription s, BillingClaim claim)
    {
        if (s.Id is not > 0 || s.Reference != claim.Reference || s.Customer?.Id != claim.CustomerId || s.Product?.Handle != claim.ProductHandle)
            throw new MaxioFailure(null, true);
    }

    private static ShopperSubscription Project(MaxioAdvancedBilling.Models.Subscription s, int customerId)
    {
        if (s.Id is not > 0 || s.Customer?.Id != customerId || string.IsNullOrWhiteSpace(s.Product?.Handle))
            throw new MaxioFailure(null, false);
        return new ShopperSubscription(s.Id.Value, s.Product.Handle, s.Product.Name, s.ProductPriceInCents,
            s.Currency, s.Product.Interval, s.Product.IntervalUnit?.Value, s.State?.Value, s.NextAssessmentAt, s.CurrentPeriodEndsAt);
    }

    private async Task<MaxioAdvancedBilling.Models.Customer?> ReconcileCustomerAsync(BillingClaim claim, CancellationToken ct)
    {
        var customer = await _gateway.FindCustomerAsync(claim.Reference, ct);
        if (customer == null) return null;
        ValidateCustomer(customer, claim);
        await CompleteAsync(claim, customer.Id!.Value, ct);
        return customer;
    }

    private async Task<MaxioAdvancedBilling.Models.Subscription?> ReconcileSubscriptionAsync(BillingClaim claim, CancellationToken ct)
    {
        var subscription = await _gateway.FindSubscriptionAsync(claim.Reference, ct);
        if (subscription == null) return null;
        ValidateSubscription(subscription, claim);
        await CompleteAsync(claim, subscription.Id!.Value, ct);
        return subscription;
    }

    private async Task<MaxioAdvancedBilling.Models.Subscription?> RecoverSubscriptionAsync(BillingClaim claim)
    {
        using var recovery = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        try { return await ReconcileSubscriptionAsync(claim, recovery.Token); }
        catch (Exception ex) when (ex is MaxioFailure or OperationCanceledException) { return null; }
    }

    public async Task ReconcilePendingAsync(CancellationToken ct)
    {
        // One bounded batch per cycle; unresolved claims retain their original reference and are never replayed.
        var claims = await _db.BillingClaims.Where(x => x.Scope == _scope && x.RemoteId == null)
            .OrderBy(x => x.LastCheckedAt).ThenBy(x => x.Id).Take(100).ToListAsync(ct);
        foreach (var claim in claims)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                claim.LastCheckedAt = DateTimeOffset.UtcNow;
                await _db.SaveChangesAsync(ct);
                if (claim.Kind == "customer") await ReconcileCustomerAsync(claim, ct);
                else await ReconcileSubscriptionAsync(claim, ct);
            }
            catch (MaxioFailure) { _logger.LogWarning("Billing reconciliation remains pending for {ClaimId}", claim.Id); }
        }
    }
}
