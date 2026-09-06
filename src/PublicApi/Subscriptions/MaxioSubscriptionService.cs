using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
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
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.Infrastructure.Identity;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

/// <summary>
/// The sole boundary between PublicApi and Maxio. Provider identifiers are derived from the
/// authenticated application user and are never accepted from an HTTP request.
/// </summary>
public sealed class MaxioSubscriptionService : IMaxioSubscriptionService
{
    private static readonly TimeSpan EnrollmentLease = TimeSpan.FromSeconds(45);
    private static readonly TimeSpan ProviderCallBudget = TimeSpan.FromSeconds(30);

    private readonly MaxioAdvancedBillingClient _client;
    private readonly AppIdentityDbContext _identityDb;
    private readonly ILogger<MaxioSubscriptionService> _logger;
    private readonly MaxioOptions _options;

    public MaxioSubscriptionService(
        MaxioAdvancedBillingClient client,
        AppIdentityDbContext identityDb,
        ILogger<MaxioSubscriptionService> logger,
        IOptions<MaxioOptions> options)
    {
        _client = client;
        _identityDb = identityDb;
        _logger = logger;
        _options = options.Value;
    }

    public async Task<IReadOnlyList<SubscriptionPlanDto>> GetPlansAsync(CancellationToken cancellationToken)
    {
        var family = await GetConfiguredFamilyAsync(cancellationToken);
        var productFamilyId = family.Id?.ToString(CultureInfo.InvariantCulture)
            ?? throw new MaxioProviderException("Maxio returned a product family without an identifier.");

        const int perPage = 100;
        var result = new List<SubscriptionPlanDto>();
        for (var page = 1; ; page++)
        {
            IReadOnlyList<ProductResponse> response = await ExecuteAsync(ct => _client.ProductFamilies.ListProductsForProductFamily(
                productFamilyId: productFamilyId,
                dateField: null,
                filter: null,
                startDate: null,
                endDate: null,
                startDatetime: null,
                endDatetime: null,
                includeArchived: false,
                include: null,
                page: page,
                perPage: perPage,
                ct: ct), cancellationToken);

            var products = response
                .Select(x => x.Product)
                .Where(x => x is not null && x.ArchivedAt is null && !string.IsNullOrWhiteSpace(x.Handle))
                .Select(x => x!)
                .ToList();

            result.AddRange(products.Select(x => new SubscriptionPlanDto(
                x.Handle!,
                x.Name ?? x.Handle!,
                x.Description,
                x.PriceInCents,
                x.Interval,
                x.IntervalUnit?.Value)));

            if (response.Count < perPage)
            {
                break;
            }
        }

        return result.OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    public async Task<SubscriptionDto> SubscribeAsync(ApplicationUser user, string planHandle, CancellationToken cancellationToken)
    {
        ValidateCustomerDetails(user);
        if (string.IsNullOrWhiteSpace(planHandle))
        {
            throw new MaxioProviderException("A subscription plan handle is required.", 400);
        }

        var plans = await GetPlansAsync(cancellationToken);
        if (!plans.Any(x => string.Equals(x.Handle, planHandle, StringComparison.Ordinal)))
        {
            throw new MaxioProviderException("The selected subscription plan is unavailable.", 400);
        }

        var enrollment = await AcquireEnrollmentAsync(user.Id, planHandle, cancellationToken);
        if (enrollment.Status == MaxioSubscriptionEnrollment.Completed)
        {
            var knownSubscription = await FindSubscriptionAsync(enrollment.SubscriptionReference, cancellationToken);
            return knownSubscription is not null
                ? MapSubscription(knownSubscription)
                : throw new MaxioProviderException("The subscription could not be confirmed with Maxio.");
        }

        try
        {
            var customer = await GetOrCreateCustomerAsync(user, enrollment.CustomerReference, cancellationToken);
            enrollment.MaxioCustomerId = customer.Id;

            var existingSubscription = await FindSubscriptionAsync(enrollment.SubscriptionReference, cancellationToken);
            var subscription = existingSubscription ?? await CreateSubscriptionAsync(
                customer.Id ?? throw new MaxioProviderException("Maxio returned a customer without an identifier."),
                planHandle,
                enrollment.SubscriptionReference,
                cancellationToken);

            var subscriptionId = subscription.Id
                ?? throw new MaxioProviderException("Maxio returned a subscription without an identifier.");
            var confirmed = await ReadSubscriptionAsync(subscriptionId, cancellationToken);

            enrollment.MaxioSubscriptionId = subscriptionId;
            enrollment.Status = MaxioSubscriptionEnrollment.Completed;
            enrollment.LeaseExpiresAt = DateTimeOffset.MinValue;
            enrollment.UpdatedAt = DateTimeOffset.UtcNow;
            enrollment.ConcurrencyToken = Guid.NewGuid();
            await _identityDb.SaveChangesAsync(cancellationToken);

            return MapSubscription(confirmed);
        }
        catch
        {
            await MarkEnrollmentFailedAsync(enrollment);
            throw;
        }
    }

    public async Task<IReadOnlyList<SubscriptionDto>> GetMySubscriptionsAsync(ApplicationUser user, CancellationToken cancellationToken)
    {
        var customer = await FindCustomerAsync(CustomerReference(user.Id), cancellationToken);
        if (customer?.Id is null)
        {
            return Array.Empty<SubscriptionDto>();
        }

        IReadOnlyList<SubscriptionResponse> response = await ExecuteAsync(
            ct => _client.Customers.ListCustomerSubscriptions(customer.Id.Value, ct: ct), cancellationToken);

        return response
            .Select(x => x.Subscription)
            .Where(x => x is not null)
            .Select(x => MapSubscription(x!))
            .OrderByDescending(x => x.NextBillingDate)
            .ToArray();
    }

    private async Task<ProductFamily> GetConfiguredFamilyAsync(CancellationToken cancellationToken)
    {
        IReadOnlyList<ProductFamilyResponse> families = await ExecuteAsync(ct => _client.ProductFamilies.ListProductFamilies(
            dateField: null,
            startDate: null,
            endDate: null,
            startDatetime: null,
            endDatetime: null,
            ct: ct), cancellationToken);

        var configuredHandle = _options.ProductFamilyHandle;
        return families.Select(x => x.ProductFamily)
            .FirstOrDefault(x => x is not null && string.Equals(x.Handle, configuredHandle, StringComparison.Ordinal))
            ?? throw new MaxioProviderException("The configured Maxio subscription catalog was not found.", 503);
    }

    private async Task<Customer> GetOrCreateCustomerAsync(ApplicationUser user, string reference, CancellationToken cancellationToken)
    {
        var existing = await FindCustomerAsync(reference, cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        var body = new CreateCustomerRequest
        {
            Customer = new CreateCustomer
            {
                FirstName = user.FirstName!.Trim(),
                LastName = user.LastName!.Trim(),
                Email = user.Email!.Trim(),
                Reference = reference
            }
        };

        try
        {
            var created = await ExecuteAsync(ct => _client.Customers.CreateCustomer(body: body, ct: ct), cancellationToken);
            return created.Customer ?? throw new MaxioProviderException("Maxio returned an empty customer response.");
        }
        catch (SdkException<CreateCustomerError> ex)
        {
            if (ex.Error.TryGetCustomerErrorResponse1(out _))
            {
                var racedCustomer = await FindCustomerAsync(reference, cancellationToken);
                if (racedCustomer is not null)
                {
                    return racedCustomer;
                }

                throw new MaxioProviderException("Maxio rejected the customer enrollment.", 422, ex);
            }

            throw ProviderException(ex.Error.TryGetRawError(out var raw) ? (int)raw.StatusCode : null, ex);
        }
        catch (MaxioWriteRetryBlockedException ex)
        {
            var reconciled = await FindCustomerAsync(reference, cancellationToken);
            if (reconciled is not null)
            {
                return reconciled;
            }

            throw new MaxioProviderException("The customer enrollment outcome could not be confirmed.", null, ex);
        }
    }

    private async Task<Customer?> FindCustomerAsync(string reference, CancellationToken cancellationToken)
    {
        try
        {
            var response = await ExecuteAsync(ct => _client.Customers.ReadCustomerByReference(reference, ct: ct), cancellationToken);
            return response.Customer ?? throw new MaxioProviderException("Maxio returned an empty customer response.");
        }
        catch (SdkException<RawError> ex) when ((int)ex.Error.StatusCode == 404)
        {
            return null;
        }
        catch (SdkException<RawError> ex)
        {
            throw ProviderException((int)ex.Error.StatusCode, ex);
        }
    }

    private async Task<Subscription?> FindSubscriptionAsync(string reference, CancellationToken cancellationToken)
    {
        try
        {
            var response = await ExecuteAsync(ct => _client.Subscriptions.FindSubscription(reference: reference, ct: ct), cancellationToken);
            return response.Subscription;
        }
        catch (SdkException<FindSubscriptionError> ex) when (ex.Error.TryGetNoContent(out _))
        {
            return null;
        }
        catch (SdkException<FindSubscriptionError> ex)
        {
            throw ProviderException(ex.Error.TryGetRawError(out var raw) ? (int)raw.StatusCode : null, ex);
        }
    }

    private async Task<Subscription> CreateSubscriptionAsync(int customerId, string planHandle, string reference, CancellationToken cancellationToken)
    {
        var body = new CreateSubscriptionRequest
        {
            Subscription = new CreateSubscription
            {
                ProductHandle = planHandle,
                CustomerId = customerId,
                PaymentCollectionMethod = CollectionMethod.Invoice,
                Reference = reference
            }
        };

        try
        {
            var response = await ExecuteAsync(ct => _client.Subscriptions.CreateSubscription(body: body, ct: ct), cancellationToken);
            return response.Subscription ?? throw new MaxioProviderException("Maxio returned an empty subscription response.");
        }
        catch (SdkException<CreateSubscriptionError> ex)
        {
            if (ex.Error.TryGetErrorListResponse1(out var validationErrors))
            {
                var reconciled = await FindSubscriptionAsync(reference, cancellationToken);
                if (reconciled is not null)
                {
                    return reconciled;
                }

                _logger.LogWarning(
                    "Maxio rejected subscription enrollment for plan {PlanHandle}. Validation errors: {ValidationErrors}",
                    planHandle,
                    validationErrors.Errors);

                throw new MaxioProviderException("Maxio rejected the subscription enrollment.", 422, ex);
            }

            throw ProviderException(ex.Error.TryGetRawError(out var raw) ? (int)raw.StatusCode : null, ex);
        }
        catch (MaxioWriteRetryBlockedException ex)
        {
            var reconciled = await FindSubscriptionAsync(reference, cancellationToken);
            if (reconciled is not null)
            {
                return reconciled;
            }

            throw new MaxioProviderException("The subscription outcome could not be confirmed.", null, ex);
        }
    }

    private async Task<Subscription> ReadSubscriptionAsync(int subscriptionId, CancellationToken cancellationToken)
    {
        try
        {
            var response = await ExecuteAsync(ct => _client.Subscriptions.ReadSubscription(subscriptionId, include: null, ct: ct), cancellationToken);
            return response.Subscription ?? throw new MaxioProviderException("Maxio returned an empty subscription response.");
        }
        catch (SdkException<RawError> ex)
        {
            throw ProviderException((int)ex.Error.StatusCode, ex);
        }
    }

    private async Task<MaxioSubscriptionEnrollment> AcquireEnrollmentAsync(string userId, string planHandle, CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var enrollment = new MaxioSubscriptionEnrollment
        {
            UserId = userId,
            PlanHandle = planHandle,
            CustomerReference = CustomerReference(userId),
            SubscriptionReference = SubscriptionReference(userId, planHandle),
            Status = MaxioSubscriptionEnrollment.Creating,
            LeaseExpiresAt = now.Add(EnrollmentLease),
            CreatedAt = now,
            UpdatedAt = now,
            ConcurrencyToken = Guid.NewGuid()
        };

        _identityDb.MaxioSubscriptionEnrollments.Add(enrollment);
        try
        {
            await _identityDb.SaveChangesAsync(cancellationToken);
            return enrollment;
        }
        catch (DbUpdateException)
        {
            _identityDb.ChangeTracker.Clear();
        }

        var existing = await _identityDb.MaxioSubscriptionEnrollments.SingleAsync(
            x => x.UserId == userId && x.PlanHandle == planHandle, cancellationToken);
        if (existing.Status == MaxioSubscriptionEnrollment.Completed)
        {
            return existing;
        }

        if (existing.Status == MaxioSubscriptionEnrollment.Creating && existing.LeaseExpiresAt > now)
        {
            throw new SubscriptionEnrollmentInProgressException();
        }

        existing.Status = MaxioSubscriptionEnrollment.Creating;
        existing.LeaseExpiresAt = now.Add(EnrollmentLease);
        existing.UpdatedAt = now;
        existing.ConcurrencyToken = Guid.NewGuid();
        try
        {
            await _identityDb.SaveChangesAsync(cancellationToken);
            return existing;
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new SubscriptionEnrollmentInProgressException();
        }
    }

    private async Task MarkEnrollmentFailedAsync(MaxioSubscriptionEnrollment enrollment)
    {
        try
        {
            enrollment.Status = MaxioSubscriptionEnrollment.Failed;
            enrollment.LeaseExpiresAt = DateTimeOffset.MinValue;
            enrollment.UpdatedAt = DateTimeOffset.UtcNow;
            enrollment.ConcurrencyToken = Guid.NewGuid();
            await _identityDb.SaveChangesAsync(CancellationToken.None);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not persist failed Maxio enrollment state for {EnrollmentId}.", enrollment.Id);
        }
    }

    private async Task<T> ExecuteAsync<T>(Func<CancellationToken, Task<T>> operation, CancellationToken cancellationToken)
    {
        using var scope = MaxioWriteOnceHandler.BeginScope();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(ProviderCallBudget);
        try
        {
            return await operation(timeout.Token);
        }
        catch (JsonException ex)
        {
            var status = MaxioWriteOnceHandler.LastResponseStatusCode;
            throw new MaxioProviderException("Maxio returned a response that could not be processed.", IsClientError(status) ? status : null, ex);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new MaxioProviderException("Maxio is temporarily unavailable.", null, ex);
        }
    }

    private static SubscriptionDto MapSubscription(Subscription subscription) => new(
        subscription.Id,
        subscription.Reference,
        subscription.Product?.Handle,
        subscription.Product?.Name,
        subscription.ProductPriceInCents,
        subscription.CurrentBillingAmountInCents,
        subscription.State?.Value,
        subscription.NextAssessmentAt ?? subscription.CurrentPeriodEndsAt);

    private static MaxioProviderException ProviderException(int? statusCode, Exception innerException) =>
        new("Maxio could not process the request.", IsClientError(statusCode) ? statusCode : null, innerException);

    private static bool IsClientError(int? statusCode) => statusCode is >= 400 and < 500;

    private static void ValidateCustomerDetails(ApplicationUser user)
    {
        if (string.IsNullOrWhiteSpace(user.FirstName) || string.IsNullOrWhiteSpace(user.LastName) || string.IsNullOrWhiteSpace(user.Email))
        {
            throw new MaxioProviderException("Your account needs a first name, last name, and email before you can subscribe.", 400);
        }
    }

    private static string CustomerReference(string userId) => $"eshop-customer-{Hash(userId)}";
    private static string SubscriptionReference(string userId, string planHandle) => $"eshop-subscription-{Hash($"{userId}:{planHandle}")}";

    private static string Hash(string input)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(bytes).ToLowerInvariant()[..40];
    }
}
