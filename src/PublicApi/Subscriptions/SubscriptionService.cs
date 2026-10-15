using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.Infrastructure.Identity;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

public sealed class SubscriptionService(MaxioGateway gateway, ClaimStore claims,
    MaxioSettings settings, ILogger<SubscriptionService> logger)
{
    public Task<PlanList> PlansAsync(CancellationToken ct) => gateway.PlansAsync(ct);

    public async Task<SubscribeResult> SubscribeAsync(ApplicationUser user, SubscribeRequest request, CancellationToken ct)
    {
        var catalog = await gateway.PlansAsync(ct);
        if (catalog.IsTruncated) throw new BillingException(503, "The complete subscription catalog is temporarily unavailable.");
        var plan = catalog.Plans.SingleOrDefault(x => x.Handle == request.ProductHandle);
        if (plan == null) throw new BillingException(400, "Select a plan from subscription-plans.");
        if (plan.RequiresPaymentMethod) throw new BillingException(422, "This plan requires a payment method. Card capture is not available in this flow.");
        if (string.IsNullOrWhiteSpace(user.Email)) throw new BillingException(422, "Your account needs an email address to subscribe.");
        var customer = await EnsureCustomerAsync(user, request, ct);
        if (customer == null) return new("pending", null);
        var reference = Reference(user.Id, "subscription", plan.Handle);
        var subscription = await CreateOnceAsync(reference,
            t => gateway.FindSubscriptionAsync(reference, t),
            t => gateway.CreateSubscriptionAsync(reference, customer.Id!.Value, plan.Handle, t),
            s => s.Id!.Value, ct);
        if (subscription == null) return new("pending", null);
        var view = MaxioGateway.View(subscription, customer.Id!.Value);
        if (view.ProductHandle != plan.Handle) throw new BillingException(502, "Billing returned an unexpected plan.");
        return new("confirmed", view);
    }

    private Task<MaxioAdvancedBilling.Models.Customer?> EnsureCustomerAsync(ApplicationUser user, SubscribeRequest request, CancellationToken ct)
    {
        var reference = Reference(user.Id, "customer");
        return CreateOnceAsync(reference, t => gateway.FindCustomerAsync(reference, t),
            t => gateway.CreateCustomerAsync(reference, user.Email!, request, t), c => c.Id!.Value, ct);
    }

    public async Task<IReadOnlyList<SubscriptionView>> MineAsync(ApplicationUser user, CancellationToken ct)
    {
        var customer = await gateway.FindCustomerAsync(Reference(user.Id, "customer"), ct);
        if (customer == null) return Array.Empty<SubscriptionView>();
        var subscriptions = await gateway.SubscriptionsAsync(customer.Id!.Value, ct);
        return subscriptions.Where(x => x.Product?.ProductFamily?.Handle == settings.ProductFamilyHandle)
            .Select(x => MaxioGateway.View(x, customer.Id.Value)).ToArray();
    }

    // Deterministic site/user/plan keys survive retries and can recover remote records after a demo DB restart.
    public string Reference(string userId, string kind, string handle = "")
    {
        var input = string.Join('\n', settings.BaseUrl ?? settings.Subdomain, userId, kind, handle);
        return "eshop-" + kind + "-" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(input))).ToLowerInvariant();
    }

    private async Task<T?> CreateOnceAsync<T>(string reference, Func<CancellationToken, Task<T?>> lookup,
        Func<CancellationToken, Task<T>> create, Func<T, int> id, CancellationToken ct) where T : class
    {
        var ownsClaim = await claims.TryClaimAsync(reference, ct);
        // A failed lookup never authorizes a create. If this request owns an unsent claim, release it.
        T? existing;
        try { existing = await lookup(ct); }
        catch
        {
            if (ownsClaim) await claims.ReleaseAsync(reference, CancellationToken.None);
            throw;
        }
        if (existing != null)
        {
            await claims.CompleteAsync(reference, id(existing), ct);
            return existing;
        }
        if (!ownsClaim) return null;
        try
        {
            var result = await create(ct);
            await claims.CompleteAsync(reference, id(result), CancellationToken.None);
            return result;
        }
        catch (BillingException ex) when (ex.StatusCode < 500)
        {
            // A definitive refusal is safe to release, but first recover a preexisting remote record.
            var found = await ReconcileAsync(reference, lookup, id);
            if (found != null) return found;
            await claims.ReleaseAsync(reference, CancellationToken.None);
            throw;
        }
        catch (Exception ex) when (ex is BillingException or OperationCanceledException)
        {
            logger.LogWarning("Billing write outcome unknown; reconciling stable reference");
            return await ReconcileAsync(reference, lookup, id);
        }
    }

    private async Task<T?> ReconcileAsync<T>(string reference, Func<CancellationToken, Task<T?>> lookup, Func<T, int> id) where T : class
    {
        try
        {
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            var found = await lookup(deadline.Token);
            if (found != null) await claims.CompleteAsync(reference, id(found), deadline.Token);
            return found;
        }
        catch (Exception ex) when (ex is BillingException or OperationCanceledException)
        {
            logger.LogWarning("Billing reconciliation pending; kind {Kind}", ex.GetType().Name);
            return null;
        }
    }
}
