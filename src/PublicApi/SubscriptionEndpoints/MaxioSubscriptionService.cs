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
using MaxioAdvancedBilling.Core.ErrorResponse;
using MaxioAdvancedBilling.Core.Exceptions;
using MaxioAdvancedBilling.Errors;
using MaxioAdvancedBilling.Models;
using MaxioAdvancedBilling.Models.Enums;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.Infrastructure.Identity;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public sealed class MaxioSubscriptionService
{
    private static readonly TimeSpan ProviderCallBudget = TimeSpan.FromSeconds(25);
    private const int ProductPageSize = 100;
    private readonly MaxioClientFactory _clients;
    private readonly MaxioOptions _options;
    private readonly AppIdentityDbContext _identityDb;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly KeyedAsyncLock _operationLock;
    private readonly MaxioWriteRetryGuard _writeGuard;
    private readonly ILogger<MaxioSubscriptionService> _logger;

    public MaxioSubscriptionService(
        MaxioClientFactory clients,
        Microsoft.Extensions.Options.IOptions<MaxioOptions> options,
        AppIdentityDbContext identityDb,
        UserManager<ApplicationUser> userManager,
        KeyedAsyncLock operationLock,
        MaxioWriteRetryGuard writeGuard,
        ILogger<MaxioSubscriptionService> logger)
    {
        _clients = clients;
        _options = options.Value;
        _identityDb = identityDb;
        _userManager = userManager;
        _operationLock = operationLock;
        _writeGuard = writeGuard;
        _logger = logger;
    }

    public async Task<IReadOnlyList<SubscriptionPlanDto>> ListPlansAsync(CancellationToken cancellationToken)
    {
        var family = await GetConfiguredProductFamilyAsync(cancellationToken);
        var productFamilyId = family.Id?.ToString() ?? throw new SubscriptionApiException(
            StatusCodes.Status502BadGateway,
            "Maxio returned a subscription catalog without an ID.");
        var plans = new List<SubscriptionPlanDto>();

        for (var page = 1; ; page++)
        {
            IReadOnlyList<ProductResponse> response;
            try
            {
                response = await Bounded(ct => _clients.Read.ProductFamilies.ListProductsForProductFamily(
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
                    perPage: ProductPageSize,
                    ct: ct), cancellationToken);
            }
            catch (SdkException<ListProductsForProductFamilyError> ex)
            {
                throw TranslateListProductsError(ex);
            }
            catch (Exception ex) when (IsProviderUnavailable(ex))
            {
                throw ProviderUnavailable(ex);
            }

            foreach (var item in response)
            {
                var product = item.Product;
                if (product is not null && product.ArchivedAt is null &&
                    !string.IsNullOrWhiteSpace(product.Handle) && !string.IsNullOrWhiteSpace(product.Name))
                {
                    plans.Add(new SubscriptionPlanDto(
                        product.Handle,
                        product.Name,
                        ToNullableInt(product.PriceInCents, "product price"),
                        ToNullableInt(product.Interval, "product interval"),
                        product.IntervalUnit?.Value));
                }
            }

            if (response.Count < ProductPageSize)
            {
                return plans;
            }
        }
    }

    public async Task<SubscriptionDto> SubscribeAsync(string username, string planHandle, CancellationToken cancellationToken)
    {
        var user = await GetSubscriberAsync(username);
        var normalizedPlanHandle = planHandle.Trim();
        if (string.IsNullOrWhiteSpace(normalizedPlanHandle))
        {
            throw new SubscriptionApiException(StatusCodes.Status400BadRequest, "A plan handle is required.");
        }

        using var operation = await _operationLock.LockAsync($"{user.Id}:{normalizedPlanHandle}", cancellationToken);
        var existingEnrollment = await _identityDb.MaxioSubscriptionEnrollments
            .SingleOrDefaultAsync(x => x.UserId == user.Id && x.ProductHandle == normalizedPlanHandle, cancellationToken);

        if (existingEnrollment is not null)
        {
            var existing = await FindSubscriptionAsync(existingEnrollment.SubscriptionReference, cancellationToken);
            if (existing is not null)
            {
                return await PersistAndMapAsync(existingEnrollment, existing, cancellationToken);
            }

            if (existingEnrollment.Status == "Pending")
            {
                throw new SubscriptionApiException(StatusCodes.Status409Conflict,
                    "Your subscription enrollment is still being reconciled. Please retry shortly.");
            }
        }

        var plan = (await ListPlansAsync(cancellationToken))
            .SingleOrDefault(x => string.Equals(x.Handle, normalizedPlanHandle, StringComparison.Ordinal));
        if (plan is null)
        {
            throw new SubscriptionApiException(StatusCodes.Status400BadRequest,
                "The requested plan is not available in the configured subscription catalog.");
        }

        var customer = await GetOrCreateCustomerAsync(user, cancellationToken);
        var reference = SubscriptionReference(user.Id, normalizedPlanHandle);
        var enrollment = existingEnrollment ?? new MaxioSubscriptionEnrollment
        {
            UserId = user.Id,
            ProductHandle = normalizedPlanHandle,
            MaxioCustomerId = RequireWholeInt(customer.Id, "customer ID"),
            SubscriptionReference = reference,
            Status = "Pending",
            CreatedAtUtc = DateTimeOffset.UtcNow,
            UpdatedAtUtc = DateTimeOffset.UtcNow
        };

        if (existingEnrollment is null)
        {
            _identityDb.MaxioSubscriptionEnrollments.Add(enrollment);
            try
            {
                await _identityDb.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateException)
            {
                _identityDb.ChangeTracker.Clear();
                var concurrent = await _identityDb.MaxioSubscriptionEnrollments.SingleAsync(
                    x => x.UserId == user.Id && x.ProductHandle == normalizedPlanHandle, cancellationToken);
                var concurrentSubscription = await FindSubscriptionAsync(concurrent.SubscriptionReference, cancellationToken);
                if (concurrentSubscription is not null)
                {
                    return await PersistAndMapAsync(concurrent, concurrentSubscription, cancellationToken);
                }

                throw new SubscriptionApiException(StatusCodes.Status409Conflict,
                    "A subscription enrollment for this plan is already in progress. Please retry shortly.");
            }
        }

        var subscription = await FindSubscriptionAsync(reference, cancellationToken);
        if (subscription is null)
        {
            subscription = await CreateOrReconcileSubscriptionAsync(RequireWholeInt(customer.Id, "customer ID"), normalizedPlanHandle, reference, cancellationToken);
        }

        return await PersistAndMapAsync(enrollment, subscription, cancellationToken);
    }

    public async Task<IReadOnlyList<SubscriptionDto>> ListMySubscriptionsAsync(string username, CancellationToken cancellationToken)
    {
        var user = await GetSubscriberAsync(username);
        var customer = await GetCustomerAsync(user.Id, cancellationToken);
        if (customer is null)
        {
            return Array.Empty<SubscriptionDto>();
        }

        IReadOnlyList<SubscriptionResponse> response;
        try
        {
            response = await Bounded(ct => _clients.Read.Customers.ListCustomerSubscriptions(customer.MaxioCustomerId, ct), cancellationToken);
        }
        catch (SdkException<RawError> ex)
        {
            throw TranslateRawError(ex.Error);
        }
        catch (Exception ex) when (IsProviderUnavailable(ex))
        {
            throw ProviderUnavailable(ex);
        }

        return response
            .Select(item => item.Subscription)
            .OfType<Subscription>()
            .Select(MapSubscription)
            .ToList();
    }

    private async Task<ProductFamily> GetConfiguredProductFamilyAsync(CancellationToken cancellationToken)
    {
        IReadOnlyList<ProductFamilyResponse> response;
        try
        {
            response = await Bounded(ct => _clients.Read.ProductFamilies.ListProductFamilies(
                dateField: null,
                startDate: null,
                endDate: null,
                startDatetime: null,
                endDatetime: null,
                ct: ct), cancellationToken);
        }
        catch (SdkException<RawError> ex)
        {
            throw TranslateRawError(ex.Error);
        }
        catch (Exception ex) when (IsProviderUnavailable(ex))
        {
            throw ProviderUnavailable(ex);
        }

        var families = response
            .Select(item => item.ProductFamily)
            .Where(family => family is not null && string.Equals(family.Handle, _options.ProductFamilyHandle, StringComparison.Ordinal))
            .ToList();

        if (families.Count != 1)
        {
            throw new SubscriptionApiException(StatusCodes.Status503ServiceUnavailable,
                "The configured subscription catalog is unavailable.");
        }

        return families[0]!;
    }

    private async Task<ApplicationUser> GetSubscriberAsync(string username)
    {
        if (string.IsNullOrWhiteSpace(username))
        {
            throw new SubscriptionApiException(StatusCodes.Status401Unauthorized, "An authenticated user is required.");
        }

        var user = await _userManager.FindByNameAsync(username);
        if (user is null)
        {
            throw new SubscriptionApiException(StatusCodes.Status401Unauthorized, "The authenticated user no longer exists.");
        }

        if (string.IsNullOrWhiteSpace(user.Email) || string.IsNullOrWhiteSpace(user.FirstName) || string.IsNullOrWhiteSpace(user.LastName))
        {
            throw new SubscriptionApiException(StatusCodes.Status422UnprocessableEntity,
                "Your account requires a first name, last name, and email before you can subscribe.");
        }

        return user;
    }

    private async Task<MaxioCustomerLink?> GetCustomerAsync(string userId, CancellationToken cancellationToken)
    {
        return await _identityDb.MaxioCustomerLinks.SingleOrDefaultAsync(link => link.UserId == userId, cancellationToken);
    }

    private async Task<Customer> GetOrCreateCustomerAsync(ApplicationUser user, CancellationToken cancellationToken)
    {
        var local = await GetCustomerAsync(user.Id, cancellationToken);
        if (local is not null)
        {
            return new Customer { Id = local.MaxioCustomerId, Reference = local.CustomerReference, Email = user.Email! };
        }

        var reference = CustomerReference(user.Id);
        var customer = await ReadCustomerAsync(reference, cancellationToken);
        if (customer is null)
        {
            customer = await CreateOrReconcileCustomerAsync(user, reference, cancellationToken);
        }

        var link = new MaxioCustomerLink
        {
            UserId = user.Id,
            MaxioCustomerId = RequireWholeInt(customer.Id, "customer ID"),
            CustomerReference = reference,
            CreatedAtUtc = DateTimeOffset.UtcNow,
            UpdatedAtUtc = DateTimeOffset.UtcNow
        };

        _identityDb.MaxioCustomerLinks.Add(link);
        try
        {
            await _identityDb.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            _identityDb.ChangeTracker.Clear();
            var concurrent = await GetCustomerAsync(user.Id, cancellationToken);
            if (concurrent is null)
            {
                throw;
            }

            return new Customer { Id = concurrent.MaxioCustomerId, Reference = concurrent.CustomerReference, Email = user.Email! };
        }

        return customer;
    }

    private async Task<Customer?> ReadCustomerAsync(string reference, CancellationToken cancellationToken)
    {
        try
        {
            return (await Bounded(ct => _clients.Read.Customers.ReadCustomerByReference(reference, ct), cancellationToken)).Customer;
        }
        catch (SdkException<RawError> ex) when (ex.Error.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
        catch (SdkException<RawError> ex)
        {
            throw TranslateRawError(ex.Error);
        }
        catch (Exception ex) when (IsProviderUnavailable(ex))
        {
            throw ProviderUnavailable(ex);
        }
    }

    private async Task<Customer> CreateOrReconcileCustomerAsync(ApplicationUser user, string reference, CancellationToken cancellationToken)
    {
        try
        {
            using var write = _writeGuard.BeginWrite();
            var response = await Bounded(ct => _clients.Write.Customers.CreateCustomer(new CreateCustomerRequest
            {
                Customer = new CreateCustomer
                {
                    FirstName = user.FirstName!,
                    LastName = user.LastName!,
                    Email = user.Email!,
                    Reference = reference
                }
            }, ct), cancellationToken);
            return response.Customer;
        }
        catch (SdkException<CreateCustomerError> ex)
        {
            var existing = await ReadCustomerAsync(reference, cancellationToken);
            if (existing is not null)
            {
                return existing;
            }

            if (ex.Error.TryGetCustomerErrorResponse1(out _))
            {
                throw new SubscriptionApiException(StatusCodes.Status422UnprocessableEntity, "Maxio rejected the customer profile.", ex);
            }
            if (ex.Error.TryGetRawError(out var raw))
            {
                throw TranslateRawError(raw, ex);
            }

            throw new SubscriptionApiException(StatusCodes.Status502BadGateway, "Maxio could not create the customer.", ex);
        }
        catch (Exception ex) when (ex is MaxioWriteRetryBlockedException or HttpRequestException or TaskCanceledException or JsonException)
        {
            var existing = await ReadCustomerAsync(reference, cancellationToken);
            if (existing is not null)
            {
                return existing;
            }

            throw new SubscriptionApiException(StatusCodes.Status409Conflict,
                "The customer creation outcome is being reconciled. Please retry shortly.", ex);
        }
    }

    private async Task<Subscription?> FindSubscriptionAsync(string reference, CancellationToken cancellationToken)
    {
        try
        {
            return (await Bounded(ct => _clients.Read.Subscriptions.FindSubscription(reference, ct), cancellationToken)).Subscription;
        }
        catch (SdkException<FindSubscriptionError> ex)
        {
            if (ex.Error.TryGetNoContent(out _))
            {
                return null;
            }
            if (ex.Error.TryGetRawError(out var raw))
            {
                throw TranslateRawError(raw, ex);
            }

            throw new SubscriptionApiException(StatusCodes.Status502BadGateway, "Maxio could not read the subscription.", ex);
        }
        catch (Exception ex) when (IsProviderUnavailable(ex))
        {
            throw ProviderUnavailable(ex);
        }
    }

    private async Task<Subscription> CreateOrReconcileSubscriptionAsync(int customerId, string planHandle, string reference, CancellationToken cancellationToken)
    {
        try
        {
            using var write = _writeGuard.BeginWrite();
            var response = await Bounded(ct => _clients.Write.Subscriptions.CreateSubscription(new CreateSubscriptionRequest
            {
                Subscription = new CreateSubscription
                {
                    ProductHandle = planHandle,
                    CustomerId = customerId,
                    Reference = reference,
                    PaymentCollectionMethod = CollectionMethod.Invoice
                }
            }, ct), cancellationToken);
            return response.Subscription ?? throw new SubscriptionApiException(StatusCodes.Status502BadGateway,
                "Maxio returned an incomplete subscription response.");
        }
        catch (SdkException<CreateSubscriptionError> ex)
        {
            var existing = await FindSubscriptionAsync(reference, cancellationToken);
            if (existing is not null)
            {
                return existing;
            }

            if (ex.Error.TryGetErrorListResponse1(out var details))
            {
                _logger.LogWarning("Maxio rejected subscription reference {SubscriptionReference} for plan {PlanHandle}: {Errors}",
                    reference, planHandle, string.Join(" | ", details.Errors));
                throw new SubscriptionApiException(StatusCodes.Status422UnprocessableEntity, "Maxio rejected the subscription request.", ex);
            }
            if (ex.Error.TryGetRawError(out var raw))
            {
                throw TranslateRawError(raw, ex);
            }

            throw new SubscriptionApiException(StatusCodes.Status502BadGateway, "Maxio could not create the subscription.", ex);
        }
        catch (Exception ex) when (ex is MaxioWriteRetryBlockedException or HttpRequestException or TaskCanceledException or JsonException)
        {
            var existing = await FindSubscriptionAsync(reference, cancellationToken);
            if (existing is not null)
            {
                return existing;
            }

            throw new SubscriptionApiException(StatusCodes.Status409Conflict,
                "The subscription outcome is being reconciled. Please retry shortly.", ex);
        }
    }

    private async Task<SubscriptionDto> PersistAndMapAsync(MaxioSubscriptionEnrollment enrollment, Subscription subscription, CancellationToken cancellationToken)
    {
        enrollment.MaxioSubscriptionId = RequireWholeInt(subscription.Id, "subscription ID");
        enrollment.Status = subscription.State?.Value ?? "unknown";
        enrollment.UpdatedAtUtc = DateTimeOffset.UtcNow;
        await _identityDb.SaveChangesAsync(cancellationToken);
        return MapSubscription(subscription);
    }

    private static SubscriptionDto MapSubscription(Subscription subscription) => new(
        ToNullableInt(subscription.Id, "subscription ID"),
        subscription.Product?.Handle,
        subscription.Product?.Name,
        ToNullableInt(subscription.ProductPriceInCents ?? subscription.CurrentBillingAmountInCents, "subscription price"),
        subscription.Currency,
        subscription.State?.Value,
        subscription.NextAssessmentAt);

    private static int RequireWholeInt(double? value, string fieldName) =>
        ToNullableInt(value, fieldName) ?? throw new SubscriptionApiException(
            StatusCodes.Status502BadGateway,
            $"Maxio returned a subscription without a {fieldName}.");

    private static int? ToNullableInt(long? value, string fieldName)
    {
        if (!value.HasValue)
        {
            return null;
        }

        try
        {
            return checked((int)value.Value);
        }
        catch (OverflowException ex)
        {
            throw new SubscriptionApiException(StatusCodes.Status502BadGateway,
                $"Maxio returned an unsupported {fieldName}.", ex);
        }
    }

    private static int? ToNullableInt(double? value, string fieldName)
    {
        if (!value.HasValue)
        {
            return null;
        }

        if (double.IsNaN(value.Value) || double.IsInfinity(value.Value) || value.Value != Math.Truncate(value.Value))
        {
            throw new SubscriptionApiException(StatusCodes.Status502BadGateway,
                $"Maxio returned an unsupported {fieldName}.");
        }

        try
        {
            return checked((int)value.Value);
        }
        catch (OverflowException ex)
        {
            throw new SubscriptionApiException(StatusCodes.Status502BadGateway,
                $"Maxio returned an unsupported {fieldName}.", ex);
        }
    }

    private async Task<T> Bounded<T>(Func<CancellationToken, Task<T>> providerCall, CancellationToken cancellationToken)
    {
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        budget.CancelAfter(ProviderCallBudget);
        return await providerCall(budget.Token);
    }

    private static SubscriptionApiException TranslateListProductsError(SdkException<ListProductsForProductFamilyError> ex)
    {
        if (ex.Error.TryGetString(out _))
        {
            return new SubscriptionApiException(StatusCodes.Status503ServiceUnavailable, "The configured subscription catalog is unavailable.", ex);
        }
        if (ex.Error.TryGetRawError(out var raw))
        {
            return TranslateRawError(raw, ex);
        }

        return new SubscriptionApiException(StatusCodes.Status502BadGateway, "Maxio could not load subscription plans.", ex);
    }

    private static SubscriptionApiException TranslateRawError(RawError error, Exception? innerException = null)
    {
        var status = (int)error.StatusCode;
        if (status is >= 400 and < 500)
        {
            return new SubscriptionApiException(status, "Maxio rejected the request.", innerException);
        }

        return new SubscriptionApiException(StatusCodes.Status502BadGateway, "Maxio is currently unavailable.", innerException);
    }

    private static SubscriptionApiException ProviderUnavailable(Exception exception) =>
        new(StatusCodes.Status503ServiceUnavailable, "Maxio is currently unavailable. Please retry shortly.", exception);

    private static bool IsProviderUnavailable(Exception exception) =>
        exception is HttpRequestException or TaskCanceledException or JsonException;

    private static string CustomerReference(string userId) => $"eshop-user-{Hash(userId)}";
    private static string SubscriptionReference(string userId, string planHandle) => $"eshop-sub-{Hash($"{userId}:{planHandle}")}";
    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
}
