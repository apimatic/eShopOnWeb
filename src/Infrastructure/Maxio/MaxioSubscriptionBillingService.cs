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
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.eShopWeb.Infrastructure.Identity;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

public sealed class MaxioSubscriptionBillingService : ISubscriptionBillingService
{
    private const string PreferredDefaultProductHandle = "eshop-pro";
    private const int PageSize = 200;
    private const int MaxModeledErrorCount = 3;
    private const int MaxModeledErrorLength = 240;
    private static readonly TimeSpan TotalCallBudget = TimeSpan.FromSeconds(20);

    private readonly MaxioAdvancedBillingClient _client;
    private readonly MaxioOptions _options;
    private readonly CatalogContext _catalogContext;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly AsyncKeyedLock _keyedLock;
    private readonly MaxioCallContext _callContext;
    private readonly ILogger<MaxioSubscriptionBillingService> _logger;

    public MaxioSubscriptionBillingService(
        MaxioAdvancedBillingClient client,
        IOptions<MaxioOptions> options,
        CatalogContext catalogContext,
        UserManager<ApplicationUser> userManager,
        AsyncKeyedLock keyedLock,
        MaxioCallContext callContext,
        ILogger<MaxioSubscriptionBillingService> logger)
    {
        _client = client;
        _options = options.Value;
        _catalogContext = catalogContext;
        _userManager = userManager;
        _keyedLock = keyedLock;
        _callContext = callContext;
        _logger = logger;
    }

    public async Task<IReadOnlyList<SubscriptionPlan>> GetPlansAsync(CancellationToken cancellationToken)
    {
        var products = await ListProductsAsync(cancellationToken);
        return products
            .Where(product => product.ArchivedAt is null && !string.IsNullOrWhiteSpace(product.Handle) && !string.IsNullOrWhiteSpace(product.Name))
            .Select(product => new SubscriptionPlan(
                product.Name!,
                product.Handle!,
                product.Description,
                product.PriceInCents,
                product.Interval,
                product.IntervalUnit?.Value,
                product.ProductPricePointHandle))
            .OrderBy(plan => plan.PriceInCents)
            .ThenBy(plan => plan.Name, StringComparer.Ordinal)
            .ToList();
    }

    public async Task<UserSubscription> SubscribeAsync(
        string userName,
        string? productHandle,
        CancellationToken cancellationToken)
    {
        var user = await GetUserAsync(userName);
        var products = await ListProductsAsync(cancellationToken);
        var activeProducts = products
            .Where(product => product.ArchivedAt is null && !string.IsNullOrWhiteSpace(product.Handle))
            .ToList();

        if (activeProducts.Count == 0)
        {
            throw BillingError(HttpStatusCode.NotFound, "no_subscription_plans", "No subscription plans are currently available.");
        }

        var selectedHandle = string.IsNullOrWhiteSpace(productHandle)
            ? activeProducts.FirstOrDefault(product => string.Equals(product.Handle, PreferredDefaultProductHandle, StringComparison.Ordinal))?.Handle
                ?? activeProducts[0].Handle!
            : productHandle.Trim();

        var selectedProduct = activeProducts.SingleOrDefault(product =>
            string.Equals(product.Handle, selectedHandle, StringComparison.Ordinal));
        if (selectedProduct is null)
        {
            throw BillingError(HttpStatusCode.BadRequest, "invalid_product_handle", "The requested subscription plan is not available.");
        }

        using var subscriptionLock = await _keyedLock.AcquireAsync(
            $"subscription:{user.Id}:{selectedHandle}", cancellationToken);

        var customerLink = await EnsureCustomerAsync(user, cancellationToken);
        var subscriptionReference = BuildSubscriptionReference(user.Id, selectedHandle);
        var link = await _catalogContext.MaxioSubscriptionLinks
            .SingleOrDefaultAsync(item => item.UserId == user.Id && item.ProductHandle == selectedHandle, cancellationToken);
        var ownsReservation = false;

        if (link is null)
        {
            link = new MaxioSubscriptionLink(user.Id, selectedHandle, subscriptionReference);
            _catalogContext.MaxioSubscriptionLinks.Add(link);
            try
            {
                await _catalogContext.SaveChangesAsync(cancellationToken);
                ownsReservation = true;
            }
            catch (DbUpdateException)
            {
                _catalogContext.Entry(link).State = EntityState.Detached;
                link = await _catalogContext.MaxioSubscriptionLinks
                    .SingleAsync(item => item.UserId == user.Id && item.ProductHandle == selectedHandle, cancellationToken);
            }
        }

        var existing = await FindSubscriptionAsync(link.SubscriptionReference, cancellationToken);
        if (existing is not null)
        {
            return await ActivateAndMapAsync(link, existing, selectedHandle, cancellationToken);
        }

        if (!ownsReservation)
        {
            if (link.IntegrationStatus == MaxioSubscriptionIntegrationStatus.Failed)
            {
                throw BillingError(HttpStatusCode.UnprocessableEntity, link.FailureCode ?? "subscription_failed",
                    "The subscription could not be created with the selected plan.");
            }

            var status = link.IntegrationStatus == MaxioSubscriptionIntegrationStatus.Pending
                ? HttpStatusCode.Conflict
                : HttpStatusCode.ServiceUnavailable;
            throw BillingError(status, "subscription_outcome_unresolved",
                "The subscription request is still being reconciled and was not sent again.");
        }

        try
        {
            var created = await CreateSubscriptionAsync(
                selectedHandle,
                customerLink.CustomerReference,
                link.SubscriptionReference,
                cancellationToken);
            return await ActivateAndMapAsync(link, created, selectedHandle, cancellationToken);
        }
        catch (AmbiguousMaxioWriteException ex)
        {
            link.MarkAmbiguous();
            await _catalogContext.SaveChangesAsync(CancellationToken.None);
            _logger.LogWarning("Maxio subscription outcome is ambiguous for reference hash {ReferenceHash}.",
                HashForLog(link.SubscriptionReference));

            var reconciled = await FindSubscriptionAsync(link.SubscriptionReference, cancellationToken);
            if (reconciled is not null)
            {
                return await ActivateAndMapAsync(link, reconciled, selectedHandle, cancellationToken);
            }

            throw BillingError(HttpStatusCode.ServiceUnavailable, "subscription_outcome_unresolved",
                "Maxio did not confirm the subscription. The request was not retried to avoid a duplicate.", ex);
        }
        catch (SubscriptionBillingException ex) when ((int)ex.StatusCode is >= 400 and < 500)
        {
            link.MarkFailed(ex.Code);
            await _catalogContext.SaveChangesAsync(CancellationToken.None);
            throw;
        }
    }

    public async Task<IReadOnlyList<UserSubscription>> GetSubscriptionsAsync(
        string userName,
        CancellationToken cancellationToken)
    {
        var user = await GetUserAsync(userName);
        var customerLink = await _catalogContext.MaxioCustomerLinks
            .AsNoTracking()
            .SingleOrDefaultAsync(item => item.UserId == user.Id, cancellationToken);

        if (customerLink?.MaxioCustomerId is null)
        {
            return Array.Empty<UserSubscription>();
        }

        var ownedLinks = await _catalogContext.MaxioSubscriptionLinks
            .AsNoTracking()
            .Where(item => item.UserId == user.Id)
            .ToDictionaryAsync(item => item.SubscriptionReference, StringComparer.Ordinal, cancellationToken);
        if (ownedLinks.Count == 0)
        {
            return Array.Empty<UserSubscription>();
        }

        var subscriptions = await ListCustomerSubscriptionsAsync(customerLink.MaxioCustomerId.Value, cancellationToken);
        return subscriptions
            .Select(envelope => envelope.Subscription)
            .Where(subscription => subscription?.Reference is not null && ownedLinks.ContainsKey(subscription.Reference))
            .Select(subscription => MapSubscription(
                subscription!,
                ownedLinks[subscription!.Reference!].ProductHandle))
            .OrderBy(subscription => subscription.ProductName, StringComparer.Ordinal)
            .ToList();
    }

    private async Task<ApplicationUser> GetUserAsync(string userName)
    {
        if (string.IsNullOrWhiteSpace(userName))
        {
            throw BillingError(HttpStatusCode.Unauthorized, "missing_identity", "An authenticated user identity is required.");
        }

        var user = await _userManager.FindByNameAsync(userName);
        if (user is null)
        {
            throw BillingError(HttpStatusCode.Unauthorized, "unknown_identity", "The authenticated user no longer exists.");
        }

        return user;
    }

    private async Task<MaxioCustomerLink> EnsureCustomerAsync(ApplicationUser user, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(user.FirstName) || string.IsNullOrWhiteSpace(user.LastName) ||
            string.IsNullOrWhiteSpace(user.Email))
        {
            throw BillingError(HttpStatusCode.UnprocessableEntity, "incomplete_billing_profile",
                "First name, last name, and email are required before subscribing.");
        }

        using var customerLock = await _keyedLock.AcquireAsync($"customer:{user.Id}", cancellationToken);
        var customerReference = BuildCustomerReference(user.Id);
        var link = await _catalogContext.MaxioCustomerLinks
            .SingleOrDefaultAsync(item => item.UserId == user.Id, cancellationToken);

        if (link?.MaxioCustomerId is not null)
        {
            return link;
        }

        if (link is null)
        {
            link = new MaxioCustomerLink(user.Id, customerReference);
            _catalogContext.MaxioCustomerLinks.Add(link);
            try
            {
                await _catalogContext.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateException)
            {
                _catalogContext.Entry(link).State = EntityState.Detached;
                link = await _catalogContext.MaxioCustomerLinks
                    .SingleAsync(item => item.UserId == user.Id, cancellationToken);
            }
        }

        var providerCustomer = await ReadCustomerAsync(link.CustomerReference, cancellationToken);
        if (providerCustomer is null)
        {
            providerCustomer = await CreateCustomerAsync(user, link.CustomerReference, cancellationToken);
        }

        if (providerCustomer.Id is null)
        {
            throw BillingError(HttpStatusCode.BadGateway, "invalid_provider_response",
                "Maxio returned a customer without an identifier.");
        }

        link.Connect(providerCustomer.Id.Value);
        await _catalogContext.SaveChangesAsync(cancellationToken);
        return link;
    }

    private async Task<IReadOnlyList<Product>> ListProductsAsync(CancellationToken cancellationToken)
    {
        var products = new List<Product>();
        for (var page = 1; ; page++)
        {
            IReadOnlyList<ProductResponse> response;
            MaxioCallContext.Scope? callScope = null;
            try
            {
                using (callScope = _callContext.Begin())
                {
                    response = await BoundedAsync(token => _client.ProductFamilies.ListProductsForProductFamily(
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
                        perPage: PageSize,
                        ct: token), cancellationToken);
                }
            }
            catch (SdkException<ListProductsForProductFamilyError> ex)
            {
                if (ex.Error.TryGetString(out _))
                {
                    throw BillingError(HttpStatusCode.NotFound, "product_family_not_found",
                        "The configured Maxio product family was not found.", ex);
                }

                if (ex.Error.TryGetRawError(out var raw))
                {
                    throw FromRawError(raw, "list_subscription_plans", ex);
                }

                throw ProviderError("list_subscription_plans", ex);
            }
            catch (JsonException ex) when (TryGetClientError(callScope, out var statusCode))
            {
                throw BillingError(statusCode, $"maxio_{(int)statusCode}",
                    "Maxio rejected the billing request.", ex);
            }
            catch (JsonException ex)
            {
                throw ProviderError("list_subscription_plans", ex);
            }
            catch (Exception ex) when (IsTransportFailure(ex, cancellationToken))
            {
                throw TransportError("list_subscription_plans", ex);
            }

            products.AddRange(response.Select(item => item.Product));
            if (response.Count < PageSize)
            {
                return products;
            }
        }
    }

    private async Task<Customer?> ReadCustomerAsync(string reference, CancellationToken cancellationToken)
    {
        try
        {
            using (_callContext.Begin())
            {
                var response = await BoundedAsync(
                    token => _client.Customers.ReadCustomerByReference(reference, ct: token), cancellationToken);
                return response.Customer;
            }
        }
        catch (SdkException<RawError> ex) when (ex.Error.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
        catch (SdkException<RawError> ex)
        {
            throw FromRawError(ex.Error, "read_customer", ex);
        }
        catch (JsonException ex)
        {
            throw ProviderError("read_customer", ex);
        }
        catch (Exception ex) when (IsTransportFailure(ex, cancellationToken))
        {
            throw TransportError("read_customer", ex);
        }
    }

    private async Task<Customer> CreateCustomerAsync(
        ApplicationUser user,
        string reference,
        CancellationToken cancellationToken)
    {
        MaxioCallContext.Scope? callScope = null;
        try
        {
            using (callScope = _callContext.Begin())
            {
                var response = await BoundedAsync(token => _client.Customers.CreateCustomer(
                    new CreateCustomerRequest
                    {
                        Customer = new CreateCustomer
                        {
                            FirstName = user.FirstName!.Trim(),
                            LastName = user.LastName!.Trim(),
                            Email = user.Email!.Trim(),
                            Reference = reference
                        }
                    },
                    ct: token), cancellationToken);
                return response.Customer;
            }
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

                throw BillingError(HttpStatusCode.UnprocessableEntity, "customer_rejected",
                    "Maxio rejected the customer profile.", ex);
            }

            if (ex.Error.TryGetRawError(out var raw))
            {
                throw FromRawError(raw, "create_customer", ex);
            }

            throw ProviderError("create_customer", ex);
        }
        catch (JsonException ex) when (TryGetClientError(callScope, out var statusCode))
        {
            if (statusCode == HttpStatusCode.UnprocessableEntity)
            {
                var concurrent = await ReadCustomerAsync(reference, cancellationToken);
                if (concurrent is not null)
                {
                    return concurrent;
                }
            }

            throw BillingError(statusCode, "customer_rejected", "Maxio rejected the customer profile.", ex);
        }
        catch (JsonException ex)
        {
            var reconciled = await ReadCustomerAsync(reference, cancellationToken);
            if (reconciled is not null)
            {
                return reconciled;
            }

            throw ProviderError("create_customer", ex);
        }
        catch (Exception ex) when (IsTransportFailure(ex, cancellationToken))
        {
            var reconciled = await ReadCustomerAsync(reference, cancellationToken);
            if (reconciled is not null)
            {
                return reconciled;
            }

            throw TransportError("create_customer", ex);
        }
    }

    private async Task<Subscription?> FindSubscriptionAsync(string reference, CancellationToken cancellationToken)
    {
        try
        {
            using (_callContext.Begin())
            {
                var response = await BoundedAsync(
                    token => _client.Subscriptions.FindSubscription(reference: reference, ct: token), cancellationToken);
                return response.Subscription;
            }
        }
        catch (SdkException<FindSubscriptionError> ex)
        {
            if (ex.Error.TryGetNoContent(out _))
            {
                return null;
            }

            if (ex.Error.TryGetRawError(out var raw))
            {
                throw FromRawError(raw, "find_subscription", ex);
            }

            throw ProviderError("find_subscription", ex);
        }
        catch (JsonException ex)
        {
            throw ProviderError("find_subscription", ex);
        }
        catch (Exception ex) when (IsTransportFailure(ex, cancellationToken))
        {
            throw TransportError("find_subscription", ex);
        }
    }

    private async Task<Subscription> CreateSubscriptionAsync(
        string productHandle,
        string customerReference,
        string subscriptionReference,
        CancellationToken cancellationToken)
    {
        MaxioCallContext.Scope? callScope = null;
        try
        {
            using (callScope = _callContext.Begin(writeOnce: true))
            {
                var response = await BoundedAsync(token => _client.Subscriptions.CreateSubscription(
                    new CreateSubscriptionRequest
                    {
                        Subscription = new CreateSubscription
                        {
                            ProductHandle = productHandle,
                            CustomerReference = customerReference,
                            Reference = subscriptionReference
                        }
                    },
                    ct: token), cancellationToken);

                return response.Subscription ?? throw new AmbiguousMaxioWriteException(
                    "Maxio returned a subscription envelope without a subscription.");
            }
        }
        catch (SdkException<CreateSubscriptionError> ex)
        {
            if (ex.Error.TryGetErrorListResponse1(out var errorResponse))
            {
                throw BillingError(HttpStatusCode.UnprocessableEntity, "subscription_rejected",
                    FormatModeledSubscriptionErrors(errorResponse.Errors), ex);
            }

            if (ex.Error.TryGetRawError(out var raw))
            {
                throw FromRawError(raw, "create_subscription", ex);
            }

            throw ProviderError("create_subscription", ex);
        }
        catch (JsonException ex) when (callScope?.LastStatusCode is >= HttpStatusCode.BadRequest and < HttpStatusCode.InternalServerError)
        {
            throw BillingError(callScope.LastStatusCode!.Value, "subscription_rejected",
                "Maxio rejected the subscription request.", ex);
        }
        catch (Exception ex) when (ex is JsonException or HttpRequestException or TaskCanceledException or MaxioWriteReplayPreventedException)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                throw new OperationCanceledException(cancellationToken);
            }

            throw new AmbiguousMaxioWriteException("The Maxio subscription outcome could not be confirmed.", ex);
        }
    }

    private async Task<IReadOnlyList<SubscriptionResponse>> ListCustomerSubscriptionsAsync(
        int customerId,
        CancellationToken cancellationToken)
    {
        try
        {
            using (_callContext.Begin())
            {
                return await BoundedAsync(
                    token => _client.Customers.ListCustomerSubscriptions(customerId, ct: token), cancellationToken);
            }
        }
        catch (SdkException<RawError> ex)
        {
            throw FromRawError(ex.Error, "list_customer_subscriptions", ex);
        }
        catch (JsonException ex)
        {
            throw ProviderError("list_customer_subscriptions", ex);
        }
        catch (Exception ex) when (IsTransportFailure(ex, cancellationToken))
        {
            throw TransportError("list_customer_subscriptions", ex);
        }
    }

    private async Task<UserSubscription> ActivateAndMapAsync(
        MaxioSubscriptionLink link,
        Subscription subscription,
        string fallbackProductHandle,
        CancellationToken cancellationToken)
    {
        if (subscription.Id is null)
        {
            link.MarkAmbiguous();
            await _catalogContext.SaveChangesAsync(CancellationToken.None);
            throw BillingError(HttpStatusCode.BadGateway, "invalid_provider_response",
                "Maxio returned a subscription without an identifier.");
        }

        link.Activate(subscription.Id.Value);
        await _catalogContext.SaveChangesAsync(cancellationToken);
        return MapSubscription(subscription, fallbackProductHandle);
    }

    private static UserSubscription MapSubscription(Subscription subscription, string fallbackProductHandle) =>
        new(
            subscription.Id ?? 0,
            subscription.Reference,
            subscription.Product?.Name,
            subscription.Product?.Handle ?? fallbackProductHandle,
            subscription.ProductPriceInCents,
            subscription.State?.Value,
            subscription.NextAssessmentAt);

    private async Task<T> BoundedAsync<T>(Func<CancellationToken, Task<T>> operation, CancellationToken cancellationToken)
    {
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        budget.CancelAfter(TotalCallBudget);
        try
        {
            return await operation(budget.Token);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
    }

    private static bool IsTransportFailure(Exception exception, CancellationToken callerToken) =>
        exception is HttpRequestException ||
        exception is TaskCanceledException && !callerToken.IsCancellationRequested;

    private static bool TryGetClientError(MaxioCallContext.Scope? scope, out HttpStatusCode statusCode)
    {
        statusCode = scope?.LastStatusCode ?? default;
        return (int)statusCode is >= 400 and < 500;
    }

    private static SubscriptionBillingException FromRawError(RawError raw, string operation, Exception exception)
    {
        if (raw.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
        {
            return ProviderError(operation, exception);
        }

        if ((int)raw.StatusCode is >= 400 and < 500)
        {
            return BillingError(raw.StatusCode, $"maxio_{(int)raw.StatusCode}",
                "Maxio rejected the billing request.", exception);
        }

        return ProviderError(operation, exception);
    }

    private static SubscriptionBillingException TransportError(string operation, Exception exception) =>
        BillingError(HttpStatusCode.ServiceUnavailable, "maxio_unavailable",
            "Maxio is temporarily unavailable. Please try again later.", exception);

    private static SubscriptionBillingException ProviderError(string operation, Exception exception) =>
        BillingError(HttpStatusCode.BadGateway, "maxio_invalid_response",
            "Maxio returned a response that could not be processed.", exception);

    private static string FormatModeledSubscriptionErrors(IReadOnlyList<string> errors)
    {
        var messages = errors
            .Where(error => !string.IsNullOrWhiteSpace(error))
            .Select(NormalizeModeledError)
            .Where(error => error.Length > 0)
            .Take(MaxModeledErrorCount)
            .ToList();

        return messages.Count == 0
            ? "Maxio rejected the subscription request."
            : $"Maxio rejected the subscription request: {string.Join("; ", messages)}";
    }

    private static string NormalizeModeledError(string error)
    {
        var normalized = string.Join(" ", error
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

        return normalized.Length <= MaxModeledErrorLength
            ? normalized
            : $"{normalized[..MaxModeledErrorLength]}…";
    }

    private static SubscriptionBillingException BillingError(
        HttpStatusCode statusCode,
        string code,
        string message,
        Exception? exception = null) => new(statusCode, message, code, exception);

    private static string BuildCustomerReference(string userId) => $"eshop-user-{userId}";

    private static string BuildSubscriptionReference(string userId, string productHandle)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes($"{userId}\n{productHandle}"));
        return $"eshop-sub-{Convert.ToHexString(hash)[..24].ToLowerInvariant()}";
    }

    private static string HashForLog(string value)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return Convert.ToHexString(hash)[..12].ToLowerInvariant();
    }

    private sealed class AmbiguousMaxioWriteException : Exception
    {
        public AmbiguousMaxioWriteException(string message, Exception? innerException = null)
            : base(message, innerException) { }
    }
}
