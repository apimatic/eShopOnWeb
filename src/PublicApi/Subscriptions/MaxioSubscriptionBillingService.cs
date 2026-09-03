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
using MaxioAdvancedBilling.Core;
using MaxioAdvancedBilling.Core.ErrorResponse;
using MaxioAdvancedBilling.Core.Exceptions;
using MaxioAdvancedBilling.Core.Hooks;
using MaxioAdvancedBilling.Errors;
using MaxioAdvancedBilling.Models;
using MaxioAdvancedBilling.Models.Enums;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.eShopWeb.Infrastructure.Identity;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

public sealed class MaxioSubscriptionBillingService : ISubscriptionBillingService
{
    private const int PageSize = 200;
    private const int MaxPlanPages = 25;
    private static readonly TimeSpan OperationBudget = TimeSpan.FromSeconds(20);
    private static readonly TimeSpan PendingLease = TimeSpan.FromMinutes(2);

    private readonly MaxioAdvancedBillingClient _client;
    private readonly CatalogContext _dbContext;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly MaxioSettings _settings;
    private readonly SubscriptionOperationCoordinator _coordinator;
    private readonly ILogger<MaxioSubscriptionBillingService> _logger;

    public MaxioSubscriptionBillingService(
        MaxioAdvancedBillingClient client,
        CatalogContext dbContext,
        UserManager<ApplicationUser> userManager,
        IOptions<MaxioSettings> settings,
        SubscriptionOperationCoordinator coordinator,
        ILogger<MaxioSubscriptionBillingService> logger)
    {
        _client = client;
        _dbContext = dbContext;
        _userManager = userManager;
        _settings = settings.Value;
        _coordinator = coordinator;
        _logger = logger;
    }

    public Task<IReadOnlyList<SubscriptionPlanDto>> ListPlansAsync(CancellationToken cancellationToken) =>
        WithinBudgetAsync(ListPlansCoreAsync, cancellationToken);

    public Task<IReadOnlyList<ShopperSubscriptionDto>> ListForUserAsync(
        string username,
        CancellationToken cancellationToken) =>
        WithinBudgetAsync(ct => ListForUserCoreAsync(username, ct), cancellationToken);

    public Task<ShopperSubscriptionDto> SubscribeAsync(
        string username,
        string productHandle,
        CancellationToken cancellationToken) =>
        WithinBudgetAsync(ct => SubscribeCoreAsync(username, productHandle, ct), cancellationToken);

    private async Task<IReadOnlyList<SubscriptionPlanDto>> ListPlansCoreAsync(CancellationToken ct)
    {
        var plans = new List<SubscriptionPlanDto>();
        var family = $"handle:{_settings.ProductFamilyHandle}";

        for (var page = 1; page <= MaxPlanPages; page++)
        {
            var response = await ListProductsPageAsync(family, page, ct);
            foreach (var productResponse in response)
            {
                var product = productResponse.Product;
                if (product.ArchivedAt is not null)
                {
                    continue;
                }

                plans.Add(MapPlan(product));
            }

            if (response.Count < PageSize)
            {
                return plans.OrderBy(plan => plan.PriceInCents).ThenBy(plan => plan.Name).ToArray();
            }
        }

        _logger.LogError("Maxio plan pagination exceeded the configured {PageCap}-page safety cap.", MaxPlanPages);
        throw new SubscriptionBillingException(
            SubscriptionBillingError.ProviderUnavailable,
            "The subscription plan catalog is temporarily unavailable.");
    }

    private async Task<ShopperSubscriptionDto> SubscribeCoreAsync(
        string username,
        string productHandle,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(productHandle))
        {
            throw new SubscriptionBillingException(
                SubscriptionBillingError.InvalidRequest,
                "ProductHandle is required.");
        }

        var user = await _userManager.FindByNameAsync(username);
        if (user is null)
        {
            throw new SubscriptionBillingException(
                SubscriptionBillingError.NotFound,
                "The authenticated user no longer exists.");
        }

        var plans = await ListPlansCoreAsync(ct);
        var selectedPlan = plans.SingleOrDefault(
            plan => string.Equals(plan.Handle, productHandle, StringComparison.Ordinal));
        if (selectedPlan is null)
        {
            throw new SubscriptionBillingException(
                SubscriptionBillingError.InvalidRequest,
                "The selected subscription plan is not available.");
        }

        var customerReference = CreateReference("customer", user.Id);
        var subscriptionReference = CreateReference("subscription", $"{user.Id}\n{selectedPlan.Handle}");

        using (await _coordinator.AcquireAsync(subscriptionReference, ct))
        {
            var now = DateTimeOffset.UtcNow;
            var record = await _dbContext.SubscriptionBillingRecords.SingleOrDefaultAsync(
                item => item.UserId == user.Id && item.ProductHandle == selectedPlan.Handle,
                ct);
            var isNew = record is null;

            if (record is null)
            {
                record = new SubscriptionBillingRecord(
                    user.Id,
                    selectedPlan.Handle,
                    customerReference,
                    subscriptionReference,
                    now);
                _dbContext.SubscriptionBillingRecords.Add(record);

                try
                {
                    await _dbContext.SaveChangesAsync(ct);
                }
                catch (DbUpdateException)
                {
                    _dbContext.Entry(record).State = EntityState.Detached;
                    record = await _dbContext.SubscriptionBillingRecords.SingleAsync(
                        item => item.UserId == user.Id && item.ProductHandle == selectedPlan.Handle,
                        ct);
                    isNew = false;
                }
            }

            var existingSubscription = await FindSubscriptionAsync(record.SubscriptionReference, ct);
            if (existingSubscription is not null)
            {
                await MarkCompletedAsync(record, existingSubscription, now, ct);
                return MapSubscription(existingSubscription);
            }

            if (!isNew && record.Status == SubscriptionBillingRecordStatus.Pending &&
                now - record.UpdatedAt < PendingLease)
            {
                throw new SubscriptionBillingException(
                    SubscriptionBillingError.Conflict,
                    "Subscription enrollment is already in progress. Retry shortly.");
            }

            record.MarkAttempt(now);
            await _dbContext.SaveChangesAsync(ct);

            var customer = await EnsureCustomerAsync(user, record.CustomerReference, ct);
            if (customer.Id is not int customerId)
            {
                throw InvalidProviderResponse("Maxio returned a customer without an id.");
            }

            Subscription subscription;
            try
            {
                subscription = await CreateSubscriptionAsync(
                    customerId,
                    selectedPlan.Handle,
                    record.SubscriptionReference,
                    ct);
            }
            catch (SubscriptionBillingException ex) when (
                ex.Error is SubscriptionBillingError.UnknownWriteOutcome or
                    SubscriptionBillingError.InvalidProviderResponse)
            {
                var reconciled = await FindSubscriptionAsync(record.SubscriptionReference, ct);
                if (reconciled is null)
                {
                    throw;
                }

                subscription = reconciled;
            }
            catch (SubscriptionBillingException ex) when (ex.Error == SubscriptionBillingError.InvalidRequest)
            {
                record.MarkFailed(DateTimeOffset.UtcNow);
                await _dbContext.SaveChangesAsync(ct);
                throw;
            }

            await MarkCompletedAsync(record, subscription, DateTimeOffset.UtcNow, ct);
            return MapSubscription(subscription);
        }
    }

    private async Task<IReadOnlyList<ShopperSubscriptionDto>> ListForUserCoreAsync(
        string username,
        CancellationToken ct)
    {
        var user = await _userManager.FindByNameAsync(username);
        if (user is null)
        {
            throw new SubscriptionBillingException(
                SubscriptionBillingError.NotFound,
                "The authenticated user no longer exists.");
        }

        var customer = await ReadCustomerByReferenceAsync(CreateReference("customer", user.Id), ct);
        if (customer is null)
        {
            return [];
        }

        if (customer.Id is not int customerId)
        {
            throw InvalidProviderResponse("Maxio returned a customer without an id.");
        }

        var responses = await ListCustomerSubscriptionsAsync(customerId, ct);
        return responses
            .Select(response => response.Subscription ??
                throw InvalidProviderResponse("Maxio returned an empty subscription envelope."))
            .Select(MapSubscription)
            .OrderByDescending(subscription => subscription.NextBillingAt)
            .ToArray();
    }

    private async Task<Customer> EnsureCustomerAsync(
        ApplicationUser user,
        string customerReference,
        CancellationToken ct)
    {
        var existing = await ReadCustomerByReferenceAsync(customerReference, ct);
        if (existing is not null)
        {
            return existing;
        }

        try
        {
            return await CreateCustomerAsync(user, customerReference, ct);
        }
        catch (SubscriptionBillingException ex) when (
            ex.Error is SubscriptionBillingError.Conflict or
                SubscriptionBillingError.UnknownWriteOutcome or
                SubscriptionBillingError.InvalidProviderResponse)
        {
            var reconciled = await ReadCustomerByReferenceAsync(customerReference, ct);
            if (reconciled is not null)
            {
                return reconciled;
            }

            throw;
        }
    }

    private async Task<IReadOnlyList<ProductResponse>> ListProductsPageAsync(
        string family,
        int page,
        CancellationToken ct)
    {
        HttpStatusCode? observedStatus = null;
        try
        {
            return await _client.ProductFamilies.ListProductsForProductFamily(
                productFamilyId: family,
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
                requestOptions: ObserveStatus(status => observedStatus = status),
                ct: ct);
        }
        catch (SdkException<ListProductsForProductFamilyError> ex)
        {
            if (ex.Error.TryGetString(out _))
            {
                throw ProviderFailure(HttpStatusCode.NotFound, ex);
            }
            if (ex.Error.TryGetRawError(out var raw))
            {
                throw ProviderFailure(raw.StatusCode, ex);
            }
            throw ProviderFailure(null, ex);
        }
        catch (JsonException ex)
        {
            throw InvalidJsonResponse(observedStatus, ex);
        }
        catch (HttpRequestException ex)
        {
            throw ProviderFailure(null, ex);
        }
        catch (TaskCanceledException ex) when (!ct.IsCancellationRequested)
        {
            throw ProviderFailure(null, ex);
        }
    }

    private async Task<Customer?> ReadCustomerByReferenceAsync(string reference, CancellationToken ct)
    {
        HttpStatusCode? observedStatus = null;
        try
        {
            var response = await _client.Customers.ReadCustomerByReference(
                reference,
                requestOptions: ObserveStatus(status => observedStatus = status),
                ct: ct);
            return response.Customer;
        }
        catch (SdkException<RawError> ex) when (ex.Error.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
        catch (SdkException<RawError> ex)
        {
            throw ProviderFailure(ex.Error.StatusCode, ex);
        }
        catch (JsonException ex)
        {
            throw InvalidJsonResponse(observedStatus, ex);
        }
        catch (HttpRequestException ex)
        {
            throw ProviderFailure(null, ex);
        }
        catch (TaskCanceledException ex) when (!ct.IsCancellationRequested)
        {
            throw ProviderFailure(null, ex);
        }
    }

    private async Task<Customer> CreateCustomerAsync(
        ApplicationUser user,
        string reference,
        CancellationToken ct)
    {
        var (firstName, lastName) = CustomerName(user.UserName ?? user.Email ?? "eshop-user");
        var body = new MaxioAdvancedBilling.Models.CreateCustomerRequest
        {
            Customer = new CreateCustomer
            {
                FirstName = firstName,
                LastName = lastName,
                Email = user.Email ?? user.UserName ?? throw new InvalidOperationException("Identity user has no email."),
                Reference = reference
            }
        };

        HttpStatusCode? observedStatus = null;
        try
        {
            var response = await _client.Customers.CreateCustomer(
                body,
                requestOptions: ObserveStatus(status => observedStatus = status),
                ct: ct);
            return response.Customer;
        }
        catch (SdkException<CreateCustomerError> ex)
        {
            if (ex.Error.TryGetCustomerErrorResponse1(out _))
            {
                throw new SubscriptionBillingException(
                    SubscriptionBillingError.Conflict,
                    "The billing customer already exists.",
                    HttpStatusCode.UnprocessableEntity,
                    ex);
            }
            if (ex.Error.TryGetRawError(out var raw))
            {
                throw ProviderFailure(raw.StatusCode, ex, writeOutcomeUnknown: true);
            }
            throw ProviderFailure(null, ex, writeOutcomeUnknown: true);
        }
        catch (JsonException ex)
        {
            throw InvalidJsonResponse(observedStatus, ex, writeOutcomeUnknown: true);
        }
        catch (HttpRequestException ex)
        {
            throw ProviderFailure(null, ex, writeOutcomeUnknown: true);
        }
        catch (TaskCanceledException ex) when (!ct.IsCancellationRequested)
        {
            throw ProviderFailure(null, ex, writeOutcomeUnknown: true);
        }
    }

    private async Task<Subscription?> FindSubscriptionAsync(string reference, CancellationToken ct)
    {
        HttpStatusCode? observedStatus = null;
        try
        {
            var response = await _client.Subscriptions.FindSubscription(
                reference,
                requestOptions: ObserveStatus(status => observedStatus = status),
                ct: ct);
            return response.Subscription ??
                throw InvalidProviderResponse("Maxio returned an empty subscription envelope.");
        }
        catch (SdkException<FindSubscriptionError> ex)
        {
            if (ex.Error.TryGetNoContent(out _))
            {
                return null;
            }
            if (ex.Error.TryGetRawError(out var raw))
            {
                throw ProviderFailure(raw.StatusCode, ex);
            }
            throw ProviderFailure(null, ex);
        }
        catch (JsonException ex)
        {
            throw InvalidJsonResponse(observedStatus, ex);
        }
        catch (HttpRequestException ex)
        {
            throw ProviderFailure(null, ex);
        }
        catch (TaskCanceledException ex) when (!ct.IsCancellationRequested)
        {
            throw ProviderFailure(null, ex);
        }
    }

    private async Task<Subscription> CreateSubscriptionAsync(
        int customerId,
        string productHandle,
        string reference,
        CancellationToken ct)
    {
        var body = new MaxioAdvancedBilling.Models.CreateSubscriptionRequest
        {
            Subscription = new CreateSubscription
            {
                CustomerId = customerId,
                ProductHandle = productHandle,
                PaymentCollectionMethod = CollectionMethod.Remittance,
                Reference = reference
            }
        };

        HttpStatusCode? observedStatus = null;
        try
        {
            var response = await _client.Subscriptions.CreateSubscription(
                body,
                requestOptions: ObserveStatus(status => observedStatus = status),
                ct: ct);
            return response.Subscription ??
                throw InvalidProviderResponse("Maxio returned an empty subscription envelope.");
        }
        catch (SdkException<CreateSubscriptionError> ex)
        {
            if (ex.Error.TryGetErrorListResponse1(out _))
            {
                throw new SubscriptionBillingException(
                    SubscriptionBillingError.InvalidRequest,
                    "Maxio rejected the subscription enrollment.",
                    HttpStatusCode.UnprocessableEntity,
                    ex);
            }
            if (ex.Error.TryGetRawError(out var raw))
            {
                throw ProviderFailure(raw.StatusCode, ex, writeOutcomeUnknown: true);
            }
            throw ProviderFailure(null, ex, writeOutcomeUnknown: true);
        }
        catch (JsonException ex)
        {
            throw InvalidJsonResponse(observedStatus, ex, writeOutcomeUnknown: true);
        }
        catch (HttpRequestException ex)
        {
            throw ProviderFailure(null, ex, writeOutcomeUnknown: true);
        }
        catch (TaskCanceledException ex) when (!ct.IsCancellationRequested)
        {
            throw ProviderFailure(null, ex, writeOutcomeUnknown: true);
        }
    }

    private async Task<IReadOnlyList<SubscriptionResponse>> ListCustomerSubscriptionsAsync(
        int customerId,
        CancellationToken ct)
    {
        HttpStatusCode? observedStatus = null;
        try
        {
            return await _client.Customers.ListCustomerSubscriptions(
                customerId,
                requestOptions: ObserveStatus(status => observedStatus = status),
                ct: ct);
        }
        catch (SdkException<RawError> ex)
        {
            throw ProviderFailure(ex.Error.StatusCode, ex);
        }
        catch (JsonException ex)
        {
            throw InvalidJsonResponse(observedStatus, ex);
        }
        catch (HttpRequestException ex)
        {
            throw ProviderFailure(null, ex);
        }
        catch (TaskCanceledException ex) when (!ct.IsCancellationRequested)
        {
            throw ProviderFailure(null, ex);
        }
    }

    private async Task MarkCompletedAsync(
        SubscriptionBillingRecord record,
        Subscription subscription,
        DateTimeOffset now,
        CancellationToken ct)
    {
        if (subscription.Id is not int subscriptionId || subscription.Customer?.Id is not int customerId)
        {
            throw InvalidProviderResponse("Maxio returned an incomplete subscription.");
        }

        record.MarkCompleted(customerId, subscriptionId, now);
        await _dbContext.SaveChangesAsync(ct);
    }

    private static SubscriptionPlanDto MapPlan(Product product)
    {
        if (string.IsNullOrWhiteSpace(product.Handle) || string.IsNullOrWhiteSpace(product.Name) ||
            product.PriceInCents is not long price || product.Interval is not int interval ||
            product.IntervalUnit is null)
        {
            throw InvalidProviderResponse("Maxio returned an incomplete product.");
        }

        return new SubscriptionPlanDto(
            product.Handle,
            product.Name,
            product.Description,
            price,
            interval,
            product.IntervalUnit.Value,
            product.ProductPricePointId ?? product.DefaultProductPricePointId,
            product.ProductPricePointHandle,
            product.RequireCreditCard ?? false);
    }

    private static ShopperSubscriptionDto MapSubscription(Subscription subscription)
    {
        var product = subscription.Product;
        if (subscription.Id is not int id || string.IsNullOrWhiteSpace(subscription.Reference) ||
            subscription.State is null || product is null || string.IsNullOrWhiteSpace(product.Handle) ||
            string.IsNullOrWhiteSpace(product.Name) || subscription.ProductPriceInCents is not long price ||
            product.Interval is not int interval || product.IntervalUnit is null)
        {
            throw InvalidProviderResponse("Maxio returned an incomplete subscription.");
        }

        return new ShopperSubscriptionDto(
            id,
            subscription.Reference,
            product.Handle,
            product.Name,
            price,
            interval,
            product.IntervalUnit.Value,
            subscription.State.Value,
            subscription.NextAssessmentAt,
            subscription.CurrentPeriodEndsAt,
            subscription.Currency);
    }

    private static RequestOptions ObserveStatus(Action<HttpStatusCode> observer) => new()
    {
        Hooks = [SdkHook.OnResponse((response, _) => observer(response.StatusCode))]
    };

    private static SubscriptionBillingException InvalidJsonResponse(
        HttpStatusCode? observedStatus,
        JsonException exception,
        bool writeOutcomeUnknown = false)
    {
        if (observedStatus is >= HttpStatusCode.BadRequest)
        {
            return ProviderFailure(observedStatus, exception, writeOutcomeUnknown);
        }

        return new SubscriptionBillingException(
            writeOutcomeUnknown
                ? SubscriptionBillingError.UnknownWriteOutcome
                : SubscriptionBillingError.InvalidProviderResponse,
            "Maxio returned a response that could not be processed.",
            observedStatus,
            exception);
    }

    private static SubscriptionBillingException InvalidProviderResponse(string diagnostic) =>
        new(SubscriptionBillingError.InvalidProviderResponse, diagnostic);

    private static SubscriptionBillingException ProviderFailure(
        HttpStatusCode? status,
        Exception exception,
        bool writeOutcomeUnknown = false)
    {
        var error = writeOutcomeUnknown && (status is null or >= HttpStatusCode.InternalServerError)
            ? SubscriptionBillingError.UnknownWriteOutcome
            : status is >= HttpStatusCode.BadRequest and < HttpStatusCode.InternalServerError &&
                status is not HttpStatusCode.Unauthorized and not HttpStatusCode.Forbidden &&
                (int?)status != 429
                ? SubscriptionBillingError.InvalidRequest
                : SubscriptionBillingError.ProviderUnavailable;

        var message = error == SubscriptionBillingError.InvalidRequest
            ? "Maxio rejected the billing request."
            : "The billing provider is temporarily unavailable.";

        return new SubscriptionBillingException(error, message, status, exception);
    }

    private async Task<T> WithinBudgetAsync<T>(
        Func<CancellationToken, Task<T>> operation,
        CancellationToken cancellationToken)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(OperationBudget);

        try
        {
            return await operation(deadline.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new SubscriptionBillingException(
                SubscriptionBillingError.ProviderUnavailable,
                "The billing provider did not respond in time.");
        }
    }

    private static string CreateReference(string kind, string value)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return $"eshop-{kind}-{Convert.ToHexString(hash).ToLowerInvariant()[..32]}";
    }

    private static (string FirstName, string LastName) CustomerName(string username)
    {
        var localPart = username.Split('@', 2)[0];
        var parts = localPart.Split(['.', '_', '-'], StringSplitOptions.RemoveEmptyEntries);
        var firstName = parts.Length > 0 ? parts[0] : "eShop";
        var lastName = parts.Length > 1 ? string.Join(' ', parts.Skip(1)) : "Customer";
        return (firstName[..Math.Min(firstName.Length, 50)], lastName[..Math.Min(lastName.Length, 50)]);
    }
}
