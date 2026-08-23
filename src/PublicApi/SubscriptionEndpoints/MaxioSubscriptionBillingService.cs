using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using MaxioAdvancedBilling.Core.ErrorResponse;
using MaxioAdvancedBilling.Core.Exceptions;
using MaxioAdvancedBilling.Errors;
using MaxioAdvancedBilling.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.Infrastructure.Identity;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public interface ISubscriptionBillingService
{
    Task<IReadOnlyList<SubscriptionPlanDto>> GetPlansAsync(CancellationToken cancellationToken);
    Task<CreateSubscriptionResponse> SubscribeAsync(BillingUser user, string productHandle, CancellationToken cancellationToken);
    Task<IReadOnlyList<SubscriptionDto>> GetSubscriptionsAsync(BillingUser user, CancellationToken cancellationToken);
}

public sealed class MaxioSubscriptionBillingService(
    MaxioAdvancedBilling.MaxioAdvancedBillingClient client,
    AppIdentityDbContext identityDbContext,
    IOptions<MaxioOptions> options,
    MaxioHttpCallContext callContext,
    ILogger<MaxioSubscriptionBillingService> logger) : ISubscriptionBillingService
{
    private const int ProductsPerPage = 100;
    private static readonly TimeSpan CallBudget = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan ClaimLeaseDuration = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan ReconciliationWindow = TimeSpan.FromMinutes(5);
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> EnrollmentLocks = new(StringComparer.Ordinal);

    private readonly MaxioOptions _options = options.Value;

    public Task<IReadOnlyList<SubscriptionPlanDto>> GetPlansAsync(CancellationToken cancellationToken)
        => WithCallBudgetAsync(GetPlansCoreAsync, cancellationToken);

    public Task<CreateSubscriptionResponse> SubscribeAsync(
        BillingUser user,
        string productHandle,
        CancellationToken cancellationToken)
        => WithCallBudgetAsync(ct => SubscribeCoreAsync(user, productHandle, ct), cancellationToken);

    public Task<IReadOnlyList<SubscriptionDto>> GetSubscriptionsAsync(
        BillingUser user,
        CancellationToken cancellationToken)
        => WithCallBudgetAsync(ct => GetSubscriptionsCoreAsync(user, ct), cancellationToken);

    private async Task<IReadOnlyList<SubscriptionPlanDto>> GetPlansCoreAsync(CancellationToken ct)
    {
        var familyId = await ResolveProductFamilyIdAsync(ct);
        var plans = new List<SubscriptionPlanDto>();

        for (var page = 1; ; page++)
        {
            var products = await ListFamilyProductsPageAsync(familyId, page, ct);
            foreach (var response in products)
            {
                var product = response.Product;
                var plan = TryMapPlan(product);
                if (plan is null)
                {
                    logger.LogWarning("Maxio returned a malformed product in family {ProductFamilyHandle}; it was omitted.",
                        _options.ProductFamilyHandle);
                    continue;
                }

                if (!plan.Archived)
                {
                    plans.Add(plan);
                }
            }

            if (products.Count < ProductsPerPage)
            {
                break;
            }
        }

        return plans.OrderBy(plan => plan.PriceInCents).ThenBy(plan => plan.Name, StringComparer.Ordinal).ToArray();
    }

    private async Task<CreateSubscriptionResponse> SubscribeCoreAsync(
        BillingUser user,
        string requestedProductHandle,
        CancellationToken ct)
    {
        var normalizedHandle = NormalizeProductHandle(requestedProductHandle);
        var lockKey = $"{user.Id}\n{normalizedHandle}";
        var enrollmentLock = EnrollmentLocks.GetOrAdd(lockKey, _ => new SemaphoreSlim(1, 1));
        await enrollmentLock.WaitAsync(ct);

        try
        {
            var product = await ReadEligibleProductAsync(requestedProductHandle.Trim(), ct);
            var canonicalHandle = product.Handle!;
            normalizedHandle = NormalizeProductHandle(canonicalHandle);
            var customerReference = StableReference("eshop-customer", user.Id);
            var subscriptionReference = StableReference("eshop-subscription", $"{user.Id}\n{normalizedHandle}");

            var customer = await EnsureCustomerAsync(user, customerReference, ct);
            var existing = await FindExistingSubscriptionAsync(customer, canonicalHandle, subscriptionReference, ct);
            if (existing is not null)
            {
                await RecordActiveClaimAsync(user.Id, normalizedHandle, subscriptionReference, existing.Id, ct);
                return new CreateSubscriptionResponse(MapSubscription(existing), Created: false);
            }

            var claimLease = await AcquireClaimAsync(user.Id, normalizedHandle, subscriptionReference, ct);
            if (!claimLease.OwnsLease)
            {
                existing = await FindExistingSubscriptionAsync(customer, canonicalHandle, subscriptionReference, ct);
                if (existing is not null)
                {
                    await MarkClaimActiveAsync(claimLease.Claim, existing.Id, ct);
                    return new CreateSubscriptionResponse(MapSubscription(existing), Created: false);
                }

                throw new BillingException(HttpStatusCode.Conflict,
                    "A subscription request for this plan is already being processed.");
            }

            try
            {
                var created = await CreateSubscriptionAtMaxioAsync(
                    canonicalHandle,
                    customerReference,
                    subscriptionReference,
                    ct);
                await MarkClaimActiveAsync(claimLease.Claim, created.Id, ct);
                return new CreateSubscriptionResponse(MapSubscription(created), Created: true);
            }
            catch (Exception ex) when (IsAmbiguousWriteFailure(ex))
            {
                if (!ct.IsCancellationRequested)
                {
                    existing = await FindExistingSubscriptionAsync(customer, canonicalHandle, subscriptionReference, ct);
                    if (existing is not null)
                    {
                        await MarkClaimActiveAsync(claimLease.Claim, existing.Id, ct);
                        return new CreateSubscriptionResponse(MapSubscription(existing), Created: false);
                    }
                }

                await MarkClaimForReconciliationAsync(claimLease.Claim);
                throw new BillingProviderException(HttpStatusCode.ServiceUnavailable,
                    "The subscription outcome is being reconciled. Retry this request shortly.", ex);
            }
            catch (BillingProviderException ex) when ((int)ex.StatusCode < 500)
            {
                await ReleaseClaimAsync(claimLease.Claim);
                throw;
            }
            catch (BillingProviderException)
            {
                await MarkClaimForReconciliationAsync(claimLease.Claim);
                throw;
            }
        }
        finally
        {
            enrollmentLock.Release();
        }
    }

    private async Task<IReadOnlyList<SubscriptionDto>> GetSubscriptionsCoreAsync(BillingUser user, CancellationToken ct)
    {
        var customerReference = StableReference("eshop-customer", user.Id);
        var customer = await ReadCustomerByReferenceAsync(customerReference, ct);
        if (customer is null)
        {
            return Array.Empty<SubscriptionDto>();
        }

        var subscriptions = await ListCustomerSubscriptionsAsync(customer.Id, ct);
        return subscriptions.Select(MapSubscription).ToArray();
    }

    private async Task<int> ResolveProductFamilyIdAsync(CancellationToken ct)
    {
        using var scope = callContext.Begin(writeOnce: false);
        var state = callContext.Current!;
        try
        {
            var response = await client.ProductFamilies.ListProductFamilies(
                dateField: null,
                startDate: null,
                endDate: null,
                startDatetime: null,
                endDatetime: null,
                ct: ct);

            var matches = response
                .Select(item => item.ProductFamily)
                .Where(family => family is not null &&
                    string.Equals(family.Handle, _options.ProductFamilyHandle, StringComparison.Ordinal))
                .ToArray();

            if (matches.Length != 1 || matches[0]!.Id is null)
            {
                throw new BillingProviderException(HttpStatusCode.ServiceUnavailable,
                    "The configured Maxio product family is unavailable.");
            }

            return matches[0]!.Id!.Value;
        }
        catch (SdkException<RawError> ex)
        {
            throw FromRawError(ex.Error, "Maxio could not list product families.", ex);
        }
        catch (JsonException ex)
        {
            throw FromMalformedResponse(state.LastStatusCode, ex);
        }
    }

    private async Task<IReadOnlyList<ProductResponse>> ListFamilyProductsPageAsync(int familyId, int page, CancellationToken ct)
    {
        using var scope = callContext.Begin(writeOnce: false);
        var state = callContext.Current!;
        try
        {
            return await client.ProductFamilies.ListProductsForProductFamily(
                productFamilyId: familyId.ToString(CultureInfo.InvariantCulture),
                dateField: null,
                filter: null,
                startDate: null,
                endDate: null,
                startDatetime: null,
                endDatetime: null,
                includeArchived: false,
                include: null,
                page: page,
                perPage: ProductsPerPage,
                ct: ct);
        }
        catch (SdkException<ListProductsForProductFamilyError> ex)
        {
            if (ex.Error.TryGetString(out _))
            {
                throw new BillingProviderException(HttpStatusCode.NotFound,
                    "The configured Maxio product family was not found.", ex);
            }

            if (ex.Error.TryGetRawError(out var raw))
            {
                throw FromRawError(raw, "Maxio could not list subscription plans.", ex);
            }

            throw new BillingProviderException(HttpStatusCode.BadGateway,
                "Maxio could not list subscription plans.", ex);
        }
        catch (JsonException ex)
        {
            throw FromMalformedResponse(state.LastStatusCode, ex);
        }
    }

    private async Task<Product> ReadEligibleProductAsync(string productHandle, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(productHandle))
        {
            throw new BillingException(HttpStatusCode.BadRequest, "A productHandle is required.");
        }

        using var scope = callContext.Begin(writeOnce: false);
        var state = callContext.Current!;
        try
        {
            var response = await client.Products.ReadProductByHandle(apiHandle: productHandle, ct: ct);
            var product = response.Product;
            if (!string.Equals(product.Handle, productHandle, StringComparison.Ordinal) ||
                !string.Equals(product.ProductFamily?.Handle, _options.ProductFamilyHandle, StringComparison.Ordinal))
            {
                throw new BillingException(HttpStatusCode.BadRequest,
                    "The requested subscription plan is not available in this catalog.");
            }

            if (product.ArchivedAt is not null)
            {
                throw new BillingException(HttpStatusCode.Conflict, "The requested subscription plan is archived.");
            }

            if (product.RequireCreditCard == true)
            {
                throw new BillingException(HttpStatusCode.UnprocessableEntity,
                    "The requested subscription plan requires a payment method.");
            }

            return product;
        }
        catch (SdkException<RawError> ex)
        {
            if (ex.Error.StatusCode == HttpStatusCode.NotFound)
            {
                throw new BillingException(HttpStatusCode.NotFound, "The requested subscription plan was not found.");
            }

            throw FromRawError(ex.Error, "Maxio could not validate the subscription plan.", ex);
        }
        catch (JsonException ex)
        {
            throw FromMalformedResponse(state.LastStatusCode, ex);
        }
    }

    private async Task<Customer> EnsureCustomerAsync(BillingUser user, string reference, CancellationToken ct)
    {
        var existing = await ReadCustomerByReferenceAsync(reference, ct);
        if (existing is not null)
        {
            return existing;
        }

        try
        {
            return await CreateCustomerAsync(user, reference, ct);
        }
        catch (BillingProviderException ex) when (ex.StatusCode == HttpStatusCode.UnprocessableEntity)
        {
            existing = await ReadCustomerByReferenceAsync(reference, ct);
            if (existing is not null)
            {
                return existing;
            }

            throw;
        }
    }

    private async Task<Customer?> ReadCustomerByReferenceAsync(string reference, CancellationToken ct)
    {
        using var scope = callContext.Begin(writeOnce: false);
        var state = callContext.Current!;
        try
        {
            var response = await client.Customers.ReadCustomerByReference(reference: reference, ct: ct);
            var customer = response.Customer;
            if (!string.Equals(customer.Reference, reference, StringComparison.Ordinal))
            {
                throw new BillingProviderException(HttpStatusCode.BadGateway,
                    "Maxio returned a customer that did not match the authenticated account.");
            }

            return customer;
        }
        catch (SdkException<RawError> ex) when (ex.Error.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
        catch (SdkException<RawError> ex)
        {
            throw FromRawError(ex.Error, "Maxio could not read the customer.", ex);
        }
        catch (JsonException ex)
        {
            throw FromMalformedResponse(state.LastStatusCode, ex);
        }
    }

    private async Task<Customer> CreateCustomerAsync(BillingUser user, string reference, CancellationToken ct)
    {
        using var scope = callContext.Begin(writeOnce: true);
        var state = callContext.Current!;
        try
        {
            var response = await client.Customers.CreateCustomer(
                body: new MaxioAdvancedBilling.Models.CreateCustomerRequest
                {
                    Customer = new CreateCustomer
                    {
                        FirstName = user.FirstName,
                        LastName = user.LastName,
                        Email = user.Email,
                        Reference = reference
                    }
                },
                ct: ct);
            return response.Customer;
        }
        catch (SdkException<CreateCustomerError> ex)
        {
            if (ex.Error.TryGetCustomerErrorResponse1(out _))
            {
                throw new BillingProviderException(HttpStatusCode.UnprocessableEntity,
                    "Maxio rejected the customer profile.", ex);
            }

            if (ex.Error.TryGetRawError(out var raw))
            {
                throw FromRawError(raw, "Maxio could not create the customer.", ex);
            }

            throw new BillingProviderException(HttpStatusCode.BadGateway,
                "Maxio could not create the customer.", ex);
        }
        catch (JsonException ex)
        {
            throw FromMalformedResponse(state.LastStatusCode, ex);
        }
    }

    private async Task<IReadOnlyList<Subscription>> ListCustomerSubscriptionsAsync(int? customerId, CancellationToken ct)
    {
        if (customerId is null)
        {
            throw new BillingProviderException(HttpStatusCode.BadGateway, "Maxio returned a customer without an id.");
        }

        using var scope = callContext.Begin(writeOnce: false);
        var state = callContext.Current!;
        try
        {
            var response = await client.Customers.ListCustomerSubscriptions(customerId.Value, ct: ct);
            if (response.Any(item => item.Subscription is null))
            {
                throw new BillingProviderException(HttpStatusCode.BadGateway,
                    "Maxio returned an unreadable subscription record.");
            }

            return response.Select(item => item.Subscription!).ToArray();
        }
        catch (SdkException<RawError> ex)
        {
            throw FromRawError(ex.Error, "Maxio could not list subscriptions.", ex);
        }
        catch (JsonException ex)
        {
            throw FromMalformedResponse(state.LastStatusCode, ex);
        }
    }

    private async Task<Subscription?> FindSubscriptionByReferenceAsync(string reference, CancellationToken ct)
    {
        using var scope = callContext.Begin(writeOnce: false);
        var state = callContext.Current!;
        try
        {
            var response = await client.Subscriptions.FindSubscription(reference: reference, ct: ct);
            return response.Subscription;
        }
        catch (SdkException<FindSubscriptionError> ex)
        {
            if (ex.Error.TryGetNoContent(out _))
            {
                return null;
            }

            if (ex.Error.TryGetRawError(out var raw))
            {
                throw FromRawError(raw, "Maxio could not reconcile the subscription.", ex);
            }

            throw new BillingProviderException(HttpStatusCode.BadGateway,
                "Maxio could not reconcile the subscription.", ex);
        }
        catch (JsonException ex)
        {
            throw FromMalformedResponse(state.LastStatusCode, ex);
        }
    }

    private async Task<Subscription?> FindExistingSubscriptionAsync(
        Customer customer,
        string productHandle,
        string subscriptionReference,
        CancellationToken ct)
    {
        var byReference = await FindSubscriptionByReferenceAsync(subscriptionReference, ct);
        if (byReference is not null)
        {
            return byReference;
        }

        var subscriptions = await ListCustomerSubscriptionsAsync(customer.Id, ct);
        return subscriptions.FirstOrDefault(subscription =>
            string.Equals(subscription.Product?.Handle, productHandle, StringComparison.OrdinalIgnoreCase));
    }

    private async Task<Subscription> CreateSubscriptionAtMaxioAsync(
        string productHandle,
        string customerReference,
        string subscriptionReference,
        CancellationToken ct)
    {
        var paymentCollectionMethod = await ResolvePaymentCollectionMethodAsync(ct);
        using var scope = callContext.Begin(writeOnce: true);
        var state = callContext.Current!;
        try
        {
            var response = await client.Subscriptions.CreateSubscription(
                body: new MaxioAdvancedBilling.Models.CreateSubscriptionRequest
                {
                    Subscription = new CreateSubscription
                    {
                        ProductHandle = productHandle,
                        CustomerReference = customerReference,
                        Reference = subscriptionReference,
                        PaymentCollectionMethod = paymentCollectionMethod
                    }
                },
                ct: ct);

            return response.Subscription ?? throw new BillingProviderException(HttpStatusCode.BadGateway,
                "Maxio returned an unreadable subscription record.");
        }
        catch (SdkException<CreateSubscriptionError> ex)
        {
            if (ex.Error.TryGetErrorListResponse1(out var errorResponse))
            {
                var providerErrors = errorResponse.Errors
                    .Where(message => !string.IsNullOrWhiteSpace(message))
                    .Take(10)
                    .Select(SanitizeProviderError)
                    .ToArray();
                logger.LogWarning(
                    "Maxio rejected subscription creation with HTTP 422. Provider errors: {ProviderErrors}",
                    providerErrors.Length == 0 ? "(none supplied)" : string.Join(" | ", providerErrors));
                throw new BillingProviderException(HttpStatusCode.UnprocessableEntity,
                    "Maxio rejected the subscription request.", ex);
            }

            if (ex.Error.TryGetRawError(out var raw))
            {
                throw FromRawError(raw, "Maxio could not create the subscription.", ex);
            }

            throw new BillingProviderException(HttpStatusCode.BadGateway,
                "Maxio could not create the subscription.", ex);
        }
        catch (JsonException ex)
        {
            throw FromMalformedResponse(state.LastStatusCode, ex);
        }
    }

    private async Task<MaxioAdvancedBilling.Models.Enums.CollectionMethod> ResolvePaymentCollectionMethodAsync(
        CancellationToken ct)
    {
        using var scope = callContext.Begin(writeOnce: false);
        var state = callContext.Current!;
        try
        {
            var response = await client.Sites.ReadSite(ct: ct);
            return response.Site.RelationshipInvoicingEnabled switch
            {
                true => MaxioAdvancedBilling.Models.Enums.CollectionMethod.Remittance,
                false => MaxioAdvancedBilling.Models.Enums.CollectionMethod.Invoice,
                null => throw new BillingProviderException(HttpStatusCode.BadGateway,
                    "Maxio did not identify the site's billing architecture.")
            };
        }
        catch (SdkException<RawError> ex)
        {
            throw FromRawError(ex.Error, "Maxio could not read the site's billing configuration.", ex);
        }
        catch (JsonException ex)
        {
            throw FromMalformedResponse(state.LastStatusCode, ex);
        }
    }

    private async Task<ClaimLease> AcquireClaimAsync(
        string userId,
        string productHandle,
        string subscriptionReference,
        CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        var leaseToken = Guid.NewGuid().ToString("N");
        var claim = await identityDbContext.MaxioSubscriptionClaims
            .SingleOrDefaultAsync(item => item.UserId == userId && item.ProductHandle == productHandle, ct);

        if (claim is null)
        {
            claim = MaxioSubscriptionClaim.Create(userId, productHandle, subscriptionReference,
                leaseToken, now, ClaimLeaseDuration);
            identityDbContext.MaxioSubscriptionClaims.Add(claim);
            try
            {
                await identityDbContext.SaveChangesAsync(ct);
                return new ClaimLease(claim, true);
            }
            catch (DbUpdateException)
            {
                identityDbContext.Entry(claim).State = EntityState.Detached;
                claim = await identityDbContext.MaxioSubscriptionClaims
                    .SingleAsync(item => item.UserId == userId && item.ProductHandle == productHandle, ct);
            }
        }

        if (claim.Status == MaxioSubscriptionClaimStatus.Active || claim.LeaseExpiresAt > now)
        {
            return new ClaimLease(claim, false);
        }

        claim.RenewLease(leaseToken, now, ClaimLeaseDuration);
        try
        {
            await identityDbContext.SaveChangesAsync(ct);
            return new ClaimLease(claim, true);
        }
        catch (DbUpdateConcurrencyException)
        {
            await identityDbContext.Entry(claim).ReloadAsync(ct);
            return new ClaimLease(claim, false);
        }
    }

    private async Task RecordActiveClaimAsync(
        string userId,
        string productHandle,
        string subscriptionReference,
        int? subscriptionId,
        CancellationToken ct)
    {
        var claim = await identityDbContext.MaxioSubscriptionClaims
            .SingleOrDefaultAsync(item => item.UserId == userId && item.ProductHandle == productHandle, ct);
        if (claim is null)
        {
            claim = MaxioSubscriptionClaim.Create(userId, productHandle, subscriptionReference,
                Guid.NewGuid().ToString("N"), DateTimeOffset.UtcNow, ClaimLeaseDuration);
            identityDbContext.MaxioSubscriptionClaims.Add(claim);
        }

        claim.MarkActive(subscriptionId, DateTimeOffset.UtcNow);
        try
        {
            await identityDbContext.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            identityDbContext.Entry(claim).State = EntityState.Detached;
        }
    }

    private async Task MarkClaimActiveAsync(MaxioSubscriptionClaim claim, int? subscriptionId, CancellationToken ct)
    {
        claim.MarkActive(subscriptionId, DateTimeOffset.UtcNow);
        await identityDbContext.SaveChangesAsync(ct);
    }

    private async Task MarkClaimForReconciliationAsync(MaxioSubscriptionClaim claim)
    {
        claim.MarkReconciliationRequired(DateTimeOffset.UtcNow, ReconciliationWindow);
        await identityDbContext.SaveChangesAsync(CancellationToken.None);
    }

    private async Task ReleaseClaimAsync(MaxioSubscriptionClaim claim)
    {
        identityDbContext.MaxioSubscriptionClaims.Remove(claim);
        await identityDbContext.SaveChangesAsync(CancellationToken.None);
    }

    private static SubscriptionPlanDto? TryMapPlan(Product product)
    {
        if (string.IsNullOrWhiteSpace(product.Handle) ||
            string.IsNullOrWhiteSpace(product.Name) ||
            product.PriceInCents is null ||
            product.Interval is null ||
            product.IntervalUnit is null)
        {
            return null;
        }

        return new SubscriptionPlanDto(
            product.Handle,
            product.Name,
            product.Description,
            product.PriceInCents.Value,
            product.Interval.Value,
            product.IntervalUnit.Value,
            product.ArchivedAt is not null,
            product.RequestCreditCard == true,
            product.RequireCreditCard == true);
    }

    private static SubscriptionDto MapSubscription(Subscription subscription)
    {
        if (subscription.Id is null ||
            string.IsNullOrWhiteSpace(subscription.Product?.Handle) ||
            string.IsNullOrWhiteSpace(subscription.Product.Name) ||
            subscription.ProductPriceInCents is null ||
            subscription.State is null ||
            subscription.Product.Interval is null ||
            subscription.Product.IntervalUnit is null)
        {
            throw new BillingProviderException(HttpStatusCode.BadGateway,
                "Maxio returned an incomplete subscription record.");
        }

        return new SubscriptionDto(
            subscription.Id.Value,
            subscription.Reference,
            subscription.Product.Handle,
            subscription.Product.Name,
            subscription.ProductPriceInCents.Value,
            subscription.State.Value,
            subscription.NextAssessmentAt,
            subscription.Product.Interval.Value,
            subscription.Product.IntervalUnit.Value,
            subscription.Currency);
    }

    private static string NormalizeProductHandle(string handle)
    {
        if (string.IsNullOrWhiteSpace(handle))
        {
            throw new BillingException(HttpStatusCode.BadRequest, "A productHandle is required.");
        }

        return handle.Trim().ToLowerInvariant();
    }

    private static string StableReference(string prefix, string value)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return $"{prefix}-{Convert.ToHexString(hash).ToLowerInvariant()}";
    }

    private static string SanitizeProviderError(string message)
    {
        const int maximumLength = 500;
        var sanitized = message.Replace('\r', ' ').Replace('\n', ' ').Trim();
        return sanitized.Length <= maximumLength ? sanitized : sanitized[..maximumLength];
    }

    private static bool IsAmbiguousWriteFailure(Exception exception)
        => exception is MaxioWriteResendBlockedException or HttpRequestException or TaskCanceledException;

    private static BillingProviderException FromRawError(RawError raw, string safeMessage, Exception inner)
    {
        var status = raw.StatusCode;
        return new BillingProviderException(
            (int)status is >= 400 and < 500 ? status : HttpStatusCode.BadGateway,
            safeMessage,
            inner);
    }

    private static BillingProviderException FromMalformedResponse(HttpStatusCode? status, JsonException inner)
    {
        var mappedStatus = status is not null && (int)status.Value is >= 400 and < 500
            ? status.Value
            : HttpStatusCode.BadGateway;
        return new BillingProviderException(mappedStatus,
            "Maxio returned a response that could not be processed.", inner);
    }

    private static async Task<T> WithCallBudgetAsync<T>(
        Func<CancellationToken, Task<T>> action,
        CancellationToken callerToken)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(callerToken);
        cts.CancelAfter(CallBudget);
        try
        {
            return await action(cts.Token);
        }
        catch (OperationCanceledException) when (callerToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException ex)
        {
            throw new BillingProviderException(HttpStatusCode.GatewayTimeout,
                "Maxio did not respond before the billing request timed out.", ex);
        }
        catch (HttpRequestException ex)
        {
            throw new BillingProviderException(HttpStatusCode.BadGateway,
                "Maxio is currently unreachable.", ex);
        }
    }

    private sealed record ClaimLease(MaxioSubscriptionClaim Claim, bool OwnsLease);
}
