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
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

public sealed record SubscriptionPlan(string Handle, string Name, string? Description, long PriceInCents, string Currency, int Interval, string IntervalUnit);
public sealed record ShopperSubscription(int Id, string? PlanHandle, string? PlanName, long? PriceInCents, string? Currency, string State, DateTimeOffset? NextBillingDate);
public sealed record SubscribeResult(string Status, ShopperSubscription? Subscription);
public sealed record MySubscriptions(IReadOnlyList<ShopperSubscription> Subscriptions, bool HasPendingOperations);
public sealed class BillingUnavailableException(string message) : Exception(message);
public sealed class BillingRejectedException() : Exception("The billing provider declined the subscription request.");
public sealed class InvalidPlanException() : Exception("Choose a handle returned by subscription-plans.");

public sealed class SubscriptionService(
    MaxioAdvancedBilling.MaxioAdvancedBillingClient client,
    MaxioSettings settings, CatalogContext db, BillingClaimStore claims,
    IHostEnvironment environment, ILogger<SubscriptionService> logger)
{
    public string CustomerReference(string userId) => Reference("customer", userId);

    private string Reference(string kind, params string[] parts)
    {
        // Length-prefix each part to avoid ambiguous concatenation. Includes the target site.
        var values = new[] { settings.BaseUrl ?? settings.Subdomain, kind }.Concat(parts);
        var text = string.Concat(values.Select(value => $"{value.Length}:{value}"));
        return "eshop-" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();
    }

    private async Task<MaxioAdvancedBilling.Models.Site> ReadSiteAsync(CancellationToken ct)
    {
        var site = (await client.Sites.ReadSite(cancellationToken: ct)).Site;
        if (environment.IsDevelopment() && site.Test != true)
            throw new BillingUnavailableException("Development billing requires a Maxio sandbox site.");
        if (string.IsNullOrWhiteSpace(site.Currency)) throw new BillingUnavailableException("Billing site currency is unavailable.");
        return site;
    }

    public async Task<IReadOnlyList<SubscriptionPlan>> PlansAsync(CancellationToken ct)
    {
        var site = await ReadSiteAsync(ct);
        var plans = new List<SubscriptionPlan>();
        for (var page = 1; page <= 50; page++)
        {
            var batch = await client.Products.ListProducts(new MaxioAdvancedBilling.Requests.Products.ListProductsRequest
            {
                Page = page, PerPage = 200, IncludeArchived = false
            }, cancellationToken: ct);
            foreach (var product in batch.Select(x => x.Product))
            {
                if (product.ProductFamily?.Handle != settings.ProductFamilyHandle || product.ProductFamily.ArchivedAt != null ||
                    product.ArchivedAt != null || product.RequireCreditCard != false) continue;
                if (string.IsNullOrWhiteSpace(product.Handle) || string.IsNullOrWhiteSpace(product.Name) ||
                    product.PriceInCents == null || product.Interval is null or <= 0 || product.IntervalUnit == null)
                    throw new BillingUnavailableException("The billing catalog has incomplete plan data.");
                plans.Add(new(product.Handle, product.Name, product.Description, product.PriceInCents.Value,
                    site.Currency!, product.Interval.Value, product.IntervalUnit.Value));
            }
            if (batch.Count < 200) return plans.OrderBy(x => x.PriceInCents).ThenBy(x => x.Handle).ToArray();
        }
        throw new BillingUnavailableException("The billing catalog exceeded its page limit; no partial catalog was returned.");
    }

    public async Task<SubscribeResult> SubscribeAsync(ApplicationUser user, string handle, CancellationToken ct)
    {
        var plans = await PlansAsync(ct);
        if (!plans.Any(x => x.Handle == handle)) throw new InvalidPlanException();
        var customerReference = CustomerReference(user.Id);
        var customerId = await EnsureCustomerAsync(user, customerReference, ct);
        if (customerId == null) return new("pending", null);
        var reference = Reference("subscription", user.Id, handle);
        var existing = await FindSubscriptionAsync(reference, ct);
        if (existing != null)
        {
            VerifyOwner(existing, customerId.Value, handle);
            return new("existing", Map(existing));
        }
        var site = await ReadSiteAsync(ct);
        if (site.RelationshipInvoicingEnabled == null)
            throw new BillingUnavailableException("The billing site's invoicing architecture is unavailable.");
        var claim = new BillingClaim { Id = reference, Kind = "subscription", CustomerReference = customerReference, ProductHandle = handle };
        if (!await claims.TryClaimAsync(claim, ct)) return new("pending", null);
        try
        {
            var created = (await client.Subscriptions.CreateSubscription(new MaxioAdvancedBilling.Requests.Subscriptions.CreateSubscriptionOperationRequest
            {
                Body = new MaxioAdvancedBilling.Models.CreateSubscriptionRequest
                {
                    Subscription = new MaxioAdvancedBilling.Models.CreateSubscription
                    {
                        ProductHandle = handle, CustomerId = customerId.Value, Reference = reference,
                        PaymentCollectionMethod = site.RelationshipInvoicingEnabled == true
                            ? MaxioAdvancedBilling.Models.Enums.CollectionMethod.Remittance
                            : MaxioAdvancedBilling.Models.Enums.CollectionMethod.Invoice
                    }
                }
            }, cancellationToken: ct)).Subscription ?? throw new BillingUnavailableException("Subscription response was incomplete.");
            VerifyOwner(created, customerId.Value, handle);
            var mapped = Map(created);
            await claims.CompleteAsync(claim, mapped.Id, CancellationToken.None);
            logger.LogInformation("Billing subscription completed.");
            return new("created", mapped);
        }
        catch (MaxioAdvancedBilling.Core.Exceptions.ApiException<MaxioAdvancedBilling.Errors.CreateSubscriptionError> ex)
        {
            if (ex.Error.TryGetErrorListResponse1(out var validation))
                logger.LogWarning("Billing subscription rejected with {ErrorCount} validation errors.", validation.Errors.Count);
            else if (ex.Error.TryGetRawError(out var raw))
                logger.LogWarning("Billing subscription returned HTTP {Status}.", (int)raw.StatusCode);
            var recovered = await RecoverSubscriptionAsync(claim, customerId.Value);
            if (recovered != null) return new("existing", Map(recovered));
            if (ex.StatusCode == HttpStatusCode.UnprocessableEntity)
            {
                await claims.ReleaseAsync(claim, CancellationToken.None);
                throw new BillingRejectedException();
            }
            return new("pending", null);
        }
        catch (Exception ex) when (ex is MaxioAdvancedBilling.Core.Exceptions.SdkException or OperationCanceledException or BillingUnavailableException)
        {
            logger.LogWarning("Billing subscription outcome requires reconciliation ({FailureType}).", ex.GetType().Name);
            var recovered = await RecoverSubscriptionAsync(claim, customerId.Value);
            return recovered == null ? new("pending", null) : new("existing", Map(recovered));
        }
    }

    private async Task<int?> EnsureCustomerAsync(ApplicationUser user, string reference, CancellationToken ct)
    {
        var existing = await FindCustomerAsync(reference, ct);
        if (existing != null) return existing.Id ?? throw new BillingUnavailableException("Customer ID missing.");
        if (string.IsNullOrWhiteSpace(user.Email)) throw new BillingUnavailableException("The account requires an email address for billing.");
        var claim = new BillingClaim { Id = reference, Kind = "customer", CustomerReference = reference };
        if (!await claims.TryClaimAsync(claim, ct)) return null;
        try
        {
            var customer = (await client.Customers.CreateCustomer(new MaxioAdvancedBilling.Requests.Customers.CreateCustomerOperationRequest
            {
                Body = new MaxioAdvancedBilling.Models.CreateCustomerRequest
                {
                    Customer = new MaxioAdvancedBilling.Models.CreateCustomer
                    {
                        FirstName = "eShop", LastName = "Shopper", Email = user.Email, Reference = reference
                    }
                }
            }, cancellationToken: ct)).Customer;
            var id = customer.Id ?? throw new BillingUnavailableException("Customer ID missing.");
            if (customer.Reference != reference) throw new BillingUnavailableException("Customer reference mismatch.");
            await claims.CompleteAsync(claim, id, CancellationToken.None);
            return id;
        }
        catch (MaxioAdvancedBilling.Core.Exceptions.ApiException<MaxioAdvancedBilling.Errors.CreateCustomerError> ex)
        {
            if (ex.Error.TryGetCustomerErrorResponse1(out _)) logger.LogWarning("Billing customer validation rejected.");
            else if (ex.Error.TryGetRawError(out var raw)) logger.LogWarning("Billing customer returned HTTP {Status}.", (int)raw.StatusCode);
            var recovered = await RecoverCustomerAsync(claim);
            if (recovered != null) return recovered;
            if (ex.StatusCode == HttpStatusCode.UnprocessableEntity)
            {
                await claims.ReleaseAsync(claim, CancellationToken.None);
                throw new BillingRejectedException();
            }
            return null;
        }
        catch (Exception ex) when (ex is MaxioAdvancedBilling.Core.Exceptions.SdkException or OperationCanceledException or BillingUnavailableException)
        {
            logger.LogWarning("Billing customer outcome requires reconciliation ({FailureType}).", ex.GetType().Name);
            return await RecoverCustomerAsync(claim);
        }
    }

    private async Task<MaxioAdvancedBilling.Models.Customer?> FindCustomerAsync(string reference, CancellationToken ct)
    {
        try
        {
            var customer = (await client.Customers.ReadCustomerByReference(new MaxioAdvancedBilling.Requests.Customers.ReadCustomerByReferenceRequest
            { Reference = reference }, cancellationToken: ct)).Customer;
            if (customer.Reference != reference || customer.Id == null) throw new BillingUnavailableException("Customer lookup returned incomplete or mismatched data.");
            return customer;
        }
        catch (MaxioAdvancedBilling.Core.Exceptions.ApiException<MaxioAdvancedBilling.Core.ErrorResponse.RawError> ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        { return null; }
    }

    private async Task<MaxioAdvancedBilling.Models.Subscription?> FindSubscriptionAsync(string reference, CancellationToken ct)
    {
        try
        {
            var subscription = (await client.Subscriptions.FindSubscription(new MaxioAdvancedBilling.Requests.Subscriptions.FindSubscriptionRequest
            { Reference = reference }, cancellationToken: ct)).Subscription;
            if (subscription == null || subscription.Reference != reference) throw new BillingUnavailableException("Subscription lookup returned incomplete or mismatched data.");
            return subscription;
        }
        catch (MaxioAdvancedBilling.Core.Exceptions.ApiException<MaxioAdvancedBilling.Errors.FindSubscriptionError> ex)
        {
            if (ex.Error.TryGetNoContent(out _)) return null;
            if (ex.Error.TryGetRawError(out var raw)) logger.LogWarning("Billing subscription lookup returned HTTP {Status}.", (int)raw.StatusCode);
            throw;
        }
    }

    private async Task<int?> RecoverCustomerAsync(BillingClaim claim)
    {
        using var recovery = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        try
        {
            var customer = await FindCustomerAsync(claim.CustomerReference, recovery.Token);
            if (customer?.Id is not int id) return null;
            await claims.CompleteAsync(claim, id, recovery.Token);
            return id;
        }
        catch (Exception ex) when (ex is MaxioAdvancedBilling.Core.Exceptions.SdkException or OperationCanceledException or BillingUnavailableException)
        { logger.LogWarning("Customer reconciliation remains pending ({FailureType}).", ex.GetType().Name); return null; }
    }

    private async Task<MaxioAdvancedBilling.Models.Subscription?> RecoverSubscriptionAsync(BillingClaim claim, int customerId)
    {
        using var recovery = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        try
        {
            var subscription = await FindSubscriptionAsync(claim.Id, recovery.Token);
            if (subscription == null) return null;
            VerifyOwner(subscription, customerId, claim.ProductHandle!);
            var mapped = Map(subscription);
            await claims.CompleteAsync(claim, mapped.Id, recovery.Token);
            return subscription;
        }
        catch (Exception ex) when (ex is MaxioAdvancedBilling.Core.Exceptions.SdkException or OperationCanceledException or BillingUnavailableException)
        { logger.LogWarning("Subscription reconciliation remains pending ({FailureType}).", ex.GetType().Name); return null; }
    }

    public async Task<MySubscriptions> MineAsync(ApplicationUser user, CancellationToken ct)
    {
        await ReadSiteAsync(ct);
        var reference = CustomerReference(user.Id);
        var customer = await FindCustomerAsync(reference, ct);
        var subscriptions = new List<ShopperSubscription>();
        if (customer?.Id is int id)
        {
            var results = await client.Customers.ListCustomerSubscriptions(new MaxioAdvancedBilling.Requests.Customers.ListCustomerSubscriptionsRequest
            { CustomerId = id }, cancellationToken: ct);
            foreach (var result in results)
            {
                var subscription = result.Subscription ?? throw new BillingUnavailableException("Subscription data missing.");
                if (subscription.Customer?.Id != id) throw new BillingUnavailableException("Subscription owner mismatch.");
                if (subscription.Product?.ProductFamily?.Handle == settings.ProductFamilyHandle) subscriptions.Add(Map(subscription));
            }
        }
        var pending = await db.BillingClaims.AnyAsync(x => x.CustomerReference == reference && x.ProviderId == null, ct);
        return new(subscriptions, pending);
    }

    public async Task ReconcilePendingAsync(CancellationToken ct)
    {
        await ReadSiteAsync(ct);
        // Bound each sweep; old pending claims remain visible and are never resent.
        var pending = await db.BillingClaims.Where(x => x.ProviderId == null)
            .OrderBy(x => x.LastCheckedAt).ThenBy(x => x.CreatedAt).Take(100).ToListAsync(ct);
        foreach (var claim in pending)
        {
            ct.ThrowIfCancellationRequested();
            claim.LastCheckedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(ct);
            if (claim.Kind == "customer") await RecoverCustomerAsync(claim);
            else
            {
                var customer = await FindCustomerAsync(claim.CustomerReference, ct);
                if (customer?.Id is int id) await RecoverSubscriptionAsync(claim, id);
            }
        }
    }

    private static void VerifyOwner(MaxioAdvancedBilling.Models.Subscription subscription, int customerId, string handle)
    {
        if (subscription.Customer?.Id != customerId || subscription.Product?.Handle != handle)
            throw new BillingUnavailableException("Subscription owner or plan mismatch.");
    }

    private static ShopperSubscription Map(MaxioAdvancedBilling.Models.Subscription subscription)
    {
        if (subscription.Id == null || subscription.State == null || subscription.Product == null || subscription.ProductPriceInCents == null)
            throw new BillingUnavailableException("Subscription response was incomplete.");
        return new(subscription.Id.Value, subscription.Product.Handle, subscription.Product.Name, subscription.ProductPriceInCents,
            subscription.Currency, subscription.State.Value, subscription.NextAssessmentAt);
    }
}
