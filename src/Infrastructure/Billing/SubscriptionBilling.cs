using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using MaxioAdvancedBilling.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.Infrastructure.Billing;

public sealed class SubscriptionBilling(
    MaxioGateway gateway, BillingClaimStore claims, CatalogContext db, MaxioOptions options,
    ILogger<SubscriptionBilling> logger) : ISubscriptionBilling
{
    private const int MaxPages = 100;
    private readonly string _scope = Hash(options.BaseUrl ?? options.Subdomain, Environment.GetEnvironmentVariable("MAXIO_ENVIRONMENT") ?? "US");

    public Task<BillingPage<SubscriptionPlan>> GetPlansAsync(CancellationToken ct) => Bounded(GetPlansCoreAsync, ct);

    private async Task<BillingPage<SubscriptionPlan>> GetPlansCoreAsync(CancellationToken ct)
    {
        var plans = new List<SubscriptionPlan>();
        for (var page = 1; page <= MaxPages; page++)
        {
            var batch = await gateway.ProductsAsync(options.ProductFamilyHandle, page, ct);
            foreach (var envelope in batch)
            {
                var product = envelope.Product;
                if (product == null || product.ProductFamily?.Handle != options.ProductFamilyHandle) throw new ProviderFault(null, false);
                if (Eligible(product)) plans.Add(new SubscriptionPlan(product.Handle!, product.Name!, product.Description,
                    product.PriceInCents!.Value, product.Interval!.Value, product.IntervalUnit!.Value));
            }
            if (batch.Count < 200) return new BillingPage<SubscriptionPlan>(plans);
        }
        return new BillingPage<SubscriptionPlan>(plans, true, MaxPages + 1);
    }

    private static bool Eligible(Product p) => p.ArchivedAt == null && p.Interval > 0 && p.PriceInCents >= 0
        && !string.IsNullOrWhiteSpace(p.Handle) && !string.IsNullOrWhiteSpace(p.Name)
        && p.IntervalUnit?.IsKnownValue() == true && p.RequireCreditCard == false
        && p.RequireBillingAddress != true && p.RequireShippingAddress != true;

    public Task<EnrollmentResult> SubscribeAsync(string userId, string email, string productHandle, CancellationToken ct) => Bounded(async token =>
    {
        if (string.IsNullOrWhiteSpace(productHandle) || productHandle.Length > 255)
            throw new BillingException(400, "A valid productHandle is required. Choose a handle from subscription-plans.");
        if (string.IsNullOrWhiteSpace(email) || !email.Contains('@') || email.StartsWith('@'))
            throw new BillingException(422, "Your account needs a valid email address before subscribing.");
        var plans = await GetPlansCoreAsync(token);
        if (plans.IsPartial) throw new BillingException(503, "The plan catalog is incomplete. Please try again later.");
        if (!plans.Items.Any(x => x.Handle == productHandle)) throw new BillingException(400, "This plan is not available for subscription.");

        var customerClaim = NewClaim(userId, "Customer");
        var customer = await EnsureCustomerAsync(customerClaim, email, token);
        if (customer == null) return new EnrollmentResult("pending", null);
        var subscriptionClaim = NewClaim(userId, "Subscription", productHandle, customer.Id);
        if (!await claims.TryClaimAsync(subscriptionClaim, token))
        {
            var found = await gateway.SubscriptionAsync(subscriptionClaim.Id, token);
            return found == null ? new EnrollmentResult("pending", null)
                : new EnrollmentResult("confirmed", MapSubscription(found, customer.Id!.Value, productHandle));
        }
        try
        {
            // Reconcile even a brand-new local claim: the development database may have restarted.
            var existing = await gateway.SubscriptionAsync(subscriptionClaim.Id, token);
            if (existing != null)
            {
                var dto = MapSubscription(existing, customer.Id!.Value, productHandle);
                await claims.CompleteAsync(subscriptionClaim, dto.Id, token);
                return new EnrollmentResult("confirmed", dto);
            }
            token.ThrowIfCancellationRequested();
            await claims.BeginDispatchAsync(subscriptionClaim, token);
        }
        catch
        {
            await claims.ReleaseAsync(subscriptionClaim, CancellationToken.None); // No create was sent.
            throw;
        }
        try
        {
            var created = await gateway.CreateSubscriptionAsync(subscriptionClaim.Id, customer.Id!.Value, productHandle, token);
            var dto = MapSubscription(created, customer.Id.Value, productHandle);
            await claims.CompleteAsync(subscriptionClaim, dto.Id, CancellationToken.None);
            logger.LogInformation("Subscription enrollment confirmed.");
            return new EnrollmentResult("confirmed", dto, true);
        }
        catch (Exception ex) when (ex is ProviderFault or OperationCanceledException)
        {
            var found = await SettleSubscriptionAsync(subscriptionClaim);
            if (found != null) return new EnrollmentResult("confirmed", found);
            if (ex is ProviderFault { Unknown: false } fault)
            {
                await claims.ReleaseAsync(subscriptionClaim, CancellationToken.None);
                throw ToBillingException(fault);
            }
            await claims.UnknownAsync(subscriptionClaim, CancellationToken.None);
            logger.LogWarning("Subscription enrollment outcome is unknown; retained claim for reconciliation.");
            return new EnrollmentResult("pending", null);
        }
    }, ct);

    private async Task<Customer?> EnsureCustomerAsync(BillingClaim claim, string email, CancellationToken ct)
    {
        if (!await claims.TryClaimAsync(claim, ct)) return await gateway.CustomerAsync(claim.Id, ct);
        try
        {
            var existing = await gateway.CustomerAsync(claim.Id, ct);
            if (existing != null)
            {
                await claims.CompleteAsync(claim, existing.Id!.Value, ct);
                return existing;
            }
            ct.ThrowIfCancellationRequested();
            await claims.BeginDispatchAsync(claim, ct);
        }
        catch { await claims.ReleaseAsync(claim, CancellationToken.None); throw; }
        try
        {
            var created = await gateway.CreateCustomerAsync(claim.Id, email, ct);
            await claims.CompleteAsync(claim, created.Id!.Value, CancellationToken.None);
            return created;
        }
        catch (Exception ex) when (ex is ProviderFault or OperationCanceledException)
        {
            using var recovery = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            try
            {
                var found = await gateway.CustomerAsync(claim.Id, recovery.Token);
                if (found != null)
                {
                    await claims.CompleteAsync(claim, found.Id!.Value, CancellationToken.None);
                    return found;
                }
            }
            catch (Exception recoveryError) when (recoveryError is ProviderFault or OperationCanceledException) { }
            if (ex is ProviderFault { Unknown: false } fault)
            {
                await claims.ReleaseAsync(claim, CancellationToken.None);
                throw ToBillingException(fault);
            }
            await claims.UnknownAsync(claim, CancellationToken.None);
            logger.LogWarning("Customer creation outcome is unknown; retained claim for reconciliation.");
            return null;
        }
    }

    public Task<AccountSubscriptions> GetMineAsync(string userId, CancellationToken ct) => Bounded(async token =>
    {
        var reference = NewClaim(userId, "Customer").Id;
        var customer = await gateway.CustomerAsync(reference, token);
        var pending = await db.BillingClaims.AsNoTracking().AnyAsync(x => x.Scope == _scope && x.UserId == userId && x.State != "Complete", token);
        var items = new List<ShopperSubscription>();
        if (customer == null) return new AccountSubscriptions(new BillingPage<ShopperSubscription>(items), pending);
        for (var page = 1; page <= MaxPages; page++)
        {
            var batch = await gateway.SubscriptionsAsync(customer.Id!.Value, page, token);
            foreach (var envelope in batch)
            {
                var subscription = envelope.Subscription;
                if (subscription?.Customer?.Id != customer.Id) throw new ProviderFault(null, false);
                if (subscription.Product?.ProductFamily?.Handle == options.ProductFamilyHandle)
                    items.Add(MapSubscription(subscription, customer.Id.Value));
            }
            if (batch.Count < 200) return new AccountSubscriptions(new BillingPage<ShopperSubscription>(items), pending);
        }
        return new AccountSubscriptions(new BillingPage<ShopperSubscription>(items, true, MaxPages + 1), pending);
    }, ct);

    public Task ReconcileAsync(CancellationToken ct) => Bounded(async token =>
    {
        var cutoff = DateTimeOffset.UtcNow.AddMinutes(-2);
        // Bounded batch; oldest-first makes progress through a persistent backlog.
        var work = await db.BillingClaims.Where(x => x.Scope == _scope && x.State != "Complete" && x.UpdatedAt < cutoff)
            .OrderBy(x => x.UpdatedAt).Take(25).ToListAsync(token);
        foreach (var claim in work)
        {
            if (claim.State == "Reserved")
            {
                // No create can run without a persisted Dispatching transition. Abandoned reservations are safe to release.
                await claims.ReleaseAsync(claim, token);
                continue;
            }
            if (claim.Kind == "Customer")
            {
                var customer = await gateway.CustomerAsync(claim.Id, token);
                if (customer != null) await claims.CompleteAsync(claim, customer.Id!.Value, token);
                else await claims.UnknownAsync(claim, token);
            }
            else
            {
                var found = await gateway.SubscriptionAsync(claim.Id, token);
                if (found != null)
                {
                    var dto = MapSubscription(found, claim.CustomerId!.Value, claim.ProductHandle);
                    await claims.CompleteAsync(claim, dto.Id, token);
                }
                else await claims.UnknownAsync(claim, token);
            }
        }
        return true;
    }, ct);

    private async Task<ShopperSubscription?> SettleSubscriptionAsync(BillingClaim claim)
    {
        using var recovery = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        try
        {
            var found = await gateway.SubscriptionAsync(claim.Id, recovery.Token);
            if (found == null) return null;
            var dto = MapSubscription(found, claim.CustomerId!.Value, claim.ProductHandle);
            await claims.CompleteAsync(claim, dto.Id, CancellationToken.None);
            return dto;
        }
        catch (Exception ex) when (ex is ProviderFault or OperationCanceledException) { return null; }
    }

    private ShopperSubscription MapSubscription(Subscription s, int customerId, string? expectedHandle = null)
    {
        if (s.Id is not > 0 || s.Customer?.Id != customerId || s.Product?.ProductFamily?.Handle != options.ProductFamilyHandle
            || string.IsNullOrWhiteSpace(s.Product.Handle) || string.IsNullOrWhiteSpace(s.Product.Name)
            || (expectedHandle != null && s.Product.Handle != expectedHandle) || s.ProductPriceInCents == null
            || s.State == null || string.IsNullOrWhiteSpace(s.Currency) || s.Product.Interval is not > 0 || s.Product.IntervalUnit == null)
            throw new ProviderFault(null, true);
        return new ShopperSubscription(s.Id.Value, s.Product.Handle, s.Product.Name, s.ProductPriceInCents.Value,
            s.Currency, s.State.Value, s.NextAssessmentAt, s.Product.Interval.Value, s.Product.IntervalUnit.Value);
    }

    private BillingClaim NewClaim(string userId, string kind, string? handle = null, int? customerId = null) => new()
    {
        Id = Hash(_scope, userId, kind, kind == "Subscription" ? options.ProductFamilyHandle : "", handle ?? ""),
        Scope = _scope, UserId = userId, Kind = kind, ProductHandle = handle, CustomerId = customerId
    };

    private static string Hash(params string[] parts) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(parts)))).ToLowerInvariant();

    private static BillingException ToBillingException(ProviderFault ex) => (int?)ex.StatusCode switch
    {
        400 or 409 or 422 => new BillingException((int)ex.StatusCode.Value, "The billing system rejected enrollment. Please check the selected plan and account details."),
        429 => new BillingException(503, "Billing is busy. Please try again shortly."),
        _ => new BillingException(502, "The billing system is temporarily unavailable.")
    };

    private static async Task<T> Bounded<T>(Func<CancellationToken, Task<T>> action, CancellationToken ct)
    {
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(ct);
        budget.CancelAfter(TimeSpan.FromSeconds(30));
        try { return await action(budget.Token); }
        catch (ProviderFault ex) { throw ToBillingException(ex); }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        { throw new BillingException(503, "Billing took too long. Please try again shortly."); }
    }
}
