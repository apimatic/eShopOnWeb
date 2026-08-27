using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.ErrorResponse;
using MaxioAdvancedBilling.Core.Exceptions;
using MaxioAdvancedBilling.Errors;
using MaxioAdvancedBilling.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.Infrastructure.Identity;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

internal sealed class MaxioSubscriptionBillingService(
    MaxioAdvancedBillingClient client,
    AppIdentityDbContext identityDbContext,
    IOptions<MaxioOptions> options,
    ISubscriptionOperationLock operationLock,
    TimeProvider timeProvider,
    ILogger<MaxioSubscriptionBillingService> logger) : ISubscriptionBillingService
{
    private static readonly TimeSpan CallBudget = TimeSpan.FromSeconds(20);
    private static readonly TimeSpan EnrollmentLease = TimeSpan.FromMinutes(2);
    private const int ProductsPerPage = 100;

    private readonly MaxioOptions _options = options.Value;

    public async Task<IReadOnlyList<SubscriptionPlanDto>> ListPlansAsync(CancellationToken cancellationToken)
    {
        try
        {
            var products = await ListConfiguredProductsAsync(cancellationToken);
            return products
                .Select(MapPlan)
                .OrderBy(plan => plan.PriceInCents)
                .ThenBy(plan => plan.Name, StringComparer.Ordinal)
                .ToList();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (BillingApiException)
        {
            throw;
        }
        catch (Exception ex) when (IsProviderBoundaryFailure(ex))
        {
            throw TranslateBoundaryFailure(ex);
        }
    }

    public async Task<CreateSubscriptionResult> SubscribeAsync(
        BillingUserIdentity user,
        string productHandle,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(productHandle))
        {
            throw new BillingApiException(
                StatusCodes.Status400BadRequest,
                "productHandle is required.");
        }

        productHandle = productHandle.Trim();

        try
        {
            await using var heldLock = await operationLock.AcquireAsync(user.UserId, cancellationToken);

            var products = await ListConfiguredProductsAsync(cancellationToken);
            if (!products.Any(product => string.Equals(product.Handle, productHandle, StringComparison.Ordinal)))
            {
                throw new BillingApiException(
                    StatusCodes.Status404NotFound,
                    $"Subscription plan '{productHandle}' was not found.");
            }

            await EnsureCustomerAsync(user, cancellationToken);

            var providerReference = CreateProviderReference(user.UserId, productHandle);
            var enrollment = await identityDbContext.MaxioSubscriptionEnrollments
                .SingleOrDefaultAsync(
                    item => item.UserId == user.UserId && item.ProductHandle == productHandle,
                    cancellationToken);

            var reconciled = await ReconcileSubscriptionAsync(enrollment, providerReference, cancellationToken);
            if (reconciled is not null)
            {
                return new CreateSubscriptionResult(MapSubscription(reconciled), Created: false);
            }

            var leaseOwner = Guid.NewGuid().ToString("N");
            enrollment = await AcquireEnrollmentLeaseAsync(
                enrollment,
                user.UserId,
                productHandle,
                providerReference,
                leaseOwner,
                cancellationToken);

            try
            {
                var created = await CreateSubscriptionAtMaxioAsync(
                    productHandle,
                    user.UserId,
                    providerReference,
                    cancellationToken);

                await CompleteEnrollmentAsync(enrollment, created, cancellationToken);
                return new CreateSubscriptionResult(MapSubscription(created), Created: true);
            }
            catch (BillingApiException ex) when (ex.StatusCode == StatusCodes.Status422UnprocessableEntity)
            {
                enrollment.Reject(timeProvider.GetUtcNow());
                await identityDbContext.SaveChangesAsync(cancellationToken);
                throw;
            }
            catch (Exception ex) when (IsAmbiguousWriteFailure(ex))
            {
                var recovered = await TryReconcileAfterAmbiguousWriteAsync(providerReference);
                if (recovered is not null)
                {
                    await CompleteEnrollmentAsync(enrollment, recovered, CancellationToken.None);
                    return new CreateSubscriptionResult(MapSubscription(recovered), Created: true);
                }

                enrollment.MarkOutcomeUnknown(timeProvider.GetUtcNow());
                await identityDbContext.SaveChangesAsync(CancellationToken.None);
                logger.LogWarning(ex, "Maxio subscription write outcome is unknown for enrollment {EnrollmentId}.", enrollment.Id);
                throw new BillingApiException(
                    StatusCodes.Status503ServiceUnavailable,
                    "The subscription outcome is still being reconciled. Retry shortly.",
                    ex);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (BillingApiException)
        {
            throw;
        }
        catch (Exception ex) when (IsProviderBoundaryFailure(ex))
        {
            throw TranslateBoundaryFailure(ex);
        }
    }

    public async Task<IReadOnlyList<SubscriptionDto>> ListSubscriptionsAsync(
        BillingUserIdentity user,
        CancellationToken cancellationToken)
    {
        try
        {
            var customer = await ReadCustomerByReferenceAsync(user.UserId, cancellationToken);
            if (customer is null)
            {
                return Array.Empty<SubscriptionDto>();
            }

            var customerId = customer.Id ?? throw new InvalidMaxioResponseException(
                "Maxio returned a customer without an ID.");

            var responses = await BoundedAsync(
                ct => client.Customers.ListCustomerSubscriptions(customerId, ct: ct),
                cancellationToken);

            return responses
                .Where(response => response.Subscription is not null)
                .Select(response => MapSubscription(response.Subscription!))
                .OrderBy(subscription => subscription.ProductName, StringComparer.Ordinal)
                .ToList();
        }
        catch (SdkException<RawError> ex)
        {
            throw FromRawError(ex.Error, "Unable to list Maxio subscriptions.", ex);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (BillingApiException)
        {
            throw;
        }
        catch (Exception ex) when (IsProviderBoundaryFailure(ex))
        {
            throw TranslateBoundaryFailure(ex);
        }
    }

    private async Task<IReadOnlyList<Product>> ListConfiguredProductsAsync(CancellationToken cancellationToken)
    {
        var familyId = await ResolveProductFamilyIdAsync(cancellationToken);
        try
        {
            return await ListProductsAsync(familyId, cancellationToken);
        }
        catch (BillingApiException ex) when (ex.StatusCode == StatusCodes.Status404NotFound)
        {
            // Numeric IDs can change when the sandbox catalog is re-seeded.
            familyId = await ResolveProductFamilyIdAsync(cancellationToken);
            return await ListProductsAsync(familyId, cancellationToken);
        }
    }

    private async Task<int> ResolveProductFamilyIdAsync(CancellationToken cancellationToken)
    {
        try
        {
            var families = await BoundedAsync(
                ct => client.ProductFamilies.ListProductFamilies(
                    dateField: null,
                    startDate: null,
                    endDate: null,
                    startDatetime: null,
                    endDatetime: null,
                    ct: ct),
                cancellationToken);

            var family = families
                .Select(response => response.ProductFamily)
                .FirstOrDefault(item =>
                    item is not null &&
                    item.ArchivedAt is null &&
                    string.Equals(item.Handle, _options.ProductFamilyHandle, StringComparison.Ordinal));

            return family?.Id ?? throw new BillingApiException(
                StatusCodes.Status404NotFound,
                $"Configured subscription product family '{_options.ProductFamilyHandle}' was not found.");
        }
        catch (SdkException<RawError> ex)
        {
            throw FromRawError(ex.Error, "Unable to load Maxio product families.", ex);
        }
    }

    private async Task<IReadOnlyList<Product>> ListProductsAsync(
        int familyId,
        CancellationToken cancellationToken)
    {
        var products = new List<Product>();
        for (var page = 1; ; page++)
        {
            IReadOnlyList<ProductResponse> responses;
            try
            {
                responses = await BoundedAsync(
                    ct => client.ProductFamilies.ListProductsForProductFamily(
                        productFamilyId: familyId.ToString(System.Globalization.CultureInfo.InvariantCulture),
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
                        ct: ct),
                    cancellationToken);
            }
            catch (SdkException<ListProductsForProductFamilyError> ex)
            {
                if (ex.Error.TryGetString(out _))
                {
                    throw new BillingApiException(StatusCodes.Status404NotFound, "The configured Maxio product family no longer exists.", ex);
                }

                if (ex.Error.TryGetRawError(out var raw))
                {
                    throw FromRawError(raw, "Unable to load Maxio products.", ex);
                }

                throw new BillingApiException(StatusCodes.Status502BadGateway, "Maxio returned an unrecognized product error.", ex);
            }

            products.AddRange(responses
                .Select(response => response.Product)
                .Where(product => product.ArchivedAt is null));

            if (responses.Count < ProductsPerPage)
            {
                return products;
            }
        }
    }

    private async Task<Customer> EnsureCustomerAsync(
        BillingUserIdentity user,
        CancellationToken cancellationToken)
    {
        var existing = await ReadCustomerByReferenceAsync(user.UserId, cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        var request = new MaxioAdvancedBilling.Models.CreateCustomerRequest
        {
            Customer = new CreateCustomer
            {
                FirstName = user.FirstName,
                LastName = user.LastName,
                Email = user.Email,
                Reference = user.UserId
            }
        };

        try
        {
            using var writeScope = MaxioWriteGuardHandler.BeginScope();
            var response = await BoundedAsync(
                ct => client.Customers.CreateCustomer(body: request, ct: ct),
                cancellationToken);
            return response.Customer;
        }
        catch (SdkException<CreateCustomerError> ex)
        {
            if (ex.Error.TryGetCustomerErrorResponse1(out _))
            {
                var concurrent = await ReadCustomerByReferenceAsync(user.UserId, cancellationToken);
                if (concurrent is not null)
                {
                    return concurrent;
                }

                throw new BillingApiException(
                    StatusCodes.Status422UnprocessableEntity,
                    "Maxio rejected the customer details required for this subscription.",
                    ex);
            }

            if (ex.Error.TryGetRawError(out var raw))
            {
                throw FromRawError(raw, "Unable to create the Maxio customer.", ex);
            }

            throw new BillingApiException(StatusCodes.Status502BadGateway, "Maxio returned an unrecognized customer error.", ex);
        }
        catch (Exception ex) when (IsAmbiguousWriteFailure(ex))
        {
            var recovered = await ReadCustomerByReferenceAsync(user.UserId, CancellationToken.None);
            if (recovered is not null)
            {
                return recovered;
            }

            throw new BillingApiException(
                StatusCodes.Status503ServiceUnavailable,
                "The customer outcome is still being reconciled. Retry shortly.",
                ex);
        }
    }

    private async Task<Customer?> ReadCustomerByReferenceAsync(
        string reference,
        CancellationToken cancellationToken)
    {
        try
        {
            var response = await BoundedAsync(
                ct => client.Customers.ReadCustomerByReference(reference: reference, ct: ct),
                cancellationToken);
            return response.Customer;
        }
        catch (SdkException<RawError> ex) when (ex.Error.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
        catch (SdkException<RawError> ex)
        {
            throw FromRawError(ex.Error, "Unable to read the Maxio customer.", ex);
        }
    }

    private async Task<Subscription?> ReconcileSubscriptionAsync(
        MaxioSubscriptionEnrollment? enrollment,
        string providerReference,
        CancellationToken cancellationToken)
    {
        if (enrollment?.MaxioSubscriptionId is int subscriptionId)
        {
            var byId = await ReadSubscriptionAsync(subscriptionId, cancellationToken);
            if (byId is not null)
            {
                await CompleteEnrollmentAsync(enrollment, byId, cancellationToken);
                return byId;
            }
        }

        var byReference = await FindSubscriptionAsync(providerReference, cancellationToken);
        if (byReference is not null && enrollment is not null)
        {
            await CompleteEnrollmentAsync(enrollment, byReference, cancellationToken);
        }

        return byReference;
    }

    private async Task<Subscription?> ReadSubscriptionAsync(
        int subscriptionId,
        CancellationToken cancellationToken)
    {
        try
        {
            var response = await BoundedAsync(
                ct => client.Subscriptions.ReadSubscription(
                    subscriptionId: subscriptionId,
                    include: null,
                    ct: ct),
                cancellationToken);
            return response.Subscription ?? throw new InvalidMaxioResponseException(
                "Maxio returned a subscription response without a subscription.");
        }
        catch (SdkException<RawError> ex) when (ex.Error.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
        catch (SdkException<RawError> ex)
        {
            throw FromRawError(ex.Error, "Unable to read the Maxio subscription.", ex);
        }
    }

    private async Task<Subscription?> FindSubscriptionAsync(
        string providerReference,
        CancellationToken cancellationToken)
    {
        try
        {
            var response = await BoundedAsync(
                ct => client.Subscriptions.FindSubscription(reference: providerReference, ct: ct),
                cancellationToken);
            return response.Subscription ?? throw new InvalidMaxioResponseException(
                "Maxio returned a subscription response without a subscription.");
        }
        catch (SdkException<FindSubscriptionError> ex)
        {
            if (ex.Error.TryGetNoContent(out _))
            {
                return null;
            }

            if (ex.Error.TryGetRawError(out var raw))
            {
                throw FromRawError(raw, "Unable to find the Maxio subscription.", ex);
            }

            throw new BillingApiException(StatusCodes.Status502BadGateway, "Maxio returned an unrecognized subscription lookup error.", ex);
        }
    }

    private async Task<Subscription> CreateSubscriptionAtMaxioAsync(
        string productHandle,
        string customerReference,
        string providerReference,
        CancellationToken cancellationToken)
    {
        var request = new MaxioAdvancedBilling.Models.CreateSubscriptionRequest
        {
            Subscription = new CreateSubscription
            {
                ProductHandle = productHandle,
                CustomerReference = customerReference,
                Reference = providerReference,
                PaymentCollectionMethod = MaxioAdvancedBilling.Models.Enums.CollectionMethod.Remittance
            }
        };

        try
        {
            using var writeScope = MaxioWriteGuardHandler.BeginScope();
            var response = await BoundedAsync(
                ct => client.Subscriptions.CreateSubscription(body: request, ct: ct),
                cancellationToken);
            return response.Subscription ?? throw new InvalidMaxioResponseException(
                "Maxio returned a subscription response without a subscription.");
        }
        catch (SdkException<CreateSubscriptionError> ex)
        {
            if (ex.Error.TryGetErrorListResponse1(out var validation))
            {
                var detail = string.Join("; ", validation.Errors.Take(5));
                var message = string.IsNullOrWhiteSpace(detail)
                    ? "Maxio rejected the subscription request."
                    : $"Maxio rejected the subscription request: {detail}";
                throw new BillingApiException(StatusCodes.Status422UnprocessableEntity, message, ex);
            }

            if (ex.Error.TryGetRawError(out var raw))
            {
                throw FromRawError(raw, "Unable to create the Maxio subscription.", ex);
            }

            throw new BillingApiException(StatusCodes.Status502BadGateway, "Maxio returned an unrecognized subscription error.", ex);
        }
    }

    private async Task<MaxioSubscriptionEnrollment> AcquireEnrollmentLeaseAsync(
        MaxioSubscriptionEnrollment? enrollment,
        string userId,
        string productHandle,
        string providerReference,
        string leaseOwner,
        CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        if (enrollment is null)
        {
            enrollment = new MaxioSubscriptionEnrollment(
                userId,
                productHandle,
                providerReference,
                leaseOwner,
                now,
                now.Add(EnrollmentLease));
            identityDbContext.MaxioSubscriptionEnrollments.Add(enrollment);
            try
            {
                await identityDbContext.SaveChangesAsync(cancellationToken);
                return enrollment;
            }
            catch (DbUpdateException)
            {
                identityDbContext.Entry(enrollment).State = EntityState.Detached;
                enrollment = await identityDbContext.MaxioSubscriptionEnrollments.SingleAsync(
                    item => item.UserId == userId && item.ProductHandle == productHandle,
                    cancellationToken);
            }
        }

        if (enrollment.HasActiveLease(now))
        {
            throw new BillingApiException(
                StatusCodes.Status409Conflict,
                "A subscription request for this plan is already in progress.");
        }

        enrollment.AcquireLease(leaseOwner, now, now.Add(EnrollmentLease));
        try
        {
            await identityDbContext.SaveChangesAsync(cancellationToken);
            return enrollment;
        }
        catch (DbUpdateConcurrencyException ex)
        {
            throw new BillingApiException(
                StatusCodes.Status409Conflict,
                "A subscription request for this plan is already in progress.",
                ex);
        }
    }

    private async Task CompleteEnrollmentAsync(
        MaxioSubscriptionEnrollment enrollment,
        Subscription subscription,
        CancellationToken cancellationToken)
    {
        var id = subscription.Id ?? throw new InvalidMaxioResponseException(
            "Maxio returned a subscription without an ID.");
        var state = subscription.State?.Value ?? "unknown";
        enrollment.Complete(id, state, timeProvider.GetUtcNow());
        await identityDbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task<Subscription?> TryReconcileAfterAmbiguousWriteAsync(string providerReference)
    {
        try
        {
            return await FindSubscriptionAsync(providerReference, CancellationToken.None);
        }
        catch (Exception ex) when (IsProviderBoundaryFailure(ex) || ex is BillingApiException)
        {
            logger.LogWarning(ex, "Maxio reconciliation could not determine the subscription outcome.");
            return null;
        }
    }

    private async Task<T> BoundedAsync<T>(
        Func<CancellationToken, Task<T>> operation,
        CancellationToken cancellationToken)
    {
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        budget.CancelAfter(CallBudget);
        return await operation(budget.Token);
    }

    private BillingApiException FromRawError(RawError raw, string message, Exception exception)
    {
        logger.LogWarning(exception, "Maxio returned HTTP {StatusCode}.", (int)raw.StatusCode);
        var statusCode = raw.StatusCode switch
        {
            HttpStatusCode.BadRequest => StatusCodes.Status400BadRequest,
            HttpStatusCode.NotFound => StatusCodes.Status404NotFound,
            HttpStatusCode.Conflict => StatusCodes.Status409Conflict,
            HttpStatusCode.UnprocessableEntity => StatusCodes.Status422UnprocessableEntity,
            HttpStatusCode.TooManyRequests => StatusCodes.Status503ServiceUnavailable,
            _ => StatusCodes.Status502BadGateway
        };
        return new BillingApiException(statusCode, message, exception);
    }

    private static SubscriptionPlanDto MapPlan(Product product)
    {
        if (string.IsNullOrWhiteSpace(product.Handle) ||
            string.IsNullOrWhiteSpace(product.Name) ||
            product.PriceInCents is null ||
            product.Interval is null ||
            product.IntervalUnit is null)
        {
            throw new InvalidMaxioResponseException("Maxio returned an incomplete subscription plan.");
        }

        return new SubscriptionPlanDto(
            product.Handle,
            product.Name,
            product.Description,
            product.PriceInCents.Value,
            product.Interval.Value,
            product.IntervalUnit.Value);
    }

    private static SubscriptionDto MapSubscription(Subscription subscription)
    {
        var product = subscription.Product ?? throw new InvalidMaxioResponseException(
            "Maxio returned a subscription without product details.");
        if (subscription.Id is null ||
            string.IsNullOrWhiteSpace(product.Handle) ||
            string.IsNullOrWhiteSpace(product.Name))
        {
            throw new InvalidMaxioResponseException("Maxio returned an incomplete subscription.");
        }

        var price = subscription.ProductPriceInCents ?? product.PriceInCents ??
            throw new InvalidMaxioResponseException("Maxio returned a subscription without a price.");

        return new SubscriptionDto(
            subscription.Id.Value,
            product.Handle,
            product.Name,
            price,
            subscription.State?.Value ?? "unknown",
            subscription.NextAssessmentAt);
    }

    private static string CreateProviderReference(string userId, string productHandle)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes($"{userId}\n{productHandle}"));
        return $"eshop-{Convert.ToHexString(bytes.AsSpan(0, 16)).ToLowerInvariant()}";
    }

    private static bool IsAmbiguousWriteFailure(Exception exception) =>
        exception is HttpRequestException or TaskCanceledException or JsonException or
            MaxioWriteRetryBlockedException or InvalidMaxioResponseException;

    private static bool IsProviderBoundaryFailure(Exception exception) =>
        IsAmbiguousWriteFailure(exception);

    private static BillingApiException TranslateBoundaryFailure(Exception exception) => exception switch
    {
        TaskCanceledException => new BillingApiException(
            StatusCodes.Status504GatewayTimeout,
            "Maxio did not respond before the billing timeout.",
            exception),
        HttpRequestException or MaxioWriteRetryBlockedException => new BillingApiException(
            StatusCodes.Status503ServiceUnavailable,
            "Maxio is temporarily unavailable.",
            exception),
        JsonException or InvalidMaxioResponseException => new BillingApiException(
            StatusCodes.Status502BadGateway,
            "Maxio returned a response that could not be processed.",
            exception),
        _ => new BillingApiException(
            StatusCodes.Status502BadGateway,
            "The billing provider request failed.",
            exception)
    };
}
