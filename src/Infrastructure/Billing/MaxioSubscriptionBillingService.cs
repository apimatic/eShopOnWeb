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
using MaxioAdvancedBilling.Models.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Billing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Identity;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Billing;

internal sealed class MaxioSubscriptionBillingService : ISubscriptionBillingService
{
    private const int ProductPageSize = 100;
    private static readonly TimeSpan CallBudget = TimeSpan.FromSeconds(20);
    private static readonly TimeSpan ReservationLease = TimeSpan.FromMinutes(2);

    private readonly MaxioAdvancedBillingClient _client;
    private readonly MaxioOptions _options;
    private readonly AppIdentityDbContext _identityDbContext;
    private readonly SubscriptionKeyLock _keyLock;
    private readonly MaxioRequestContext _requestContext;
    private readonly ILogger<MaxioSubscriptionBillingService> _logger;

    public MaxioSubscriptionBillingService(
        MaxioAdvancedBillingClient client,
        IOptions<MaxioOptions> options,
        AppIdentityDbContext identityDbContext,
        SubscriptionKeyLock keyLock,
        MaxioRequestContext requestContext,
        ILogger<MaxioSubscriptionBillingService> logger)
    {
        _client = client;
        _options = options.Value;
        _identityDbContext = identityDbContext;
        _keyLock = keyLock;
        _requestContext = requestContext;
        _logger = logger;
    }

    public async Task<IReadOnlyList<SubscriptionPlan>> ListPlansAsync(CancellationToken cancellationToken)
    {
        var plans = new List<SubscriptionPlan>();
        for (var page = 1; page <= 100; page++)
        {
            IReadOnlyList<ProductResponse> responses;
            try
            {
                responses = await ExecuteAsync(
                    ct => _client.ProductFamilies.ListProductsForProductFamily(
                        productFamilyId: $"handle:{_options.ProductFamilyHandle}",
                        dateField: null,
                        filter: null,
                        startDate: null,
                        endDate: null,
                        startDatetime: null,
                        endDatetime: null,
                        includeArchived: false,
                        include: null,
                        page: page,
                        perPage: ProductPageSize,
                        ct: ct),
                    isWrite: false,
                    cancellationToken);
            }
            catch (SdkException<ListProductsForProductFamilyError> ex)
            {
                throw MapListProductsError(ex);
            }

            foreach (var response in responses)
            {
                var product = response.Product;
                if (product is null || product.ArchivedAt is not null ||
                    !string.Equals(product.ProductFamily?.Handle, _options.ProductFamilyHandle, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (string.IsNullOrWhiteSpace(product.Handle) || string.IsNullOrWhiteSpace(product.Name) ||
                    product.PriceInCents is null)
                {
                    throw ProviderContractFailure();
                }

                plans.Add(new SubscriptionPlan(
                    product.Handle,
                    product.Name,
                    product.Description,
                    product.PriceInCents.Value,
                    product.Interval,
                    product.IntervalUnit?.Value,
                    product.RequireCreditCard ?? false));
            }

            if (responses.Count < ProductPageSize)
            {
                return plans;
            }
        }

        throw new BillingException(
            BillingFailureKind.ProviderFailure,
            "The billing catalog is too large to process safely.");
    }

    public async Task<SubscriptionDetails> SubscribeAsync(
        BillingCustomer customer,
        string productHandle,
        CancellationToken cancellationToken)
    {
        ValidateCustomer(customer);
        var normalizedHandle = NormalizeHandle(productHandle);
        var reference = CreateSubscriptionReference(customer.UserId, normalizedHandle);

        using (await _keyLock.AcquireAsync(reference, cancellationToken))
        {
            await EnsureCustomerAsync(customer, cancellationToken);
            var product = await ReadEligibleProductAsync(normalizedHandle, cancellationToken);
            var actualHandle = product.Handle!;

            var existing = await FindSubscriptionAsync(reference, cancellationToken);
            if (existing is not null)
            {
                await ConfirmReservationAsync(reference, customer.UserId, actualHandle, existing, cancellationToken);
                return MapSubscription(existing);
            }

            var leaseId = await AcquireReservationAsync(
                reference,
                customer.UserId,
                actualHandle,
                cancellationToken);

            Subscription subscription;
            try
            {
                subscription = await CreateSubscriptionAsync(
                    actualHandle,
                    CustomerReference(customer.UserId),
                    reference,
                    cancellationToken);
            }
            catch (Exception ex) when (ex is MaxioAmbiguousWriteException ||
                                       ex is BillingException { Kind: BillingFailureKind.InvalidRequest })
            {
                var reconciled = await FindSubscriptionAsync(reference, cancellationToken);
                if (reconciled is not null)
                {
                    subscription = reconciled;
                }
                else if (ex is BillingException billingException)
                {
                    await FailReservationAsync(reference, leaseId, cancellationToken);
                    throw billingException;
                }
                else
                {
                    throw new BillingException(
                        BillingFailureKind.ProviderUnavailable,
                        "The subscription request may still be processing. Try again shortly.",
                        ex);
                }
            }

            await ConfirmReservationAsync(reference, customer.UserId, actualHandle, subscription, cancellationToken);
            return MapSubscription(subscription);
        }
    }

    public async Task<IReadOnlyList<SubscriptionDetails>> ListSubscriptionsAsync(
        string userId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(userId))
        {
            throw new BillingException(BillingFailureKind.InvalidRequest, "The authenticated user is invalid.");
        }

        var customer = await ReadCustomerAsync(CustomerReference(userId), cancellationToken);
        if (customer is null)
        {
            return Array.Empty<SubscriptionDetails>();
        }

        if (customer.Id is null)
        {
            throw ProviderContractFailure();
        }

        try
        {
            var responses = await ExecuteAsync(
                ct => _client.Customers.ListCustomerSubscriptions(customer.Id.Value, ct: ct),
                isWrite: false,
                cancellationToken);
            return responses.Select(response =>
            {
                if (response.Subscription is null)
                {
                    throw ProviderContractFailure();
                }

                return MapSubscription(response.Subscription);
            }).ToArray();
        }
        catch (SdkException<RawError> ex)
        {
            throw MapRawError(ex.Error, "Unable to retrieve subscriptions.");
        }
    }

    private async Task<Customer> EnsureCustomerAsync(
        BillingCustomer customer,
        CancellationToken cancellationToken)
    {
        var reference = CustomerReference(customer.UserId);
        var existing = await ReadCustomerAsync(reference, cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        try
        {
            var response = await ExecuteAsync(
                ct => _client.Customers.CreateCustomer(
                    new CreateCustomerRequest
                    {
                        Customer = new CreateCustomer
                        {
                            FirstName = customer.FirstName,
                            LastName = customer.LastName,
                            Email = customer.Email,
                            Reference = reference
                        }
                    },
                    ct: ct),
                isWrite: true,
                cancellationToken);
            return RequireCustomer(response);
        }
        catch (SdkException<CreateCustomerError> ex)
        {
            if (ex.Error.TryGetCustomerErrorResponse1(out _))
            {
                var racedCustomer = await ReadCustomerAsync(reference, cancellationToken);
                if (racedCustomer is not null)
                {
                    return racedCustomer;
                }

                throw new BillingException(
                    BillingFailureKind.InvalidRequest,
                    "The billing customer could not be created.",
                    ex);
            }

            if (ex.Error.TryGetRawError(out var raw))
            {
                throw MapRawError(raw, "The billing customer could not be created.", ex);
            }

            throw ProviderContractFailure(ex);
        }
        catch (MaxioAmbiguousWriteException ex)
        {
            var reconciled = await ReadCustomerAsync(reference, cancellationToken);
            if (reconciled is not null)
            {
                return reconciled;
            }

            throw new BillingException(
                BillingFailureKind.ProviderUnavailable,
                "The billing customer request may still be processing. Try again shortly.",
                ex);
        }
    }

    private async Task<Product> ReadEligibleProductAsync(
        string handle,
        CancellationToken cancellationToken)
    {
        try
        {
            var response = await ExecuteAsync(
                ct => _client.Products.ReadProductByHandle(handle, ct: ct),
                isWrite: false,
                cancellationToken);
            var product = response.Product;
            if (product is null || product.ArchivedAt is not null ||
                string.IsNullOrWhiteSpace(product.Handle) ||
                !string.Equals(product.ProductFamily?.Handle, _options.ProductFamilyHandle, StringComparison.OrdinalIgnoreCase))
            {
                throw new BillingException(
                    BillingFailureKind.NotFound,
                    "The requested subscription plan is not available.");
            }

            return product;
        }
        catch (SdkException<RawError> ex) when (ex.Error.StatusCode == HttpStatusCode.NotFound)
        {
            throw new BillingException(
                BillingFailureKind.NotFound,
                "The requested subscription plan was not found.",
                ex);
        }
        catch (SdkException<RawError> ex)
        {
            throw MapRawError(ex.Error, "Unable to validate the subscription plan.", ex);
        }
    }

    private async Task<Customer?> ReadCustomerAsync(string reference, CancellationToken cancellationToken)
    {
        try
        {
            var response = await ExecuteAsync(
                ct => _client.Customers.ReadCustomerByReference(reference, ct: ct),
                isWrite: false,
                cancellationToken);
            return RequireCustomer(response);
        }
        catch (SdkException<RawError> ex) when (ex.Error.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
        catch (SdkException<RawError> ex)
        {
            throw MapRawError(ex.Error, "Unable to retrieve the billing customer.", ex);
        }
    }

    private async Task<Subscription?> FindSubscriptionAsync(
        string reference,
        CancellationToken cancellationToken)
    {
        try
        {
            var response = await ExecuteAsync(
                ct => _client.Subscriptions.FindSubscription(reference, ct: ct),
                isWrite: false,
                cancellationToken);
            return response.Subscription ?? throw ProviderContractFailure();
        }
        catch (SdkException<FindSubscriptionError> ex)
        {
            if (ex.Error.TryGetNoContent(out _))
            {
                return null;
            }

            if (ex.Error.TryGetRawError(out var raw))
            {
                throw MapRawError(raw, "Unable to look up the subscription.", ex);
            }

            throw ProviderContractFailure(ex);
        }
    }

    private async Task<Subscription> CreateSubscriptionAsync(
        string productHandle,
        string customerReference,
        string subscriptionReference,
        CancellationToken cancellationToken)
    {
        try
        {
            var response = await ExecuteAsync(
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
                isWrite: true,
                cancellationToken);
            return response.Subscription ?? throw ProviderContractFailure();
        }
        catch (SdkException<CreateSubscriptionError> ex)
        {
            if (ex.Error.TryGetErrorListResponse1(out var errorResponse))
            {
                _logger.LogWarning(
                    "Maxio rejected subscription creation with validation errors: {ValidationErrors}",
                    FormatValidationErrors(errorResponse.Errors));
                throw new BillingException(
                    BillingFailureKind.InvalidRequest,
                    "Maxio rejected the subscription request.",
                    ex);
            }

            if (ex.Error.TryGetRawError(out var raw))
            {
                throw MapRawError(raw, "The subscription could not be created.", ex);
            }

            throw ProviderContractFailure(ex);
        }
    }

    private async Task<T> ExecuteAsync<T>(
        Func<CancellationToken, Task<T>> operation,
        bool isWrite,
        CancellationToken cancellationToken)
    {
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        budget.CancelAfter(CallBudget);
        using var requestScope = _requestContext.Begin(isWrite);

        try
        {
            return await operation(budget.Token);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (MaxioRepeatWritePreventedException ex)
        {
            throw new MaxioAmbiguousWriteException(ex);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            if (isWrite)
            {
                throw new MaxioAmbiguousWriteException(ex);
            }

            throw new BillingException(
                BillingFailureKind.ProviderUnavailable,
                "The billing service is temporarily unavailable.",
                ex);
        }
        catch (JsonException ex)
        {
            if (isWrite && (requestScope.StatusCode is null || requestScope.StatusCode < HttpStatusCode.BadRequest))
            {
                throw new MaxioAmbiguousWriteException(ex);
            }

            throw MapUnreadableResponse(requestScope.StatusCode, ex);
        }
    }

    private async Task<string> AcquireReservationAsync(
        string reference,
        string userId,
        string productHandle,
        CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var leaseId = Guid.NewGuid().ToString("D");
        var enrollment = await _identityDbContext.SubscriptionEnrollments
            .SingleOrDefaultAsync(x => x.Reference == reference, cancellationToken);

        if (enrollment is null)
        {
            enrollment = new SubscriptionEnrollment
            {
                Reference = reference,
                UserId = userId,
                ProductHandle = productHandle,
                Status = "Pending",
                LeaseId = leaseId,
                LeaseExpiresAt = now.Add(ReservationLease),
                UpdatedAt = now
            };
            _identityDbContext.SubscriptionEnrollments.Add(enrollment);
            try
            {
                await _identityDbContext.SaveChangesAsync(cancellationToken);
                return leaseId;
            }
            catch (DbUpdateException)
            {
                _identityDbContext.ChangeTracker.Clear();
                enrollment = await _identityDbContext.SubscriptionEnrollments
                    .SingleAsync(x => x.Reference == reference, cancellationToken);
            }
        }

        if (enrollment.Status == "Pending" && enrollment.LeaseExpiresAt > now)
        {
            throw new BillingException(
                BillingFailureKind.Conflict,
                "A subscription request for this plan is already in progress.");
        }

        if (enrollment.Status == "Confirmed")
        {
            throw new BillingException(
                BillingFailureKind.Conflict,
                "The existing subscription could not be reconciled with Maxio.");
        }

        enrollment.Status = "Pending";
        enrollment.LeaseId = leaseId;
        enrollment.LeaseExpiresAt = now.Add(ReservationLease);
        enrollment.UpdatedAt = now;
        await _identityDbContext.SaveChangesAsync(cancellationToken);
        return leaseId;
    }

    private async Task ConfirmReservationAsync(
        string reference,
        string userId,
        string productHandle,
        Subscription subscription,
        CancellationToken cancellationToken)
    {
        var enrollment = await _identityDbContext.SubscriptionEnrollments
            .SingleOrDefaultAsync(x => x.Reference == reference, cancellationToken);
        if (enrollment is null)
        {
            enrollment = new SubscriptionEnrollment { Reference = reference };
            _identityDbContext.SubscriptionEnrollments.Add(enrollment);
        }

        enrollment.UserId = userId;
        enrollment.ProductHandle = productHandle;
        enrollment.Status = "Confirmed";
        enrollment.LeaseId = null;
        enrollment.LeaseExpiresAt = null;
        enrollment.MaxioSubscriptionId = subscription.Id;
        enrollment.UpdatedAt = DateTimeOffset.UtcNow;
        await _identityDbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task FailReservationAsync(
        string reference,
        string leaseId,
        CancellationToken cancellationToken)
    {
        var enrollment = await _identityDbContext.SubscriptionEnrollments
            .SingleOrDefaultAsync(x => x.Reference == reference && x.LeaseId == leaseId, cancellationToken);
        if (enrollment is null)
        {
            return;
        }

        enrollment.Status = "Failed";
        enrollment.LeaseId = null;
        enrollment.LeaseExpiresAt = null;
        enrollment.UpdatedAt = DateTimeOffset.UtcNow;
        await _identityDbContext.SaveChangesAsync(cancellationToken);
    }

    private static Customer RequireCustomer(CustomerResponse response)
    {
        var customer = response.Customer;
        if (customer is null || customer.Id is null || string.IsNullOrWhiteSpace(customer.Reference))
        {
            throw ProviderContractFailure();
        }

        return customer;
    }

    private static SubscriptionDetails MapSubscription(Subscription subscription)
    {
        var product = subscription.Product;
        if (subscription.Id is null || string.IsNullOrWhiteSpace(subscription.Reference) ||
            subscription.ProductPriceInCents is null || string.IsNullOrWhiteSpace(subscription.Currency) ||
            subscription.State is null || product is null || string.IsNullOrWhiteSpace(product.Handle) ||
            string.IsNullOrWhiteSpace(product.Name))
        {
            throw ProviderContractFailure();
        }

        return new SubscriptionDetails(
            subscription.Id.Value,
            subscription.Reference,
            product.Handle,
            product.Name,
            subscription.ProductPriceInCents.Value,
            subscription.Currency,
            subscription.State.Value,
            subscription.NextAssessmentAt);
    }

    private static void ValidateCustomer(BillingCustomer customer)
    {
        if (string.IsNullOrWhiteSpace(customer.UserId) || string.IsNullOrWhiteSpace(customer.Email) ||
            string.IsNullOrWhiteSpace(customer.FirstName) || string.IsNullOrWhiteSpace(customer.LastName))
        {
            throw new BillingException(
                BillingFailureKind.InvalidRequest,
                "The authenticated user's billing profile is incomplete.");
        }
    }

    private static string NormalizeHandle(string handle)
    {
        if (string.IsNullOrWhiteSpace(handle))
        {
            throw new BillingException(BillingFailureKind.InvalidRequest, "A product handle is required.");
        }

        return handle.Trim();
    }

    private static string CustomerReference(string userId) => $"eshop-user-{userId}";

    private static string CreateSubscriptionReference(string userId, string productHandle)
    {
        var input = Encoding.UTF8.GetBytes($"{userId}\n{productHandle.ToLowerInvariant()}");
        var hash = Convert.ToHexString(SHA256.HashData(input)).ToLowerInvariant();
        return $"eshop-sub-{hash[..40]}";
    }

    private static BillingException MapListProductsError(
        SdkException<ListProductsForProductFamilyError> exception)
    {
        if (exception.Error.TryGetString(out _))
        {
            return new BillingException(
                BillingFailureKind.ProviderFailure,
                "The configured Maxio product family was not found.",
                exception);
        }

        if (exception.Error.TryGetRawError(out var raw))
        {
            return MapRawError(raw, "Unable to retrieve subscription plans.", exception);
        }

        return ProviderContractFailure(exception);
    }

    private static BillingException MapRawError(
        RawError raw,
        string safeMessage,
        Exception? exception = null)
    {
        var status = raw.StatusCode;
        var kind = status switch
        {
            HttpStatusCode.NotFound => BillingFailureKind.NotFound,
            HttpStatusCode.Conflict => BillingFailureKind.Conflict,
            HttpStatusCode.BadRequest or HttpStatusCode.UnprocessableEntity => BillingFailureKind.InvalidRequest,
            HttpStatusCode.RequestTimeout or HttpStatusCode.TooManyRequests => BillingFailureKind.ProviderUnavailable,
            _ when (int)status >= 500 => BillingFailureKind.ProviderUnavailable,
            _ => BillingFailureKind.ProviderFailure
        };
        return new BillingException(kind, safeMessage, exception);
    }

    private static BillingException MapUnreadableResponse(HttpStatusCode? status, JsonException exception)
    {
        var kind = status switch
        {
            HttpStatusCode.NotFound => BillingFailureKind.NotFound,
            HttpStatusCode.Conflict => BillingFailureKind.Conflict,
            HttpStatusCode.BadRequest or HttpStatusCode.UnprocessableEntity => BillingFailureKind.InvalidRequest,
            _ => BillingFailureKind.ProviderFailure
        };
        return new BillingException(kind, "The billing service returned an unreadable response.", exception);
    }

    private static BillingException ProviderContractFailure(Exception? exception = null) =>
        new(
            BillingFailureKind.ProviderFailure,
            "The billing service returned an incomplete response.",
            exception);

    private static string FormatValidationErrors(IReadOnlyList<string> errors)
    {
        const int maximumErrorLength = 300;
        const int maximumErrorCount = 10;
        var safeErrors = errors
            .Take(maximumErrorCount)
            .Select(error => (error ?? string.Empty)
                .Replace('\r', ' ')
                .Replace('\n', ' ')
                .Trim())
            .Select(error => error.Length <= maximumErrorLength
                ? error
                : error[..maximumErrorLength]);
        return string.Join(" | ", safeErrors);
    }
}
