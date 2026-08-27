using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.ErrorResponse;
using MaxioAdvancedBilling.Core.Exceptions;
using MaxioAdvancedBilling.Errors;
using MaxioAdvancedBilling.Models;
using MaxioAdvancedBilling.Models.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

internal sealed class MaxioSubscriptionBillingService : ISubscriptionBillingService
{
    private const int PageSize = 100;
    private static readonly TimeSpan CallBudget = TimeSpan.FromSeconds(25);
    private static readonly TimeSpan PlanCacheDuration = TimeSpan.FromSeconds(30);

    private readonly MaxioAdvancedBillingClient _client;
    private readonly CatalogContext _dbContext;
    private readonly IMemoryCache _cache;
    private readonly MaxioOptions _options;
    private readonly SubscriptionOperationLocks _operationLocks;
    private readonly ILogger<MaxioSubscriptionBillingService> _logger;

    public MaxioSubscriptionBillingService(
        MaxioAdvancedBillingClient client,
        CatalogContext dbContext,
        IMemoryCache cache,
        IOptions<MaxioOptions> options,
        SubscriptionOperationLocks operationLocks,
        ILogger<MaxioSubscriptionBillingService> logger)
    {
        _client = client;
        _dbContext = dbContext;
        _cache = cache;
        _options = options.Value;
        _operationLocks = operationLocks;
        _logger = logger;
    }

    public async Task<IReadOnlyList<SubscriptionPlan>> GetPlansAsync(CancellationToken cancellationToken)
    {
        var cacheKey = $"maxio-plans:{_options.ProductFamilyHandle}";
        if (_cache.TryGetValue(cacheKey, out IReadOnlyList<SubscriptionPlan>? cached) && cached is not null)
        {
            return cached;
        }

        var familyId = await ResolveProductFamilyIdAsync(cancellationToken);
        var plans = new List<SubscriptionPlan>();

        for (var page = 1; ; page++)
        {
            IReadOnlyList<ProductResponse> responses;
            try
            {
                responses = await BoundedAsync(
                    ct => _client.ProductFamilies.ListProductsForProductFamily(
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
                        perPage: PageSize,
                        ct: ct),
                    cancellationToken);
            }
            catch (SdkException<ListProductsForProductFamilyError> ex)
            {
                if (ex.Error.TryGetString(out _))
                {
                    throw ProviderConfigurationError(ex);
                }

                if (ex.Error.TryGetRawError(out var raw))
                {
                    throw ProviderReadError(raw.StatusCode, ex);
                }

                throw InvalidProviderResponse(ex);
            }
            catch (JsonException ex)
            {
                throw InvalidProviderResponse(ex);
            }
            catch (Exception ex) when (IsProviderTransportFailure(ex, cancellationToken))
            {
                throw ProviderUnavailable(ex);
            }

            foreach (var response in responses)
            {
                if (TryMapPlan(response.Product, out var plan))
                {
                    plans.Add(plan);
                }
                else
                {
                    _logger.LogWarning("Maxio returned an incomplete or out-of-family product envelope; it was omitted.");
                }
            }

            if (responses.Count < PageSize)
            {
                break;
            }
        }

        var result = plans.OrderBy(plan => plan.PriceInCents).ToArray();
        _cache.Set(cacheKey, result, PlanCacheDuration);
        return result;
    }

    public async Task<SubscribeResult> SubscribeAsync(
        SubscriptionUser user,
        string productHandle,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(productHandle))
        {
            throw new SubscriptionBillingException(
                "product_handle_required",
                "A product handle is required.",
                (int)HttpStatusCode.BadRequest);
        }

        var normalizedHandle = productHandle.Trim();
        var product = await ReadEligibleProductAsync(normalizedHandle, cancellationToken);
        var customerReference = MaxioReferenceGenerator.Customer(user.UserId);
        var subscriptionReference = MaxioReferenceGenerator.Subscription(user.UserId, product.Handle!);
        var lockKey = user.UserId + "\n" + product.Handle;

        using var operationLock = await _operationLocks.AcquireAsync(lockKey, cancellationToken);
        var enrollment = await _dbContext.SubscriptionEnrollments.SingleOrDefaultAsync(
            item => item.UserId == user.UserId && item.ProductHandle == product.Handle,
            cancellationToken);

        if (enrollment is not null)
        {
            var existing = await FindSubscriptionAsync(enrollment.SubscriptionReference, cancellationToken);
            if (existing is not null)
            {
                enrollment.MarkSucceeded(existing.Id);
                await _dbContext.SaveChangesAsync(cancellationToken);
                return SubscribeResult.Completed(existing.Summary);
            }

            if (enrollment.SubscriptionWriteStarted ||
                enrollment.Status is SubscriptionEnrollmentStatus.Succeeded or SubscriptionEnrollmentStatus.Ambiguous)
            {
                enrollment.MarkAmbiguous();
                await _dbContext.SaveChangesAsync(cancellationToken);
                return SubscribeResult.Pending();
            }

            enrollment.MarkPending();
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        else
        {
            enrollment = new SubscriptionEnrollment(
                user.UserId,
                product.Handle!,
                customerReference,
                subscriptionReference);
            _dbContext.SubscriptionEnrollments.Add(enrollment);

            try
            {
                await _dbContext.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateException)
            {
                _dbContext.Entry(enrollment).State = EntityState.Detached;
                enrollment = await _dbContext.SubscriptionEnrollments.SingleAsync(
                    item => item.UserId == user.UserId && item.ProductHandle == product.Handle,
                    cancellationToken);

                var concurrent = await FindSubscriptionAsync(enrollment.SubscriptionReference, cancellationToken);
                if (concurrent is not null)
                {
                    return SubscribeResult.Completed(concurrent.Summary);
                }

                return SubscribeResult.Pending();
            }
        }

        try
        {
            await EnsureCustomerAsync(user, customerReference, cancellationToken);
        }
        catch (SubscriptionBillingException)
        {
            enrollment.MarkFailed();
            await _dbContext.SaveChangesAsync(CancellationToken.None);
            throw;
        }

        enrollment.MarkSubscriptionWriteStarted();
        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            await _dbContext.Entry(enrollment).ReloadAsync(cancellationToken);
            var concurrent = await FindSubscriptionAsync(enrollment.SubscriptionReference, cancellationToken);
            return concurrent is null
                ? SubscribeResult.Pending()
                : SubscribeResult.Completed(concurrent.Summary);
        }

        try
        {
            var created = await CreateSubscriptionAsync(
                product.Handle!,
                customerReference,
                subscriptionReference,
                cancellationToken);
            enrollment.MarkSucceeded(created.Id);
            await _dbContext.SaveChangesAsync(cancellationToken);
            return SubscribeResult.Completed(created.Summary);
        }
        catch (AmbiguousMaxioWriteException ex)
        {
            _logger.LogWarning(ex, "The Maxio subscription write outcome is ambiguous; reconciliation is required.");
            enrollment.MarkAmbiguous();
            await _dbContext.SaveChangesAsync(CancellationToken.None);
            return SubscribeResult.Pending();
        }
        catch (SubscriptionBillingException)
        {
            enrollment.MarkFailed();
            await _dbContext.SaveChangesAsync(CancellationToken.None);
            throw;
        }
    }

    public async Task<IReadOnlyList<SubscriptionSummary>> GetSubscriptionsAsync(
        string userId,
        CancellationToken cancellationToken)
    {
        var customer = await ReadCustomerAsync(MaxioReferenceGenerator.Customer(userId), cancellationToken);
        if (customer is null)
        {
            return Array.Empty<SubscriptionSummary>();
        }

        IReadOnlyList<SubscriptionResponse> responses;
        try
        {
            responses = await BoundedAsync(
                ct => _client.Customers.ListCustomerSubscriptions(customer.Id!.Value, ct: ct),
                cancellationToken);
        }
        catch (SdkException<RawError> ex)
        {
            throw ProviderReadError(ex.Error.StatusCode, ex);
        }
        catch (JsonException ex)
        {
            throw InvalidProviderResponse(ex);
        }
        catch (Exception ex) when (IsProviderTransportFailure(ex, cancellationToken))
        {
            throw ProviderUnavailable(ex);
        }

        var subscriptions = new List<SubscriptionSummary>();
        foreach (var response in responses)
        {
            if (TryMapSubscription(response.Subscription, out var mapped))
            {
                subscriptions.Add(mapped.Summary);
            }
            else
            {
                _logger.LogWarning("Maxio returned an incomplete subscription envelope; it was omitted.");
            }
        }

        return subscriptions;
    }

    private async Task<int> ResolveProductFamilyIdAsync(CancellationToken cancellationToken)
    {
        IReadOnlyList<ProductFamilyResponse> responses;
        try
        {
            responses = await BoundedAsync(
                ct => _client.ProductFamilies.ListProductFamilies(
                    dateField: null,
                    startDate: null,
                    endDate: null,
                    startDatetime: null,
                    endDatetime: null,
                    ct: ct),
                cancellationToken);
        }
        catch (SdkException<RawError> ex)
        {
            throw ProviderReadError(ex.Error.StatusCode, ex);
        }
        catch (JsonException ex)
        {
            throw InvalidProviderResponse(ex);
        }
        catch (Exception ex) when (IsProviderTransportFailure(ex, cancellationToken))
        {
            throw ProviderUnavailable(ex);
        }

        var family = responses
            .Select(response => response.ProductFamily)
            .SingleOrDefault(candidate =>
                candidate is not null &&
                candidate.ArchivedAt is null &&
                string.Equals(candidate.Handle, _options.ProductFamilyHandle, StringComparison.Ordinal));

        if (family?.Id is null)
        {
            throw ProviderConfigurationError();
        }

        return family.Id.Value;
    }

    private async Task<Product> ReadEligibleProductAsync(
        string productHandle,
        CancellationToken cancellationToken)
    {
        ProductResponse response;
        try
        {
            response = await BoundedAsync(
                ct => _client.Products.ReadProductByHandle(productHandle, ct: ct),
                cancellationToken);
        }
        catch (SdkException<RawError> ex) when (ex.Error.StatusCode == HttpStatusCode.NotFound)
        {
            throw new SubscriptionBillingException(
                "subscription_plan_not_found",
                "The requested subscription plan was not found.",
                (int)HttpStatusCode.NotFound,
                ex);
        }
        catch (SdkException<RawError> ex)
        {
            throw ProviderReadError(ex.Error.StatusCode, ex);
        }
        catch (JsonException ex)
        {
            throw InvalidProviderResponse(ex);
        }
        catch (Exception ex) when (IsProviderTransportFailure(ex, cancellationToken))
        {
            throw ProviderUnavailable(ex);
        }

        var product = response.Product;
        if (product is null ||
            product.ArchivedAt is not null ||
            string.IsNullOrWhiteSpace(product.Handle) ||
            !string.Equals(product.Handle, productHandle, StringComparison.Ordinal) ||
            !string.Equals(product.ProductFamily?.Handle, _options.ProductFamilyHandle, StringComparison.Ordinal))
        {
            throw new SubscriptionBillingException(
                "subscription_plan_not_found",
                "The requested subscription plan was not found.",
                (int)HttpStatusCode.NotFound);
        }

        if (product.RequireCreditCard == true)
        {
            throw new SubscriptionBillingException(
                "payment_method_required",
                "This plan requires a payment method and cannot be purchased through this subscription flow.",
                (int)HttpStatusCode.UnprocessableEntity);
        }

        return product;
    }

    private async Task<Customer> EnsureCustomerAsync(
        SubscriptionUser user,
        string reference,
        CancellationToken cancellationToken)
    {
        var existing = await ReadCustomerAsync(reference, cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        try
        {
            using var writeScope = MaxioWriteGuardHandler.BeginWriteScope();
            var response = await BoundedAsync(
                ct => _client.Customers.CreateCustomer(
                    new CreateCustomerRequest
                    {
                        Customer = new CreateCustomer
                        {
                            FirstName = user.FirstName,
                            LastName = user.LastName,
                            Email = user.Email,
                            Reference = reference
                        }
                    },
                    ct: ct),
                cancellationToken);
            return RequireCustomer(response.Customer);
        }
        catch (SdkException<CreateCustomerError> ex)
        {
            if (ex.Error.TryGetCustomerErrorResponse1(out _))
            {
                var concurrent = await ReadCustomerAsync(reference, cancellationToken);
                if (concurrent is not null)
                {
                    return concurrent;
                }

                throw new SubscriptionBillingException(
                    "customer_rejected",
                    "The billing customer could not be created.",
                    (int)HttpStatusCode.UnprocessableEntity,
                    ex);
            }

            if (ex.Error.TryGetRawError(out var raw))
            {
                throw ProviderWriteError(raw.StatusCode, ex);
            }

            throw InvalidProviderResponse(ex);
        }
        catch (Exception ex) when (ex is MaxioWriteReplayBlockedException or JsonException ||
                                   IsProviderTransportFailure(ex, cancellationToken))
        {
            var reconciled = await ReadCustomerAsync(reference, cancellationToken);
            if (reconciled is not null)
            {
                return reconciled;
            }

            throw ProviderUnavailable(ex);
        }
    }

    private async Task<Customer?> ReadCustomerAsync(string reference, CancellationToken cancellationToken)
    {
        try
        {
            var response = await BoundedAsync(
                ct => _client.Customers.ReadCustomerByReference(reference, ct: ct),
                cancellationToken);
            return RequireCustomer(response.Customer);
        }
        catch (SdkException<RawError> ex) when (ex.Error.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
        catch (SdkException<RawError> ex)
        {
            throw ProviderReadError(ex.Error.StatusCode, ex);
        }
        catch (JsonException ex)
        {
            throw InvalidProviderResponse(ex);
        }
        catch (Exception ex) when (IsProviderTransportFailure(ex, cancellationToken))
        {
            throw ProviderUnavailable(ex);
        }
    }

    private async Task<MappedSubscription> CreateSubscriptionAsync(
        string productHandle,
        string customerReference,
        string subscriptionReference,
        CancellationToken cancellationToken)
    {
        try
        {
            using var writeScope = MaxioWriteGuardHandler.BeginWriteScope();
            var response = await BoundedAsync(
                ct => _client.Subscriptions.CreateSubscription(
                    new CreateSubscriptionRequest
                    {
                        Subscription = new CreateSubscription
                        {
                            ProductHandle = productHandle,
                            CustomerReference = customerReference,
                            Reference = subscriptionReference,
                            PaymentCollectionMethod = CollectionMethod.Remittance
                        }
                    },
                    ct: ct),
                cancellationToken);
            return RequireSubscription(response.Subscription);
        }
        catch (SdkException<CreateSubscriptionError> ex)
        {
            if (ex.Error.TryGetErrorListResponse1(out var errorResponse))
            {
                _logger.LogWarning(
                    "Maxio rejected subscription creation for product {ProductHandle}. Validation errors: {ValidationErrors}",
                    productHandle,
                    string.Join(" | ", errorResponse.Errors));

                var reconciled = await FindSubscriptionAsync(subscriptionReference, cancellationToken);
                if (reconciled is not null)
                {
                    return reconciled;
                }

                throw new SubscriptionBillingException(
                    "subscription_rejected",
                    "Maxio rejected the subscription request.",
                    (int)HttpStatusCode.UnprocessableEntity,
                    ex);
            }

            if (ex.Error.TryGetRawError(out var raw))
            {
                throw ProviderWriteError(raw.StatusCode, ex);
            }

            throw InvalidProviderResponse(ex);
        }
        catch (Exception ex) when (ex is MaxioWriteReplayBlockedException or JsonException ||
                                   IsProviderTransportFailure(ex, cancellationToken))
        {
            var reconciled = await FindSubscriptionAsync(subscriptionReference, cancellationToken);
            if (reconciled is not null)
            {
                return reconciled;
            }

            throw new AmbiguousMaxioWriteException(ex);
        }
    }

    private async Task<MappedSubscription?> FindSubscriptionAsync(
        string reference,
        CancellationToken cancellationToken)
    {
        try
        {
            var response = await BoundedAsync(
                ct => _client.Subscriptions.FindSubscription(reference: reference, ct: ct),
                cancellationToken);
            return RequireSubscription(response.Subscription);
        }
        catch (SdkException<FindSubscriptionError> ex)
        {
            if (ex.Error.TryGetNoContent(out _))
            {
                return null;
            }

            if (ex.Error.TryGetRawError(out var raw))
            {
                if (raw.StatusCode == HttpStatusCode.NotFound)
                {
                    return null;
                }

                throw ProviderReadError(raw.StatusCode, ex);
            }

            throw InvalidProviderResponse(ex);
        }
        catch (JsonException ex)
        {
            throw InvalidProviderResponse(ex);
        }
        catch (Exception ex) when (IsProviderTransportFailure(ex, cancellationToken))
        {
            throw ProviderUnavailable(ex);
        }
    }

    private bool TryMapPlan(Product? product, out SubscriptionPlan plan)
    {
        if (product is not null &&
            product.ArchivedAt is null &&
            !string.IsNullOrWhiteSpace(product.Handle) &&
            !string.IsNullOrWhiteSpace(product.Name) &&
            product.PriceInCents is not null &&
            product.Interval is not null &&
            product.IntervalUnit is not null &&
            string.Equals(product.ProductFamily?.Handle, _options.ProductFamilyHandle, StringComparison.Ordinal))
        {
            plan = new SubscriptionPlan(
                product.Handle,
                product.Name,
                product.Description,
                product.PriceInCents.Value,
                product.Interval.Value,
                product.IntervalUnit.Value,
                product.RequireCreditCard == true);
            return true;
        }

        plan = null!;
        return false;
    }

    private static bool TryMapSubscription(Subscription? subscription, out MappedSubscription mapped)
    {
        if (subscription?.Id is not null &&
            !string.IsNullOrWhiteSpace(subscription.Reference) &&
            subscription.State is not null &&
            subscription.ProductPriceInCents is not null &&
            !string.IsNullOrWhiteSpace(subscription.Product?.Handle) &&
            !string.IsNullOrWhiteSpace(subscription.Product.Name))
        {
            mapped = new MappedSubscription(
                subscription.Id.Value,
                new SubscriptionSummary(
                    subscription.Reference,
                    subscription.Product.Handle,
                    subscription.Product.Name,
                    subscription.ProductPriceInCents.Value,
                    subscription.Currency,
                    subscription.State.Value,
                    subscription.CurrentPeriodEndsAt));
            return true;
        }

        mapped = null!;
        return false;
    }

    private static Customer RequireCustomer(Customer? customer)
    {
        if (customer?.Id is null || string.IsNullOrWhiteSpace(customer.Reference))
        {
            throw InvalidProviderResponse();
        }

        return customer;
    }

    private static MappedSubscription RequireSubscription(Subscription? subscription)
    {
        if (!TryMapSubscription(subscription, out var mapped))
        {
            throw InvalidProviderResponse();
        }

        return mapped;
    }

    private static async Task<T> BoundedAsync<T>(
        Func<CancellationToken, Task<T>> operation,
        CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(CallBudget);
        return await operation(timeout.Token);
    }

    private static bool IsProviderTransportFailure(Exception exception, CancellationToken callerToken) =>
        exception is HttpRequestException ||
        (exception is OperationCanceledException && !callerToken.IsCancellationRequested);

    private static SubscriptionBillingException ProviderConfigurationError(Exception? innerException = null) =>
        new(
            "billing_configuration_invalid",
            "The configured Maxio catalog could not be resolved.",
            (int)HttpStatusCode.BadGateway,
            innerException);

    private static SubscriptionBillingException ProviderReadError(
        HttpStatusCode statusCode,
        Exception innerException) =>
        new(
            "billing_provider_error",
            "Maxio could not complete the billing request.",
            (int)statusCode >= 500 ? (int)HttpStatusCode.ServiceUnavailable : (int)HttpStatusCode.BadGateway,
            innerException);

    private static SubscriptionBillingException ProviderWriteError(
        HttpStatusCode statusCode,
        Exception innerException) =>
        new(
            "billing_provider_error",
            "Maxio could not complete the billing request.",
            (int)statusCode >= 500 ? (int)HttpStatusCode.ServiceUnavailable : (int)statusCode,
            innerException);

    private static SubscriptionBillingException ProviderUnavailable(Exception innerException) =>
        new(
            "billing_provider_unavailable",
            "Maxio is temporarily unavailable.",
            (int)HttpStatusCode.ServiceUnavailable,
            innerException);

    private static SubscriptionBillingException InvalidProviderResponse(Exception? innerException = null) =>
        new(
            "billing_provider_response_invalid",
            "Maxio returned a response that could not be processed.",
            (int)HttpStatusCode.BadGateway,
            innerException);

    private sealed record MappedSubscription(int Id, SubscriptionSummary Summary);

    private sealed class AmbiguousMaxioWriteException : Exception
    {
        public AmbiguousMaxioWriteException(Exception innerException)
            : base("The Maxio write outcome could not be established.", innerException)
        {
        }
    }
}
