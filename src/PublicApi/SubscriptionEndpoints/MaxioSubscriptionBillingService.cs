using System;
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
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.ErrorResponse;
using MaxioAdvancedBilling.Core.Exceptions;
using MaxioAdvancedBilling.Errors;
using MaxioAdvancedBilling.Models;
using MaxioAdvancedBilling.Models.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.Infrastructure.Identity;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public sealed class MaxioSubscriptionBillingService : ISubscriptionBillingService
{
    private const int PageSize = 100;
    private static readonly TimeSpan CallBudget = TimeSpan.FromSeconds(25);
    private static readonly TimeSpan EnrollmentLease = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan PlanCacheDuration = TimeSpan.FromMinutes(2);
    private readonly MaxioAdvancedBillingClient _client;
    private readonly MaxioOptions _options;
    private readonly AppIdentityDbContext _identityDb;
    private readonly IMemoryCache _cache;
    private readonly MaxioWriteGuard _writeGuard;
    private readonly SubscriptionKeyLock _keyLock;
    private readonly ILogger<MaxioSubscriptionBillingService> _logger;

    public MaxioSubscriptionBillingService(
        MaxioAdvancedBillingClient client,
        IOptions<MaxioOptions> options,
        AppIdentityDbContext identityDb,
        IMemoryCache cache,
        MaxioWriteGuard writeGuard,
        SubscriptionKeyLock keyLock,
        ILogger<MaxioSubscriptionBillingService> logger)
    {
        _client = client;
        _options = options.Value;
        _identityDb = identityDb;
        _cache = cache;
        _writeGuard = writeGuard;
        _keyLock = keyLock;
        _logger = logger;
    }

    public Task<IReadOnlyList<SubscriptionPlanDto>> ListPlansAsync(CancellationToken cancellationToken) =>
        WithinBudgetAsync(ListPlansCoreAsync, cancellationToken);

    public Task<SubscriptionDto> SubscribeAsync(
        ApplicationUser user,
        string planHandle,
        CancellationToken cancellationToken) =>
        WithinBudgetAsync(ct => SubscribeCoreAsync(user, planHandle, ct), cancellationToken);

    public Task<IReadOnlyList<SubscriptionDto>> ListMySubscriptionsAsync(
        ApplicationUser user,
        CancellationToken cancellationToken) =>
        WithinBudgetAsync(ct => ListMySubscriptionsCoreAsync(user, ct), cancellationToken);

    private async Task<IReadOnlyList<SubscriptionPlanDto>> ListPlansCoreAsync(CancellationToken ct)
    {
        var cacheKey = $"maxio-plans:{_options.ProductFamilyHandle}";
        if (_cache.TryGetValue(cacheKey, out IReadOnlyList<SubscriptionPlanDto>? cached) && cached is not null)
        {
            return cached;
        }

        try
        {
            var families = await _client.ProductFamilies.ListProductFamilies(
                dateField: null,
                startDate: null,
                endDate: null,
                startDatetime: null,
                endDatetime: null,
                ct: ct);
            var matches = families
                .Select(x => x.ProductFamily)
                .Where(x => x is not null && x.ArchivedAt is null &&
                            string.Equals(x.Handle, _options.ProductFamilyHandle, StringComparison.Ordinal))
                .ToList();
            if (matches.Count != 1 || matches[0]!.Id is null)
            {
                throw SubscriptionBillingException.Configuration(
                    "The configured Maxio product family could not be resolved uniquely by handle.");
            }

            var plans = new List<SubscriptionPlanDto>();
            for (var page = 1; ; page++)
            {
                IReadOnlyList<ProductResponse> response;
                try
                {
                    response = await _client.ProductFamilies.ListProductsForProductFamily(
                        productFamilyId: matches[0]!.Id!.Value.ToString(CultureInfo.InvariantCulture),
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
                        ct: ct);
                }
                catch (SdkException<ListProductsForProductFamilyError> ex)
                {
                    if (ex.Error.TryGetString(out _))
                    {
                        throw SubscriptionBillingException.Configuration(
                            "The configured Maxio product family is not available.", ex);
                    }
                    if (ex.Error.TryGetRawError(out var raw))
                    {
                        throw FromRawError(raw, "Maxio could not list subscription plans.", ex);
                    }
                    throw SubscriptionBillingException.Dependency("Maxio could not list subscription plans.", ex);
                }

                plans.AddRange(response
                    .Select(x => x.Product)
                    .Where(x => x.ArchivedAt is null)
                    .Select(MapPlan));
                if (response.Count < PageSize)
                {
                    break;
                }
            }

            var result = plans.OrderBy(x => x.PriceInCents).ThenBy(x => x.Handle, StringComparer.Ordinal).ToList();
            _cache.Set(cacheKey, result, PlanCacheDuration);
            return result;
        }
        catch (SdkException<RawError> ex)
        {
            throw FromRawError(ex.Error, "Maxio could not load the subscription catalog.", ex);
        }
    }

    private async Task<SubscriptionDto> SubscribeCoreAsync(ApplicationUser user, string planHandle, CancellationToken ct)
    {
        if (!IsValidPlanHandle(planHandle))
        {
            throw SubscriptionBillingException.Rejected("A valid plan handle is required.");
        }

        var normalizedHandle = planHandle;
        var plan = await ReadPlanAsync(normalizedHandle, ct);
        var reference = SubscriptionReference(user.Id, normalizedHandle);
        using var keyedLock = await _keyLock.AcquireAsync($"{user.Id}\n{normalizedHandle}", ct);
        var (enrollment, ownsLease) = await ReserveEnrollmentAsync(user.Id, normalizedHandle, reference, ct);

        var existing = await FindSubscriptionAsync(reference, ct);
        if (existing is not null)
        {
            ValidateSubscriptionOwnership(existing, user.Id, normalizedHandle);
            await CompleteEnrollmentAsync(enrollment, existing.Id, ct);
            return MapSubscription(existing);
        }

        if (!ownsLease)
        {
            throw SubscriptionBillingException.Conflict(
                "Another request is creating this subscription. Retry the same request shortly to retrieve its result.");
        }

        var customerReference = CustomerReference(user.Id);
        await EnsureCustomerAsync(user, customerReference, ct);

        MaxioAdvancedBilling.Models.Subscription created;
        try
        {
            created = await CreateSubscriptionAsync(normalizedHandle, customerReference, reference, ct);
        }
        catch (UnknownWriteOutcomeException ex)
        {
            _logger.LogWarning(ex, "Reconciling Maxio subscription write with reference {Reference}", reference);
            var reconciled = await FindSubscriptionAsync(reference, ct);
            if (reconciled is null)
            {
                await MarkRetryableAsync(enrollment, ct);
                throw SubscriptionBillingException.Dependency(
                    "The subscription outcome is not yet known. Retry the same request to reconcile it safely.", ex);
            }
            created = reconciled;
        }
        catch
        {
            await MarkRetryableAsync(enrollment, ct);
            throw;
        }

        ValidateSubscriptionOwnership(created, user.Id, normalizedHandle);
        await CompleteEnrollmentAsync(enrollment, created.Id, ct);
        _cache.Remove($"maxio-plans:{_options.ProductFamilyHandle}");
        return MapSubscription(created, plan);
    }

    private async Task<IReadOnlyList<SubscriptionDto>> ListMySubscriptionsCoreAsync(ApplicationUser user, CancellationToken ct)
    {
        var customer = await ReadCustomerAsync(CustomerReference(user.Id), ct);
        if (customer is null)
        {
            return Array.Empty<SubscriptionDto>();
        }
        if (customer.Id is null)
        {
            throw SubscriptionBillingException.Dependency("Maxio returned an incomplete customer record.");
        }

        try
        {
            var responses = await _client.Customers.ListCustomerSubscriptions(customer.Id.Value, ct: ct);
            return responses
                .Select(x => x.Subscription)
                .Where(x => x is not null &&
                            string.Equals(x.Product?.ProductFamily?.Handle, _options.ProductFamilyHandle, StringComparison.Ordinal))
                .Select(x => MapSubscription(x!))
                .OrderBy(x => x.PlanName, StringComparer.Ordinal)
                .ToList();
        }
        catch (SdkException<RawError> ex)
        {
            throw FromRawError(ex.Error, "Maxio could not list this customer's subscriptions.", ex);
        }
    }

    private async Task<Product> ReadPlanAsync(string handle, CancellationToken ct)
    {
        try
        {
            var product = (await _client.Products.ReadProductByHandle(handle, ct: ct)).Product;
            if (product.ArchivedAt is not null || !string.Equals(product.Handle, handle, StringComparison.Ordinal) ||
                !string.Equals(product.ProductFamily?.Handle, _options.ProductFamilyHandle, StringComparison.Ordinal))
            {
                throw SubscriptionBillingException.PlanNotFound(handle);
            }
            _ = MapPlan(product);
            return product;
        }
        catch (SdkException<RawError> ex) when (ex.Error.StatusCode == HttpStatusCode.NotFound)
        {
            throw SubscriptionBillingException.PlanNotFound(handle);
        }
        catch (SdkException<RawError> ex)
        {
            throw FromRawError(ex.Error, "Maxio could not validate the selected subscription plan.", ex);
        }
    }

    private async Task<Customer?> ReadCustomerAsync(string reference, CancellationToken ct)
    {
        try
        {
            return (await _client.Customers.ReadCustomerByReference(reference, ct: ct)).Customer;
        }
        catch (SdkException<RawError> ex) when (ex.Error.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
        catch (SdkException<RawError> ex)
        {
            throw FromRawError(ex.Error, "Maxio could not resolve the billing customer.", ex);
        }
    }

    private async Task<Customer> EnsureCustomerAsync(ApplicationUser user, string reference, CancellationToken ct)
    {
        var existing = await ReadCustomerAsync(reference, ct);
        if (existing is not null)
        {
            return existing;
        }

        var email = user.Email ?? user.UserName;
        if (string.IsNullOrWhiteSpace(email))
        {
            throw SubscriptionBillingException.Configuration("The authenticated user does not have an email address.");
        }
        var localPart = email.Split('@', 2)[0];
        var firstName = string.IsNullOrWhiteSpace(localPart) ? "eShop" : localPart;
        var body = new MaxioAdvancedBilling.Models.CreateCustomerRequest
        {
            Customer = new CreateCustomer
            {
                FirstName = firstName,
                LastName = "Customer",
                Email = email,
                Reference = reference
            }
        };

        try
        {
            using var writeScope = _writeGuard.BeginSinglePostScope();
            return (await _client.Customers.CreateCustomer(body, ct: ct)).Customer;
        }
        catch (SdkException<CreateCustomerError> ex)
        {
            if (ex.Error.TryGetCustomerErrorResponse1(out _))
            {
                var raced = await ReadCustomerAsync(reference, ct);
                if (raced is not null)
                {
                    return raced;
                }
                throw SubscriptionBillingException.Rejected("Maxio rejected the billing customer details.", ex);
            }
            if (ex.Error.TryGetRawError(out var raw))
            {
                throw FromRawError(raw, "Maxio could not create the billing customer.", ex);
            }
            throw SubscriptionBillingException.Dependency("Maxio could not create the billing customer.", ex);
        }
        catch (Exception ex) when (ex is MaxioWriteReplayPreventedException or HttpRequestException or TaskCanceledException)
        {
            var raced = await ReadCustomerAsync(reference, ct);
            return raced ?? throw SubscriptionBillingException.Dependency(
                "The billing customer outcome is not yet known. Retry the request safely.", ex);
        }
    }

    private async Task<MaxioAdvancedBilling.Models.Subscription?> FindSubscriptionAsync(string reference, CancellationToken ct)
    {
        try
        {
            return (await _client.Subscriptions.FindSubscription(reference, ct: ct)).Subscription;
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
                throw FromRawError(raw, "Maxio could not reconcile the subscription.", ex);
            }
            throw SubscriptionBillingException.Dependency("Maxio could not reconcile the subscription.", ex);
        }
    }

    private async Task<MaxioAdvancedBilling.Models.Subscription> CreateSubscriptionAsync(
        string planHandle,
        string customerReference,
        string reference,
        CancellationToken ct)
    {
        var body = new MaxioAdvancedBilling.Models.CreateSubscriptionRequest
        {
            Subscription = new MaxioAdvancedBilling.Models.CreateSubscription
            {
                ProductHandle = planHandle,
                CustomerReference = customerReference,
                Reference = reference,
                PaymentCollectionMethod = CollectionMethod.Remittance
            }
        };

        try
        {
            using var writeScope = _writeGuard.BeginSinglePostScope();
            var response = await _client.Subscriptions.CreateSubscription(body, ct: ct);
            return response?.Subscription ??
                   throw SubscriptionBillingException.Dependency("Maxio returned an incomplete subscription record.");
        }
        catch (SdkException<CreateSubscriptionError> ex)
        {
            if (ex.Error.TryGetErrorListResponse1(out var details))
            {
                _logger.LogWarning(
                    "Maxio rejected a subscription request with {ErrorCount} validation error(s)",
                    details.Errors.Count);
                throw SubscriptionBillingException.Rejected("Maxio rejected the subscription request.", ex);
            }
            if (ex.Error.TryGetRawError(out var raw))
            {
                throw FromRawError(raw, "Maxio could not create the subscription.", ex);
            }
            throw SubscriptionBillingException.Dependency("Maxio could not create the subscription.", ex);
        }
        catch (Exception ex) when (ex is MaxioWriteReplayPreventedException or HttpRequestException or TaskCanceledException)
        {
            throw new UnknownWriteOutcomeException(ex);
        }
    }

    private async Task<(SubscriptionEnrollment Enrollment, bool OwnsLease)> ReserveEnrollmentAsync(
        string userId,
        string planHandle,
        string reference,
        CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        var leaseId = Guid.NewGuid().ToString("N");
        var enrollment = await _identityDb.SubscriptionEnrollments
            .SingleOrDefaultAsync(x => x.UserId == userId && x.PlanHandle == planHandle, ct);
        if (enrollment is null)
        {
            enrollment = new SubscriptionEnrollment
            {
                UserId = userId,
                PlanHandle = planHandle,
                SubscriptionReference = reference,
                Status = SubscriptionEnrollmentStatus.Processing,
                LeaseId = leaseId,
                LeaseExpiresAt = now.Add(EnrollmentLease),
                UpdatedAt = now
            };
            _identityDb.SubscriptionEnrollments.Add(enrollment);
            try
            {
                await _identityDb.SaveChangesAsync(ct);
                return (enrollment, true);
            }
            catch (DbUpdateException)
            {
                _identityDb.Entry(enrollment).State = EntityState.Detached;
                enrollment = await _identityDb.SubscriptionEnrollments
                    .SingleAsync(x => x.UserId == userId && x.PlanHandle == planHandle, ct);
            }
        }

        if (enrollment.Status == SubscriptionEnrollmentStatus.Completed)
        {
            return (enrollment, false);
        }
        if (enrollment.LeaseExpiresAt > now && enrollment.Status == SubscriptionEnrollmentStatus.Processing)
        {
            return (enrollment, false);
        }

        enrollment.Status = SubscriptionEnrollmentStatus.Processing;
        enrollment.LeaseId = leaseId;
        enrollment.LeaseExpiresAt = now.Add(EnrollmentLease);
        enrollment.UpdatedAt = now;
        enrollment.ConcurrencyToken = Guid.NewGuid().ToString("N");
        try
        {
            await _identityDb.SaveChangesAsync(ct);
            return (enrollment, true);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw SubscriptionBillingException.Conflict(
                "Another request acquired this subscription enrollment. Retry shortly.");
        }
    }

    private async Task CompleteEnrollmentAsync(SubscriptionEnrollment enrollment, int? subscriptionId, CancellationToken ct)
    {
        enrollment.Status = SubscriptionEnrollmentStatus.Completed;
        enrollment.MaxioSubscriptionId = subscriptionId;
        enrollment.LeaseId = null;
        enrollment.LeaseExpiresAt = null;
        enrollment.UpdatedAt = DateTimeOffset.UtcNow;
        enrollment.ConcurrencyToken = Guid.NewGuid().ToString("N");
        await _identityDb.SaveChangesAsync(ct);
    }

    private async Task MarkRetryableAsync(SubscriptionEnrollment enrollment, CancellationToken ct)
    {
        enrollment.Status = SubscriptionEnrollmentStatus.Retryable;
        enrollment.LeaseId = null;
        enrollment.LeaseExpiresAt = null;
        enrollment.UpdatedAt = DateTimeOffset.UtcNow;
        enrollment.ConcurrencyToken = Guid.NewGuid().ToString("N");
        try
        {
            await _identityDb.SaveChangesAsync(ct);
        }
        catch (Exception ex) when (ex is DbUpdateException or OperationCanceledException)
        {
            _logger.LogWarning(ex, "Could not mark subscription enrollment {EnrollmentId} retryable", enrollment.Id);
        }
    }

    private void ValidateSubscriptionOwnership(MaxioAdvancedBilling.Models.Subscription subscription, string userId, string planHandle)
    {
        if (!string.Equals(subscription.Customer?.Reference, CustomerReference(userId), StringComparison.Ordinal) ||
            !string.Equals(subscription.Product?.Handle, planHandle, StringComparison.Ordinal) ||
            !string.Equals(subscription.Product?.ProductFamily?.Handle, _options.ProductFamilyHandle, StringComparison.Ordinal))
        {
            throw SubscriptionBillingException.Conflict(
                "The existing Maxio subscription reference does not match this user and plan.");
        }
    }

    private static SubscriptionPlanDto MapPlan(Product product)
    {
        if (string.IsNullOrWhiteSpace(product.Handle) || string.IsNullOrWhiteSpace(product.Name) ||
            product.PriceInCents is null || product.Interval is null || product.IntervalUnit is null)
        {
            throw SubscriptionBillingException.Dependency("Maxio returned an incomplete subscription plan.");
        }
        return new SubscriptionPlanDto(
            product.Handle,
            product.Name,
            product.Description,
            product.PriceInCents.Value,
            product.Interval.Value,
            product.IntervalUnit.Value);
    }

    private static SubscriptionDto MapSubscription(
        MaxioAdvancedBilling.Models.Subscription subscription,
        Product? validatedPlan = null)
    {
        var product = subscription.Product ?? validatedPlan;
        if (subscription.Id is null || string.IsNullOrWhiteSpace(subscription.Reference) || product is null ||
            string.IsNullOrWhiteSpace(product.Handle) || string.IsNullOrWhiteSpace(product.Name) ||
            subscription.ProductPriceInCents is null || subscription.State is null)
        {
            throw SubscriptionBillingException.Dependency("Maxio returned an incomplete subscription record.");
        }
        return new SubscriptionDto(
            subscription.Id.Value,
            subscription.Reference,
            product.Handle,
            product.Name,
            subscription.ProductPriceInCents.Value,
            subscription.State.Value,
            subscription.NextAssessmentAt ?? subscription.CurrentPeriodEndsAt,
            subscription.Currency);
    }

    private async Task<T> WithinBudgetAsync<T>(Func<CancellationToken, Task<T>> operation, CancellationToken requestToken)
    {
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(requestToken);
        budget.CancelAfter(CallBudget);
        try
        {
            return await operation(budget.Token);
        }
        catch (SubscriptionBillingException)
        {
            throw;
        }
        catch (OperationCanceledException ex) when (!requestToken.IsCancellationRequested)
        {
            throw SubscriptionBillingException.Timeout(ex);
        }
        catch (HttpRequestException ex)
        {
            throw SubscriptionBillingException.Dependency("Maxio could not be reached.", ex);
        }
        catch (JsonException ex)
        {
            throw SubscriptionBillingException.Dependency(
                "Maxio returned a response that could not be processed.", ex);
        }
    }

    private static SubscriptionBillingException FromRawError(RawError raw, string fallbackMessage, Exception inner)
    {
        var status = (int)raw.StatusCode;
        if (status is >= 400 and < 500)
        {
            return status == 404
                ? new SubscriptionBillingException(404, "Billing resource not found", fallbackMessage, inner)
                : SubscriptionBillingException.Rejected(fallbackMessage, inner);
        }
        return SubscriptionBillingException.Dependency(fallbackMessage, inner);
    }

    private static string CustomerReference(string userId) => "eshop-customer-" + Hash(userId);
    private static string SubscriptionReference(string userId, string planHandle) =>
        "eshop-sub-" + Hash(userId + "\n" + planHandle);

    private static string Hash(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant()[..32];

    private static bool IsValidPlanHandle(string? value) =>
        value is { Length: > 0 and <= 100 } &&
        char.IsLetterOrDigit(value[0]) &&
        value.All(character => char.IsLetterOrDigit(character) || character is '_' or '-');

    private sealed class UnknownWriteOutcomeException : Exception
    {
        public UnknownWriteOutcomeException(Exception innerException)
            : base("The Maxio write outcome is unknown.", innerException)
        {
        }
    }
}
