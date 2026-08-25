using System;
using System.Collections.Concurrent;
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
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.SubscriptionBilling;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Billing;

internal sealed class MaxioSubscriptionBillingService : ISubscriptionBillingService
{
    private const int ProductsPerPage = 100;
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> EnrollmentLocks = new();

    private readonly MaxioAdvancedBillingClient _client;
    private readonly CatalogContext _dbContext;
    private readonly MaxioOptions _options;
    private readonly MaxioWriteGuard _writeGuard;

    public MaxioSubscriptionBillingService(
        MaxioAdvancedBillingClient client,
        CatalogContext dbContext,
        IOptions<MaxioOptions> options,
        MaxioWriteGuard writeGuard)
    {
        _client = client;
        _dbContext = dbContext;
        _options = options.Value;
        _writeGuard = writeGuard;
    }

    public async Task<IReadOnlyList<SubscriptionPlan>> GetPlansAsync(
        CancellationToken cancellationToken = default)
    {
        var familyIdentifier = $"handle:{_options.ProductFamilyHandle}";
        var products = new List<Product>();
        for (var page = 1; ; page++)
        {
            IReadOnlyList<ProductResponse> pageItems;
            try
            {
                pageItems = await BoundedAsync(ct =>
                    _client.ProductFamilies.ListProductsForProductFamily(
                        productFamilyId: familyIdentifier,
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
                        ct: ct), cancellationToken);
            }
            catch (SdkException<ListProductsForProductFamilyError> ex)
            {
                if (ex.Error.TryGetString(out _) || ex.Error.TryGetRawError(out _))
                {
                    throw new SubscriptionBillingException(
                        "Maxio could not return subscription plans.",
                        HttpStatusCode.BadGateway,
                        ex);
                }

                throw new SubscriptionBillingException(
                    "Maxio returned an unsupported catalog error.",
                    HttpStatusCode.BadGateway,
                    ex);
            }
            catch (Exception ex) when (IsProviderBoundaryFailure(ex))
            {
                throw ProviderUnavailable(ex, cancellationToken);
            }

            products.AddRange(pageItems.Select(x => x.Product));
            if (pageItems.Count < ProductsPerPage) break;
        }

        return products
            .Where(x => x.ArchivedAt is null)
            .Select(MapPlan)
            .OrderBy(x => x.PriceInCents)
            .ThenBy(x => x.Name, StringComparer.Ordinal)
            .ToList();
    }

    public async Task<SubscriptionDetails> SubscribeAsync(
        BillingUser user,
        string productHandle,
        CancellationToken cancellationToken = default)
    {
        ValidateUser(user);
        productHandle = productHandle?.Trim() ?? string.Empty;
        if (productHandle.Length == 0)
        {
            throw new SubscriptionBillingException(
                "A product handle is required.", HttpStatusCode.BadRequest);
        }

        var plan = (await GetPlansAsync(cancellationToken))
            .SingleOrDefault(x => string.Equals(x.Handle, productHandle, StringComparison.Ordinal));
        if (plan is null)
        {
            throw new SubscriptionBillingException(
                "The requested subscription plan does not exist.", HttpStatusCode.BadRequest);
        }

        var lockKey = $"{user.Id}\n{productHandle}";
        var enrollmentLock = EnrollmentLocks.GetOrAdd(lockKey, _ => new SemaphoreSlim(1, 1));
        await enrollmentLock.WaitAsync(cancellationToken);
        try
        {
            var reference = SubscriptionReference(user.Id, productHandle);
            var claim = await GetOrCreateEnrollmentAsync(
                user.Id, productHandle, reference, cancellationToken);
            var enrollment = claim.Enrollment;
            var customer = await EnsureCustomerAsync(user, cancellationToken);
            var customerId = Required(customer.Id, "Maxio customer");

            if (enrollment.MaxioCustomerId != customerId)
            {
                enrollment.ConfirmCustomer(customerId);
                await _dbContext.SaveChangesAsync(cancellationToken);
            }

            var existing = await FindSubscriptionAsync(reference, cancellationToken);
            if (existing is not null)
            {
                ValidateOwnership(existing, customerId, productHandle);
                await ConfirmEnrollmentAsync(enrollment, existing, cancellationToken);
                return MapSubscription(existing);
            }

            if (!claim.IsOwner)
            {
                throw new SubscriptionBillingException(
                    "This subscription enrollment is already in progress. Retry shortly.",
                    HttpStatusCode.Conflict);
            }

            Subscription created;
            try
            {
                created = await CreateSubscriptionAsync(
                    customerId, productHandle, reference, cancellationToken);
            }
            catch (SubscriptionBillingException ex)
                when ((int)ex.StatusCode is >= 400 and < 500)
            {
                enrollment.ReleaseClaim();
                await _dbContext.SaveChangesAsync(cancellationToken);
                throw;
            }
            ValidateOwnership(created, customerId, productHandle);
            await ConfirmEnrollmentAsync(enrollment, created, cancellationToken);
            return MapSubscription(created);
        }
        finally
        {
            enrollmentLock.Release();
        }
    }

    public async Task<IReadOnlyList<SubscriptionDetails>> GetSubscriptionsAsync(
        BillingUser user,
        CancellationToken cancellationToken = default)
    {
        ValidateUser(user);
        var customer = await EnsureCustomerAsync(user, cancellationToken);
        var customerId = Required(customer.Id, "Maxio customer");

        IReadOnlyList<SubscriptionResponse> responses;
        try
        {
            responses = await BoundedAsync(ct =>
                _client.Customers.ListCustomerSubscriptions(customerId, ct: ct),
                cancellationToken);
        }
        catch (SdkException<RawError> ex)
        {
            throw ProviderFailure(ex.Error.StatusCode,
                "Maxio could not return subscriptions.", ex);
        }
        catch (Exception ex) when (IsProviderBoundaryFailure(ex))
        {
            throw ProviderUnavailable(ex, cancellationToken);
        }

        return responses
            .Select(x => x.Subscription ?? throw new SubscriptionBillingException(
                "Maxio returned an incomplete subscription response."))
            .Where(x => x.Customer?.Id == customerId)
            .Select(MapSubscription)
            .OrderBy(x => x.ProductName, StringComparer.Ordinal)
            .ToList();
    }

    private async Task<EnrollmentClaim> GetOrCreateEnrollmentAsync(
        string userId,
        string productHandle,
        string reference,
        CancellationToken cancellationToken)
    {
        var existing = await _dbContext.SubscriptionEnrollments.SingleOrDefaultAsync(
            x => x.UserId == userId && x.ProductHandle == productHandle,
            cancellationToken);
        if (existing is not null)
        {
            return await TryTakeExpiredClaimAsync(existing, cancellationToken);
        }

        var enrollment = new SubscriptionEnrollment(userId, productHandle, reference);
        _dbContext.SubscriptionEnrollments.Add(enrollment);
        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
            return new EnrollmentClaim(enrollment, true);
        }
        catch (DbUpdateException)
        {
            _dbContext.Entry(enrollment).State = EntityState.Detached;
            var winner = await _dbContext.SubscriptionEnrollments.SingleAsync(
                x => x.UserId == userId && x.ProductHandle == productHandle,
                cancellationToken);
            return await TryTakeExpiredClaimAsync(winner, cancellationToken);
        }
    }

    private async Task<EnrollmentClaim> TryTakeExpiredClaimAsync(
        SubscriptionEnrollment enrollment,
        CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        if (enrollment.MaxioSubscriptionId is not null || enrollment.HasActiveClaim(now))
        {
            return new EnrollmentClaim(enrollment, false);
        }

        var originalToken = enrollment.ClaimToken;
        _dbContext.Entry(enrollment).Property(x => x.ClaimToken).OriginalValue = originalToken;
        enrollment.RenewClaim(now);
        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
            return new EnrollmentClaim(enrollment, true);
        }
        catch (DbUpdateConcurrencyException)
        {
            _dbContext.Entry(enrollment).State = EntityState.Detached;
            var winner = await _dbContext.SubscriptionEnrollments.SingleAsync(
                x => x.UserId == enrollment.UserId && x.ProductHandle == enrollment.ProductHandle,
                cancellationToken);
            return new EnrollmentClaim(winner, false);
        }
    }

    private async Task<Customer> EnsureCustomerAsync(
        BillingUser user,
        CancellationToken cancellationToken)
    {
        var reference = CustomerReference(user.Id);
        var existing = await ReadCustomerAsync(reference, cancellationToken);
        if (existing is not null) return existing;

        using var write = _writeGuard.BeginWrite();
        try
        {
            var response = await BoundedAsync(ct => _client.Customers.CreateCustomer(
                body: new CreateCustomerRequest
                {
                    Customer = new CreateCustomer
                    {
                        FirstName = user.FirstName,
                        LastName = user.LastName,
                        Email = user.Email,
                        Reference = reference
                    }
                },
                ct: ct), cancellationToken);
            return response.Customer;
        }
        catch (SdkException<CreateCustomerError> ex)
        {
            if (ex.Error.TryGetCustomerErrorResponse1(out _))
            {
                var racedCustomer = await ReadCustomerAsync(reference, cancellationToken);
                if (racedCustomer is not null) return racedCustomer;
                throw new SubscriptionBillingException(
                    "Maxio rejected the customer profile.",
                    HttpStatusCode.UnprocessableEntity,
                    ex);
            }

            if (ex.Error.TryGetRawError(out var raw))
            {
                throw ProviderFailure(raw.StatusCode,
                    "Maxio could not create the customer.", ex);
            }

            throw new SubscriptionBillingException(
                "Maxio returned an unsupported customer error.",
                HttpStatusCode.BadGateway,
                ex);
        }
        catch (Exception ex) when (IsProviderBoundaryFailure(ex))
        {
            var reconciled = await ReadCustomerAsync(reference, CancellationToken.None);
            if (reconciled is not null) return reconciled;

            if (write.LastStatusCode is { } status && (int)status is >= 400 and < 500)
            {
                throw new SubscriptionBillingException(
                    "Maxio rejected the customer profile.", status, ex);
            }

            throw ProviderUnavailable(ex, cancellationToken,
                "The Maxio customer outcome could not be confirmed.");
        }
    }

    private async Task<Customer?> ReadCustomerAsync(
        string reference,
        CancellationToken cancellationToken)
    {
        try
        {
            var response = await BoundedAsync(ct =>
                _client.Customers.ReadCustomerByReference(reference, ct: ct),
                cancellationToken);
            return response.Customer;
        }
        catch (SdkException<RawError> ex) when (ex.Error.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
        catch (SdkException<RawError> ex)
        {
            throw ProviderFailure(ex.Error.StatusCode,
                "Maxio could not look up the customer.", ex);
        }
        catch (Exception ex) when (IsProviderBoundaryFailure(ex))
        {
            throw ProviderUnavailable(ex, cancellationToken);
        }
    }

    private async Task<Subscription?> FindSubscriptionAsync(
        string reference,
        CancellationToken cancellationToken)
    {
        try
        {
            var response = await BoundedAsync(ct =>
                _client.Subscriptions.FindSubscription(reference, ct: ct),
                cancellationToken);
            return response.Subscription ?? throw new SubscriptionBillingException(
                "Maxio returned an incomplete subscription response.");
        }
        catch (SdkException<FindSubscriptionError> ex)
        {
            if (ex.Error.TryGetNoContent(out _)) return null;
            if (ex.Error.TryGetRawError(out var raw))
            {
                throw ProviderFailure(raw.StatusCode,
                    "Maxio could not look up the subscription.", ex);
            }

            throw new SubscriptionBillingException(
                "Maxio returned an unsupported subscription lookup error.",
                HttpStatusCode.BadGateway,
                ex);
        }
        catch (Exception ex) when (IsProviderBoundaryFailure(ex))
        {
            throw ProviderUnavailable(ex, cancellationToken);
        }
    }

    private async Task<Subscription> CreateSubscriptionAsync(
        int customerId,
        string productHandle,
        string reference,
        CancellationToken cancellationToken)
    {
        using var write = _writeGuard.BeginWrite();
        Exception? ambiguousFailure = null;
        try
        {
            var response = await BoundedAsync(ct => _client.Subscriptions.CreateSubscription(
                body: new CreateSubscriptionRequest
                {
                    Subscription = new CreateSubscription
                    {
                        ProductHandle = productHandle,
                        CustomerId = customerId,
                        Reference = reference,
                        PaymentCollectionMethod = CollectionMethod.Remittance
                    }
                },
                ct: ct), cancellationToken);
            return response.Subscription ?? throw new SubscriptionBillingException(
                "Maxio returned an incomplete subscription response.");
        }
        catch (SdkException<CreateSubscriptionError> ex)
        {
            if (ex.Error.TryGetErrorListResponse1(out var validation))
            {
                var detail = string.Join(" ", validation.Errors.Take(3));
                throw new SubscriptionBillingException(
                    string.IsNullOrWhiteSpace(detail)
                        ? "Maxio rejected the subscription."
                        : $"Maxio rejected the subscription: {detail}",
                    HttpStatusCode.UnprocessableEntity,
                    ex);
            }

            if (ex.Error.TryGetRawError(out var raw) && (int)raw.StatusCode is >= 400 and < 500)
            {
                throw new SubscriptionBillingException(
                    "Maxio rejected the subscription.", raw.StatusCode, ex);
            }

            ambiguousFailure = ex;
        }
        catch (Exception ex) when (IsProviderBoundaryFailure(ex))
        {
            ambiguousFailure = ex;
        }

        var reconciled = await FindSubscriptionAsync(reference, CancellationToken.None);
        if (reconciled is not null) return reconciled;

        if (write.LastStatusCode is { } status && (int)status is >= 400 and < 500)
        {
            throw new SubscriptionBillingException(
                "Maxio rejected the subscription.", status, ambiguousFailure);
        }

        throw new SubscriptionBillingException(
            "The Maxio subscription outcome could not be confirmed.",
            HttpStatusCode.BadGateway,
            ambiguousFailure);
    }

    private async Task ConfirmEnrollmentAsync(
        SubscriptionEnrollment enrollment,
        Subscription subscription,
        CancellationToken cancellationToken)
    {
        var subscriptionId = Required(subscription.Id, "Maxio subscription");
        if (enrollment.MaxioSubscriptionId != subscriptionId)
        {
            enrollment.ConfirmSubscription(subscriptionId);
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
    }

    private static SubscriptionPlan MapPlan(Product product)
    {
        return new SubscriptionPlan(
            Required(product.Handle, "product handle"),
            Required(product.Name, "product name"),
            product.Description,
            Required(product.PriceInCents, "product price"),
            Required(product.Interval, "product interval"),
            Required(product.IntervalUnit?.Value, "product interval unit"));
    }

    private static SubscriptionDetails MapSubscription(Subscription subscription)
    {
        var product = subscription.Product ?? throw new SubscriptionBillingException(
            "Maxio returned a subscription without its product.");
        return new SubscriptionDetails(
            Required(subscription.Id, "subscription id"),
            Required(subscription.Reference, "subscription reference"),
            Required(product.Handle, "product handle"),
            Required(product.Name, "product name"),
            Required(subscription.ProductPriceInCents, "subscription price"),
            Required(subscription.State?.Value, "subscription state"),
            subscription.NextAssessmentAt ?? subscription.CurrentPeriodEndsAt);
    }

    private static void ValidateOwnership(
        Subscription subscription,
        int customerId,
        string productHandle)
    {
        if (subscription.Customer?.Id != customerId ||
            !string.Equals(subscription.Product?.Handle, productHandle, StringComparison.Ordinal))
        {
            throw new SubscriptionBillingException(
                "The Maxio subscription reference belongs to a different enrollment.",
                HttpStatusCode.Conflict);
        }
    }

    private static void ValidateUser(BillingUser user)
    {
        if (string.IsNullOrWhiteSpace(user.Id) ||
            string.IsNullOrWhiteSpace(user.Email) ||
            string.IsNullOrWhiteSpace(user.FirstName) ||
            string.IsNullOrWhiteSpace(user.LastName))
        {
            throw new SubscriptionBillingException(
                "The authenticated user profile is incomplete.",
                HttpStatusCode.UnprocessableEntity);
        }
    }

    private static async Task<T> BoundedAsync<T>(
        Func<CancellationToken, Task<T>> call,
        CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));
        return await call(timeout.Token);
    }

    private static bool IsProviderBoundaryFailure(Exception exception) =>
        exception is HttpRequestException or TaskCanceledException or JsonException or
            MaxioWriteResendBlockedException;

    private static SubscriptionBillingException ProviderUnavailable(
        Exception exception,
        CancellationToken callerToken,
        string message = "Maxio is temporarily unavailable.")
    {
        if (exception is TaskCanceledException && callerToken.IsCancellationRequested)
        {
            return new SubscriptionBillingException(
                "The request was cancelled.",
                HttpStatusCode.RequestTimeout,
                exception);
        }

        return new SubscriptionBillingException(message, HttpStatusCode.BadGateway, exception);
    }

    private static SubscriptionBillingException ProviderFailure(
        HttpStatusCode providerStatus,
        string message,
        Exception exception)
    {
        var status = (int)providerStatus is >= 400 and < 500
            ? providerStatus
            : HttpStatusCode.BadGateway;
        return new SubscriptionBillingException(message, status, exception);
    }

    private static string CustomerReference(string userId) => $"eshop-user-{userId}";

    private static string SubscriptionReference(string userId, string productHandle)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes($"{userId}\n{productHandle}"));
        return $"eshop-sub-{Convert.ToHexString(bytes).ToLowerInvariant()}";
    }

    private static T Required<T>(T? value, string field) where T : struct =>
        value ?? throw new SubscriptionBillingException($"Maxio omitted the {field}.");

    private static string Required(string? value, string field) =>
        !string.IsNullOrWhiteSpace(value)
            ? value
            : throw new SubscriptionBillingException($"Maxio omitted the {field}.");

    private sealed record EnrollmentClaim(SubscriptionEnrollment Enrollment, bool IsOwner);
}
