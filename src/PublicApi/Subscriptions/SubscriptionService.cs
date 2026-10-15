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

public sealed class SubscriptionService(
    MaxioAdvancedBilling.MaxioAdvancedBillingClient client, MaxioSettings settings,
    BillingClaimStore claims, CatalogContext db, ILogger<SubscriptionService> logger)
{
    public static string CustomerReference(string userId) => "eshop-c-" + Hash(userId);
    public static string SubscriptionReference(string userId, string handle) => "eshop-s-" + Hash(userId + "\n" + handle);
    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    public Task<IReadOnlyList<SubscriptionPlan>> GetPlansAsync(CancellationToken ct) => BoundedAsync(async token =>
        (IReadOnlyList<SubscriptionPlan>)(await OfferedProductsAsync(token)).Select(p => new SubscriptionPlan(
            p.Handle!, p.Name!, p.Description, p.PriceInCents!.Value, p.Interval!.Value, p.IntervalUnit!.Value)).ToArray(), ct);

    private async Task<List<MaxioAdvancedBilling.Models.Product>> OfferedProductsAsync(CancellationToken ct)
    {
        var products = new List<MaxioAdvancedBilling.Models.Product>();
        for (var page = 1; page <= 50; page++)
        {
            var batch = await client.Products.ListProducts(new MaxioAdvancedBilling.Requests.Products.ListProductsRequest
            {
                Page = page, PerPage = 200, IncludeArchived = false
            }, cancellationToken: ct);
            if (batch == null || batch.Any(x => x?.Product == null)) throw Unavailable();
            foreach (var item in batch)
            {
                var p = item.Product;
                if (p.ProductFamily?.Handle != settings.ProductFamilyHandle || p.ArchivedAt != null ||
                    p.ProductFamily.ArchivedAt != null || p.RequireCreditCard != false) continue;
                if (string.IsNullOrWhiteSpace(p.Handle) || string.IsNullOrWhiteSpace(p.Name) || p.PriceInCents == null ||
                    p.PriceInCents < 0 || p.Interval is null or <= 0 || p.IntervalUnit == null) throw Unavailable();
                products.Add(p);
            }
            if (batch.Count < 200) return products;
        }
        throw new BillingException("The billing catalog exceeded the supported page limit. No partial catalog was returned.");
    }

    public Task<SubscriptionEnrollment> SubscribeAsync(ApplicationUser user, SubscribeRequest request, CancellationToken ct) =>
        BoundedAsync(async token =>
        {
            if (string.IsNullOrWhiteSpace(request.ProductHandle) || request.ProductHandle.Length > 255 ||
                string.IsNullOrWhiteSpace(request.FirstName) || request.FirstName.Length > 100 ||
                string.IsNullOrWhiteSpace(request.LastName) || request.LastName.Length > 100)
                throw new BillingException("Product handle, first name and last name are required.", 400);
            if (string.IsNullOrWhiteSpace(user.Email)) throw new BillingException("Your account needs an email address before subscribing.", 400);
            var offered = await OfferedProductsAsync(token);
            if (!offered.Any(x => x.Handle == request.ProductHandle))
                throw new BillingException("Select a currently available subscription plan.", 400);

            var customer = await EnsureCustomerAsync(user, request, token);
            if (customer == null) return new SubscriptionEnrollment("pending", null);
            var claim = NewClaim(user.Id, "subscription", SubscriptionReference(user.Id, request.ProductHandle), request.ProductHandle);
            if (!await claims.TryClaimAsync(claim, token))
            {
                var prior = await db.BillingClaims.AsNoTracking().SingleAsync(x => x.Id == claim.Id, token);
                if (prior.ProviderId == null) return new SubscriptionEnrollment("pending", null);
                var priorSubscription = await FindSubscriptionAsync(claim, token);
                return Enrollment(priorSubscription);
            }
            // Restore provider state after local mapping loss, while owning the claim.
            MaxioAdvancedBilling.Models.Subscription? existing;
            try { existing = await FindSubscriptionAsync(claim, token); }
            catch { await ReleaseAsync(claim); throw; } // No provider write has been attempted yet.
            if (existing != null)
            {
                if (Settled(existing)) await claims.CompleteAsync(claim.Id, existing.Id!.Value, token);
                return Enrollment(existing);
            }
            try
            {
                var response = await client.Subscriptions.CreateSubscription(
                    new MaxioAdvancedBilling.Requests.Subscriptions.CreateSubscriptionOperationRequest
                    {
                        Body = new MaxioAdvancedBilling.Models.CreateSubscriptionRequest
                        {
                            Subscription = new MaxioAdvancedBilling.Models.CreateSubscription
                            {
                                ProductHandle = request.ProductHandle, CustomerId = customer.Id, Reference = claim.Reference,
                                PaymentCollectionMethod = MaxioAdvancedBilling.Models.Enums.CollectionMethod.Remittance
                            }
                        }
                    }, cancellationToken: token);
                var subscription = ValidateSubscription(response.Subscription, claim);
                if (Settled(subscription)) await claims.CompleteAsync(claim.Id, subscription.Id!.Value, token);
                return Enrollment(subscription);
            }
            catch (MaxioAdvancedBilling.Core.Exceptions.ApiException<MaxioAdvancedBilling.Errors.CreateSubscriptionError> ex)
            {
                if (ex.Error.TryGetErrorListResponse1(out var error))
                    logger.LogWarning("Subscription enrollment refused; validation messages: {Count}", error.Errors.Count);
                else if (ex.Error.TryGetRawError(out var raw))
                    logger.LogWarning("Subscription enrollment returned HTTP {Status}", (int)raw.StatusCode);
                if (DefiniteRefusal(ex.StatusCode))
                {
                    await ReleaseAsync(claim);
                    throw new BillingException("Billing declined the subscription request.", ex.StatusCode == HttpStatusCode.UnprocessableEntity ? 422 : 503);
                }
                return await RecoverSubscriptionAsync(claim);
            }
            catch (MaxioAdvancedBilling.Core.Exceptions.ResponseDeserializationException ex) when (DefiniteRefusal(ex.StatusCode))
            {
                await ReleaseAsync(claim);
                throw new BillingException("Billing declined the subscription request.", ex.StatusCode == HttpStatusCode.UnprocessableEntity ? 422 : 503);
            }
            catch (Exception ex) when (IsUncertain(ex))
            {
                return await RecoverSubscriptionAsync(claim);
            }
        }, ct);

    private async Task<MaxioAdvancedBilling.Models.Customer?> EnsureCustomerAsync(ApplicationUser user, SubscribeRequest request, CancellationToken ct)
    {
        var reference = CustomerReference(user.Id);
        var existing = await FindCustomerAsync(reference, ct);
        if (existing != null) return existing;
        var claim = NewClaim(user.Id, "customer", reference);
        if (!await claims.TryClaimAsync(claim, ct)) return await FindCustomerAsync(reference, ct);
        try
        {
            var response = await client.Customers.CreateCustomer(new MaxioAdvancedBilling.Requests.Customers.CreateCustomerOperationRequest
            {
                Body = new MaxioAdvancedBilling.Models.CreateCustomerRequest
                {
                    Customer = new MaxioAdvancedBilling.Models.CreateCustomer
                    {
                        FirstName = request.FirstName.Trim(), LastName = request.LastName.Trim(), Email = user.Email!, Reference = reference
                    }
                }
            }, cancellationToken: ct);
            var customer = ValidateCustomer(response.Customer, reference);
            await claims.CompleteAsync(claim.Id, customer.Id!.Value, ct);
            return customer;
        }
        catch (MaxioAdvancedBilling.Core.Exceptions.ApiException<MaxioAdvancedBilling.Errors.CreateCustomerError> ex)
        {
            if (ex.Error.TryGetCustomerErrorResponse1(out _)) logger.LogWarning("Customer enrollment refused validation.");
            else if (ex.Error.TryGetRawError(out var raw)) logger.LogWarning("Customer enrollment returned HTTP {Status}", (int)raw.StatusCode);
            // A unique-reference conflict may be an earlier successful enrollment.
            var recovered = await RecoverCustomerAsync(claim);
            if (recovered != null) return recovered;
            if (DefiniteRefusal(ex.StatusCode))
            {
                await ReleaseAsync(claim);
                throw new BillingException("Billing declined the customer details.", ex.StatusCode == HttpStatusCode.UnprocessableEntity ? 422 : 503);
            }
            return null;
        }
        catch (MaxioAdvancedBilling.Core.Exceptions.ResponseDeserializationException ex) when (DefiniteRefusal(ex.StatusCode))
        {
            await ReleaseAsync(claim);
            throw new BillingException("Billing declined the customer details.", 422);
        }
        catch (Exception ex) when (IsUncertain(ex)) { return await RecoverCustomerAsync(claim); }
    }

    public Task<MySubscriptions> GetMineAsync(ApplicationUser user, CancellationToken ct) => BoundedAsync(async token =>
    {
        var customer = await FindCustomerAsync(CustomerReference(user.Id), token);
        var pending = await db.BillingClaims.AsNoTracking().AnyAsync(x => x.Site == settings.Site && x.UserId == user.Id && x.ProviderId == null, token);
        if (customer == null) return new MySubscriptions(Array.Empty<SubscriptionDetails>(), pending);
        var response = await client.Customers.ListCustomerSubscriptions(
            new MaxioAdvancedBilling.Requests.Customers.ListCustomerSubscriptionsRequest { CustomerId = customer.Id!.Value }, cancellationToken: token);
        if (response == null || response.Any(x => x.Subscription == null || x.Subscription.Customer?.Id != customer.Id)) throw Unavailable();
        var result = response.Select(x => x.Subscription!).Where(x => x.Product?.ProductFamily?.Handle == settings.ProductFamilyHandle)
            .Select(Details).ToArray();
        return new MySubscriptions(result, pending);
    }, ct);

    private BillingClaim NewClaim(string userId, string kind, string reference, string? handle = null) => new()
    {
        Id = Hash(settings.Site + "\n" + kind + "\n" + reference), UserId = userId, Site = settings.Site,
        Kind = kind, Reference = reference, ProductHandle = handle
    };

    private async Task<MaxioAdvancedBilling.Models.Customer?> FindCustomerAsync(string reference, CancellationToken ct)
    {
        try
        {
            var response = await client.Customers.ReadCustomerByReference(
                new MaxioAdvancedBilling.Requests.Customers.ReadCustomerByReferenceRequest { Reference = reference }, cancellationToken: ct);
            return ValidateCustomer(response.Customer, reference);
        }
        catch (MaxioAdvancedBilling.Core.Exceptions.ApiException<MaxioAdvancedBilling.Core.ErrorResponse.RawError> ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        { return null; }
    }

    private static MaxioAdvancedBilling.Models.Customer ValidateCustomer(MaxioAdvancedBilling.Models.Customer? customer, string reference)
    {
        if (customer?.Id is null or <= 0 || customer.Reference != reference) throw Unavailable();
        return customer;
    }

    private async Task<MaxioAdvancedBilling.Models.Subscription?> FindSubscriptionAsync(BillingClaim claim, CancellationToken ct)
    {
        try
        {
            var response = await client.Subscriptions.FindSubscription(
                new MaxioAdvancedBilling.Requests.Subscriptions.FindSubscriptionRequest { Reference = claim.Reference }, cancellationToken: ct);
            return ValidateSubscription(response.Subscription, claim);
        }
        catch (MaxioAdvancedBilling.Core.Exceptions.ApiException<MaxioAdvancedBilling.Errors.FindSubscriptionError> ex)
        {
            if (ex.Error.TryGetNoContent(out _)) return null;
            if (ex.Error.TryGetRawError(out var raw)) logger.LogWarning("Subscription lookup returned HTTP {Status}", (int)raw.StatusCode);
            throw;
        }
    }

    private static MaxioAdvancedBilling.Models.Subscription ValidateSubscription(MaxioAdvancedBilling.Models.Subscription? subscription, BillingClaim claim)
    {
        if (subscription?.Id is null or <= 0 || subscription.Reference != claim.Reference ||
            subscription.Customer?.Reference != CustomerReference(claim.UserId) || subscription.Product?.Handle != claim.ProductHandle)
            throw Unavailable();
        _ = Details(subscription);
        return subscription;
    }

    private static SubscriptionDetails Details(MaxioAdvancedBilling.Models.Subscription subscription)
    {
        if (subscription.Id is null or <= 0 || string.IsNullOrWhiteSpace(subscription.Product?.Handle) ||
            string.IsNullOrWhiteSpace(subscription.Product.Name) || subscription.ProductPriceInCents == null || subscription.State == null)
            throw Unavailable();
        return new SubscriptionDetails(subscription.Id.Value, subscription.Product.Handle, subscription.Product.Name,
            subscription.ProductPriceInCents.Value, subscription.Currency, subscription.State.Value,
            subscription.CurrentPeriodEndsAt ?? subscription.NextAssessmentAt, subscription.Product.Interval, subscription.Product.IntervalUnit?.Value);
    }

    private async Task<MaxioAdvancedBilling.Models.Customer?> RecoverCustomerAsync(BillingClaim claim)
    {
        using var reserve = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        try
        {
            var customer = await FindCustomerAsync(claim.Reference, reserve.Token);
            if (customer != null) await claims.CompleteAsync(claim.Id, customer.Id!.Value, reserve.Token);
            return customer;
        }
        catch (Exception ex) when (IsUncertain(ex)) { logger.LogWarning("Customer enrollment remains pending reconciliation."); return null; }
    }

    private async Task<SubscriptionEnrollment> RecoverSubscriptionAsync(BillingClaim claim)
    {
        using var reserve = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        try
        {
            var subscription = await FindSubscriptionAsync(claim, reserve.Token);
            if (subscription != null)
            {
                if (Settled(subscription)) await claims.CompleteAsync(claim.Id, subscription.Id!.Value, reserve.Token);
                return Enrollment(subscription);
            }
        }
        catch (Exception ex) when (IsUncertain(ex)) { logger.LogWarning("Subscription enrollment remains pending reconciliation."); }
        return new SubscriptionEnrollment("pending", null);
    }

    private async Task ReleaseAsync(BillingClaim claim)
    {
        using var reserve = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await claims.ReleaseAsync(claim.Id, reserve.Token);
    }

    // Background recovery never writes to the provider. An absence after a lost response is not permission to re-send.
    public async Task ReconcileAsync(BillingClaim claim, CancellationToken ct)
    {
        var subscription = claim.Kind == "subscription" ? await FindSubscriptionAsync(claim, ct) : null;
        int? id = claim.Kind == "customer" ? (await FindCustomerAsync(claim.Reference, ct))?.Id :
            subscription != null && Settled(subscription) ? subscription.Id : null;
        if (id != null) await claims.CompleteAsync(claim.Id, id.Value, ct);
        else if (claim.CreatedAt < DateTimeOffset.UtcNow.AddHours(-1))
            logger.LogError("Billing claim {ClaimId} requires operator reconciliation; no repeat write was sent.", claim.Id);
    }

    private async Task<T> BoundedAsync<T>(Func<CancellationToken, Task<T>> action, CancellationToken ct)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(TimeSpan.FromSeconds(45));
        try { return await action(deadline.Token); }
        catch (MaxioAdvancedBilling.Core.Exceptions.SdkException ex)
        {
            logger.LogWarning("Billing operation unavailable: {FailureType}", ex.GetType().Name);
            throw Unavailable();
        }
        catch (OperationCanceledException) { throw new BillingException("Billing timed out. Check your subscriptions before trying again.", 503); }
        catch (BillingException) { throw; }
        catch (Exception ex)
        {
            logger.LogError("Billing workflow failed: {FailureType}", ex.GetType().Name);
            throw Unavailable();
        }
    }

    private static bool DefiniteRefusal(HttpStatusCode status) => (int)status >= 400 && (int)status < 500 && status != HttpStatusCode.RequestTimeout;
    private static bool Settled(MaxioAdvancedBilling.Models.Subscription subscription) =>
        subscription.State != MaxioAdvancedBilling.Models.Enums.SubscriptionState.Pending &&
        subscription.State != MaxioAdvancedBilling.Models.Enums.SubscriptionState.Assessing &&
        subscription.State != MaxioAdvancedBilling.Models.Enums.SubscriptionState.FailedToCreate;
    private static SubscriptionEnrollment Enrollment(MaxioAdvancedBilling.Models.Subscription? subscription) =>
        subscription == null || !Settled(subscription) ? new("pending", null) : new("confirmed", Details(subscription));
    private static bool IsUncertain(Exception ex) => ex is MaxioAdvancedBilling.Core.Exceptions.SdkException or OperationCanceledException or BillingException or DbUpdateException;
    private static BillingException Unavailable() => new("Billing is temporarily unavailable. Please check your subscriptions shortly.");
}
