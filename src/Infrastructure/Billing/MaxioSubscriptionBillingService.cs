using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.ErrorResponse;
using MaxioAdvancedBilling.Core.Exceptions;
using MaxioAdvancedBilling.Errors;
using MaxioAdvancedBilling.Models;
using MaxioAdvancedBilling.Models.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Billing;

internal sealed class MaxioSubscriptionBillingService : ISubscriptionBillingService
{
    private const int MaxProviderDiagnosticLength = 512;
    private static readonly TimeSpan CallBudget = TimeSpan.FromSeconds(28);
    private static readonly TimeSpan LeaseDuration = TimeSpan.FromMinutes(1);
    private static readonly Regex EmailPattern = new(
        @"\b[A-Z0-9._%+-]+@[A-Z0-9.-]+\.[A-Z]{2,}\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex LongHexPattern = new(
        @"\b[A-F0-9]{32,}\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex WhitespacePattern = new(
        @"\s+",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private readonly MaxioAdvancedBillingClient _client;
    private readonly SubscriptionBillingDbContext _db;
    private readonly MaxioReferenceFactory _references;
    private readonly SubscriptionKeyedLock _keyedLock;
    private readonly MaxioCallContext _callContext;
    private readonly MaxioOptions _options;
    private readonly ILogger<MaxioSubscriptionBillingService> _logger;

    public MaxioSubscriptionBillingService(
        MaxioAdvancedBillingClient client,
        SubscriptionBillingDbContext db,
        MaxioReferenceFactory references,
        SubscriptionKeyedLock keyedLock,
        MaxioCallContext callContext,
        IOptions<MaxioOptions> options,
        ILogger<MaxioSubscriptionBillingService> logger)
    {
        _client = client;
        _db = db;
        _references = references;
        _keyedLock = keyedLock;
        _callContext = callContext;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<IReadOnlyList<SubscriptionPlan>> ListPlansAsync(CancellationToken cancellationToken)
    {
        const int pageSize = 100;
        var plans = new List<SubscriptionPlan>();

        for (var page = 1; ; page++)
        {
            IReadOnlyList<ProductResponse> responses;
            try
            {
                responses = await BoundedAsync(
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
                        perPage: pageSize,
                        ct: ct),
                    blockRepeatedWrites: false,
                    cancellationToken);
            }
            catch (SdkException<ListProductsForProductFamilyError> exception)
            {
                if (exception.Error.TryGetString(out _))
                {
                    throw Failure(HttpStatusCode.BadGateway, "Maxio catalog unavailable",
                        "The configured subscription catalog could not be found.", exception);
                }

                if (exception.Error.TryGetRawError(out var raw))
                {
                    throw FromProvider(raw.StatusCode, "list subscription plans", exception);
                }

                throw ProtocolFailure("list subscription plans", exception);
            }

            foreach (var response in responses)
            {
                var product = response.Product;
                if (product.ArchivedAt is not null || string.IsNullOrWhiteSpace(product.Handle))
                {
                    continue;
                }

                plans.Add(new SubscriptionPlan(
                    product.Handle,
                    product.Name ?? product.Handle,
                    product.Description,
                    product.PriceInCents,
                    product.Interval,
                    product.IntervalUnit?.Value,
                    product.RequireCreditCard == true));
            }

            if (responses.Count < pageSize)
            {
                return plans;
            }
        }
    }

    public async Task<SubscriptionDetails> SubscribeAsync(
        SubscriptionShopper shopper,
        string productHandle,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(productHandle))
        {
            throw Failure(HttpStatusCode.BadRequest, "Invalid subscription request",
                "A productHandle is required.");
        }

        productHandle = productHandle.Trim();
        await ValidateProductAsync(productHandle, cancellationToken);

        var subscriptionReference = _references.Subscription(shopper.UserId, productHandle);
        await using var localLock = new AsyncDisposable(
            await _keyedLock.AcquireAsync(subscriptionReference, cancellationToken));

        var enrollment = await GetOrCreateEnrollmentAsync(shopper.UserId, productHandle, cancellationToken);
        var leaseOwner = Guid.NewGuid().ToString("N");
        var ownsLease = await AcquireLeaseAsync(enrollment.Id, leaseOwner, cancellationToken);

        if (!ownsLease)
        {
            return await FindRequiredSubscriptionAsync(subscriptionReference, cancellationToken);
        }

        try
        {
            var existing = await FindSubscriptionAsync(subscriptionReference, cancellationToken);
            if (existing is not null)
            {
                await MarkSucceededAsync(enrollment.Id, existing, cancellationToken);
                return MapSubscription(existing, subscriptionReference);
            }

            var customerReference = _references.Customer(shopper.UserId);
            var customer = await EnsureCustomerAsync(shopper.Email, customerReference, cancellationToken);
            var subscription = await CreateSubscriptionAsync(
                productHandle,
                customerReference,
                subscriptionReference,
                cancellationToken);

            await MarkSucceededAsync(enrollment.Id, subscription, cancellationToken, customer.Id);
            return MapSubscription(subscription, subscriptionReference);
        }
        catch (SubscriptionBillingException exception)
        {
            await MarkFailureAsync(
                enrollment.Id,
                exception.StatusCode == HttpStatusCode.UnprocessableEntity
                    ? SubscriptionEnrollmentStatus.Rejected
                    : SubscriptionEnrollmentStatus.Pending,
                ((int)exception.StatusCode).ToString(),
                cancellationToken);
            throw;
        }
        finally
        {
            await ReleaseLeaseAsync(enrollment.Id, leaseOwner, CancellationToken.None);
        }
    }

    public async Task<IReadOnlyList<SubscriptionDetails>> ListSubscriptionsAsync(
        string userId,
        CancellationToken cancellationToken)
    {
        var reference = _references.Customer(userId);
        var customer = await ReadCustomerAsync(reference, cancellationToken);
        if (customer is null)
        {
            return Array.Empty<SubscriptionDetails>();
        }

        if (customer.Id is null)
        {
            throw ProtocolFailure("list customer subscriptions");
        }

        IReadOnlyList<SubscriptionResponse> responses;
        try
        {
            responses = await BoundedAsync(
                ct => _client.Customers.ListCustomerSubscriptions(customer.Id.Value, ct: ct),
                blockRepeatedWrites: false,
                cancellationToken);
        }
        catch (SdkException<RawError> exception)
        {
            throw FromProvider(exception.Error.StatusCode, "list customer subscriptions", exception);
        }

        return responses
            .Where(x => x.Subscription?.Product?.ProductFamily?.Handle == _options.ProductFamilyHandle)
            .Select(x => MapSubscription(
                x.Subscription!,
                x.Subscription!.Reference ?? string.Empty))
            .ToArray();
    }

    private async Task ValidateProductAsync(string productHandle, CancellationToken cancellationToken)
    {
        ProductResponse response;
        try
        {
            response = await BoundedAsync(
                ct => _client.Products.ReadProductByHandle(productHandle, ct: ct),
                blockRepeatedWrites: false,
                cancellationToken);
        }
        catch (SdkException<RawError> exception) when (exception.Error.StatusCode == HttpStatusCode.NotFound)
        {
            throw Failure(HttpStatusCode.NotFound, "Subscription plan not found",
                "The requested subscription plan does not exist.", exception);
        }
        catch (SdkException<RawError> exception)
        {
            throw FromProvider(exception.Error.StatusCode, "read subscription plan", exception);
        }

        var product = response.Product;
        if (product.ProductFamily?.Handle != _options.ProductFamilyHandle || product.ArchivedAt is not null)
        {
            throw Failure(HttpStatusCode.NotFound, "Subscription plan not found",
                "The requested subscription plan does not exist in the configured catalog.");
        }

        if (product.RequireCreditCard == true)
        {
            throw Failure(HttpStatusCode.UnprocessableEntity, "Payment method required",
                "The requested plan requires a payment method, which this subscription flow does not capture.");
        }
    }

    private async Task<Customer> EnsureCustomerAsync(
        string email,
        string reference,
        CancellationToken cancellationToken)
    {
        var existing = await ReadCustomerAsync(reference, cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        var localPart = email.Split('@', 2)[0];
        var request = new CreateCustomerRequest
        {
            Customer = new CreateCustomer
            {
                FirstName = string.IsNullOrWhiteSpace(localPart) ? "eShop" : localPart,
                LastName = "Customer",
                Email = email,
                Reference = reference
            }
        };

        try
        {
            var response = await BoundedAsync(
                ct => _client.Customers.CreateCustomer(request, ct: ct),
                blockRepeatedWrites: true,
                cancellationToken);
            return response.Customer;
        }
        catch (SdkException<CreateCustomerError> exception)
        {
            if (exception.Error.TryGetCustomerErrorResponse1(out _))
            {
                var winner = await ReadCustomerAsync(reference, cancellationToken);
                if (winner is not null)
                {
                    return winner;
                }

                throw Failure(HttpStatusCode.UnprocessableEntity, "Customer enrollment rejected",
                    "Maxio rejected the customer enrollment.", exception);
            }

            if (exception.Error.TryGetRawError(out var raw))
            {
                throw FromProvider(raw.StatusCode, "create customer", exception);
            }

            throw ProtocolFailure("create customer", exception);
        }
    }

    private async Task<Customer?> ReadCustomerAsync(string reference, CancellationToken cancellationToken)
    {
        try
        {
            var response = await BoundedAsync(
                ct => _client.Customers.ReadCustomerByReference(reference, ct: ct),
                blockRepeatedWrites: false,
                cancellationToken);
            return response.Customer;
        }
        catch (SdkException<RawError> exception) when (exception.Error.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
        catch (SdkException<RawError> exception)
        {
            throw FromProvider(exception.Error.StatusCode, "read customer", exception);
        }
    }

    private async Task<Subscription?> FindSubscriptionAsync(
        string reference,
        CancellationToken cancellationToken)
    {
        try
        {
            var response = await BoundedAsync(
                ct => _client.Subscriptions.FindSubscription(reference, ct: ct),
                blockRepeatedWrites: false,
                cancellationToken);
            return response.Subscription ?? throw ProtocolFailure("find subscription");
        }
        catch (SdkException<FindSubscriptionError> exception)
        {
            if (exception.Error.TryGetNoContent(out _))
            {
                return null;
            }

            if (exception.Error.TryGetRawError(out var raw))
            {
                throw FromProvider(raw.StatusCode, "find subscription", exception);
            }

            throw ProtocolFailure("find subscription", exception);
        }
    }

    private async Task<SubscriptionDetails> FindRequiredSubscriptionAsync(
        string reference,
        CancellationToken cancellationToken)
    {
        var subscription = await FindSubscriptionAsync(reference, cancellationToken);
        return subscription is null
            ? throw ProtocolFailure("reconcile subscription")
            : MapSubscription(subscription, reference);
    }

    private async Task<Subscription> CreateSubscriptionAsync(
        string productHandle,
        string customerReference,
        string subscriptionReference,
        CancellationToken cancellationToken)
    {
        var request = new CreateSubscriptionRequest
        {
            Subscription = new CreateSubscription
            {
                ProductHandle = productHandle,
                CustomerReference = customerReference,
                Reference = subscriptionReference,
                PaymentCollectionMethod = CollectionMethod.Remittance
            }
        };

        try
        {
            var response = await BoundedAsync(
                ct => _client.Subscriptions.CreateSubscription(request, ct: ct),
                blockRepeatedWrites: true,
                cancellationToken);
            return response.Subscription ?? throw ProtocolFailure("create subscription");
        }
        catch (SdkException<CreateSubscriptionError> exception)
        {
            if (exception.Error.TryGetErrorListResponse1(out var validation))
            {
                _logger.LogWarning(
                    "Maxio rejected subscription creation: {ValidationErrors}",
                    SanitizeProviderValidation(validation.Errors));
                throw Failure(HttpStatusCode.UnprocessableEntity, "Subscription rejected",
                    "Maxio rejected the subscription request.", exception);
            }

            if (exception.Error.TryGetRawError(out var raw))
            {
                throw FromProvider(raw.StatusCode, "create subscription", exception);
            }

            throw ProtocolFailure("create subscription", exception);
        }
    }

    private async Task<SubscriptionEnrollment> GetOrCreateEnrollmentAsync(
        string userId,
        string productHandle,
        CancellationToken cancellationToken)
    {
        var existing = await _db.SubscriptionEnrollments.SingleOrDefaultAsync(
            x => x.IntegrationScope == _references.IntegrationScope &&
                 x.UserId == userId &&
                 x.ProductHandle == productHandle,
            cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        var now = DateTimeOffset.UtcNow;
        var enrollment = new SubscriptionEnrollment
        {
            IntegrationScope = _references.IntegrationScope,
            UserId = userId,
            ProductHandle = productHandle,
            CustomerReference = _references.Customer(userId),
            SubscriptionReference = _references.Subscription(userId, productHandle),
            Status = SubscriptionEnrollmentStatus.Pending,
            ConcurrencyToken = Guid.NewGuid(),
            CreatedAt = now,
            UpdatedAt = now
        };
        _db.SubscriptionEnrollments.Add(enrollment);

        try
        {
            await _db.SaveChangesAsync(cancellationToken);
            return enrollment;
        }
        catch (DbUpdateException)
        {
            _db.Entry(enrollment).State = EntityState.Detached;
            return await _db.SubscriptionEnrollments.SingleAsync(
                x => x.IntegrationScope == _references.IntegrationScope &&
                     x.UserId == userId &&
                     x.ProductHandle == productHandle,
                cancellationToken);
        }
    }

    private async Task<bool> AcquireLeaseAsync(
        int enrollmentId,
        string owner,
        CancellationToken cancellationToken)
    {
        while (true)
        {
            var now = DateTimeOffset.UtcNow;
            if (!_db.Database.IsRelational())
            {
                var enrollment = await _db.SubscriptionEnrollments.SingleAsync(
                    x => x.Id == enrollmentId,
                    cancellationToken);
                if (enrollment.Status == SubscriptionEnrollmentStatus.Succeeded)
                {
                    return false;
                }

                enrollment.Status = SubscriptionEnrollmentStatus.Pending;
                enrollment.LeaseOwner = owner;
                enrollment.LeaseExpiresAt = now + LeaseDuration;
                enrollment.ConcurrencyToken = Guid.NewGuid();
                enrollment.UpdatedAt = now;
                await _db.SaveChangesAsync(cancellationToken);
                return true;
            }

            _db.ChangeTracker.Clear();
            var snapshot = await _db.SubscriptionEnrollments.AsNoTracking().SingleAsync(
                x => x.Id == enrollmentId,
                cancellationToken);
            if (snapshot.Status == SubscriptionEnrollmentStatus.Succeeded)
            {
                return false;
            }

            var claimed = await _db.SubscriptionEnrollments
                .Where(x => x.Id == enrollmentId &&
                            (x.LeaseOwner == null || x.LeaseExpiresAt < now))
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(x => x.Status, SubscriptionEnrollmentStatus.Pending)
                    .SetProperty(x => x.LeaseOwner, owner)
                    .SetProperty(x => x.LeaseExpiresAt, now + LeaseDuration)
                    .SetProperty(x => x.ConcurrencyToken, Guid.NewGuid())
                    .SetProperty(x => x.UpdatedAt, now),
                    cancellationToken);
            if (claimed == 1)
            {
                return true;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(200), cancellationToken);
        }
    }

    private async Task MarkSucceededAsync(
        int enrollmentId,
        Subscription subscription,
        CancellationToken cancellationToken,
        int? customerId = null)
    {
        var enrollment = await LoadEnrollmentAsync(enrollmentId, cancellationToken);
        enrollment.Status = SubscriptionEnrollmentStatus.Succeeded;
        enrollment.MaxioCustomerId = customerId ?? enrollment.MaxioCustomerId;
        enrollment.MaxioSubscriptionId = subscription.Id;
        enrollment.LastFailureCode = null;
        enrollment.ConcurrencyToken = Guid.NewGuid();
        enrollment.UpdatedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);
    }

    private async Task MarkFailureAsync(
        int enrollmentId,
        SubscriptionEnrollmentStatus status,
        string failureCode,
        CancellationToken cancellationToken)
    {
        try
        {
            var enrollment = await LoadEnrollmentAsync(enrollmentId, cancellationToken);
            enrollment.Status = status;
            enrollment.LastFailureCode = failureCode;
            enrollment.ConcurrencyToken = Guid.NewGuid();
            enrollment.UpdatedAt = DateTimeOffset.UtcNow;
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Could not persist the sanitized Maxio enrollment failure state.");
        }
    }

    private async Task ReleaseLeaseAsync(int enrollmentId, string owner, CancellationToken cancellationToken)
    {
        try
        {
            var enrollment = await LoadEnrollmentAsync(enrollmentId, cancellationToken);
            if (enrollment.LeaseOwner != owner)
            {
                return;
            }

            enrollment.LeaseOwner = null;
            enrollment.LeaseExpiresAt = null;
            enrollment.ConcurrencyToken = Guid.NewGuid();
            enrollment.UpdatedAt = DateTimeOffset.UtcNow;
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Could not release a Maxio enrollment lease; it will expire automatically.");
        }
    }

    private async Task<SubscriptionEnrollment> LoadEnrollmentAsync(
        int enrollmentId,
        CancellationToken cancellationToken)
    {
        _db.ChangeTracker.Clear();
        return await _db.SubscriptionEnrollments.SingleAsync(x => x.Id == enrollmentId, cancellationToken);
    }

    private async Task<T> BoundedAsync<T>(
        Func<CancellationToken, Task<T>> call,
        bool blockRepeatedWrites,
        CancellationToken cancellationToken)
    {
        using var callScope = _callContext.Begin(blockRepeatedWrites);
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        budget.CancelAfter(CallBudget);

        try
        {
            return await call(budget.Token);
        }
        catch (JsonException exception)
        {
            var status = _callContext.LastStatusCode;
            if (status is >= HttpStatusCode.BadRequest)
            {
                throw FromProvider(status.Value, "process Maxio response", exception);
            }

            throw ProtocolFailure("process Maxio response", exception);
        }
        catch (MaxioRepeatedWriteBlockedException exception)
        {
            throw Failure(HttpStatusCode.ServiceUnavailable, "Subscription outcome pending",
                "The Maxio write outcome is uncertain and will be reconciled on the next request.", exception);
        }
        catch (HttpRequestException exception)
        {
            throw Failure(HttpStatusCode.ServiceUnavailable, "Maxio unavailable",
                "Maxio could not be reached. Retry the request to reconcile its outcome.", exception);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw Failure(HttpStatusCode.ServiceUnavailable, "Maxio timeout",
                "Maxio did not respond within the configured request budget.");
        }
    }

    private static SubscriptionDetails MapSubscription(Subscription subscription, string fallbackReference)
    {
        var product = subscription.Product;
        if (product is null || string.IsNullOrWhiteSpace(product.Handle))
        {
            throw ProtocolFailure("map subscription product");
        }

        return new SubscriptionDetails(
            subscription.Id,
            subscription.Reference ?? fallbackReference,
            product.Handle,
            product.Name,
            subscription.ProductPriceInCents,
            subscription.State?.Value,
            subscription.NextAssessmentAt,
            product.Interval,
            product.IntervalUnit?.Value,
            subscription.Currency);
    }

    private static SubscriptionBillingException FromProvider(
        HttpStatusCode statusCode,
        string operation,
        Exception? innerException = null)
    {
        if (statusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
        {
            return Failure(HttpStatusCode.BadGateway, "Maxio authentication failed",
                "Maxio rejected the configured credentials.", innerException);
        }

        if (statusCode == HttpStatusCode.TooManyRequests || (int)statusCode >= 500)
        {
            return Failure(HttpStatusCode.ServiceUnavailable, "Maxio unavailable",
                "Maxio is temporarily unavailable.", innerException);
        }

        var safeStatus = (int)statusCode is >= 400 and < 500 ? statusCode : HttpStatusCode.BadGateway;
        return Failure(safeStatus, "Maxio request failed",
            $"Maxio rejected the request to {operation}.", innerException);
    }

    private static SubscriptionBillingException ProtocolFailure(
        string operation,
        Exception? innerException = null) =>
        Failure(HttpStatusCode.BadGateway, "Invalid Maxio response",
            $"Maxio returned an unusable response while attempting to {operation}.", innerException);

    private static SubscriptionBillingException Failure(
        HttpStatusCode statusCode,
        string title,
        string safeMessage,
        Exception? innerException = null) =>
        new(statusCode, title, safeMessage, innerException);

    private static string SanitizeProviderValidation(IReadOnlyList<string> errors)
    {
        var diagnostic = string.Join(" | ", errors
            .Where(error => !string.IsNullOrWhiteSpace(error))
            .Take(5));
        if (string.IsNullOrWhiteSpace(diagnostic))
        {
            return "Provider returned an empty validation-error list.";
        }

        diagnostic = EmailPattern.Replace(diagnostic, "[email]");
        diagnostic = LongHexPattern.Replace(diagnostic, "[reference]");
        diagnostic = WhitespacePattern.Replace(diagnostic, " ").Trim();
        return diagnostic.Length <= MaxProviderDiagnosticLength
            ? diagnostic
            : diagnostic[..MaxProviderDiagnosticLength] + "…";
    }

    private sealed class AsyncDisposable : IAsyncDisposable
    {
        private readonly IDisposable _value;

        public AsyncDisposable(IDisposable value) => _value = value;

        public ValueTask DisposeAsync()
        {
            _value.Dispose();
            return ValueTask.CompletedTask;
        }
    }
}
