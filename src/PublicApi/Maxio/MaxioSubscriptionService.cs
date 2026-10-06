using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.Authentication.Basic;
using MaxioAdvancedBilling.Core.Configuration;
using MaxioAdvancedBilling.Core.ErrorResponse;
using MaxioAdvancedBilling.Core.Exceptions;
using MaxioAdvancedBilling.Errors;
using MaxioAdvancedBilling.Models;
using MaxioAdvancedBilling.Models.Enums;
using MaxioAdvancedBilling.Servers;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

public sealed class MaxioSubscriptionService : IMaxioSubscriptionService
{
    public const string HttpClientName = "MaxioAdvancedBilling";
    private const int PageSize = 50;
    private const int MaxPages = 20;
    private static readonly TimeSpan CallBudget = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan PerAttemptTimeout = TimeSpan.FromSeconds(10);

    private static readonly ConcurrentDictionary<string, SemaphoreSlim> Locks = new();

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IOptions<MaxioOptions> _options;
    private readonly IRepository<MaxioSubscription> _subscriptions;
    private readonly ILogger<MaxioSubscriptionService> _logger;

    private MaxioAdvancedBillingClient? _client;
    private int _cachedFamilyId;

    public MaxioSubscriptionService(
        IHttpClientFactory httpClientFactory,
        IOptions<MaxioOptions> options,
        IRepository<MaxioSubscription> subscriptions,
        ILogger<MaxioSubscriptionService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _options = options;
        _subscriptions = subscriptions;
        _logger = logger;
    }

    public async Task<MaxioPlanCatalog> GetPlanCatalogAsync(CancellationToken cancellationToken)
    {
        var familyId = await ResolveFamilyIdAsync(cancellationToken);
        var products = await ListAllProductsAsync(familyId, cancellationToken);
        var components = await ListAllComponentsAsync(familyId, cancellationToken);

        var plans = products.Select(ToPlanDto).ToList();
        var componentDtos = components.Select(ToComponentDto).ToList();
        return new MaxioPlanCatalog(_options.Value.ProductFamilyHandle, plans, componentDtos);
    }

    public async Task<MaxioSubscribeResult> SubscribeAsync(string userId, string email, string planHandle, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(userId))
            throw new MaxioIntegrationException(400, "A user identity is required to subscribe.");
        if (string.IsNullOrWhiteSpace(email))
            throw new MaxioIntegrationException(400, "A user email is required to subscribe.");
        if (string.IsNullOrWhiteSpace(planHandle))
            throw new MaxioIntegrationException(400, "A planHandle is required to subscribe.");

        var planLock = AcquireLock($"s:{userId}:{planHandle}");
        await planLock.WaitAsync(cancellationToken);
        try
        {
            var familyId = await ResolveFamilyIdAsync(cancellationToken);
            var product = await GetPlanAsync(planHandle, familyId, cancellationToken);
            var reference = BuildSubscriptionReference(userId, planHandle);

            var local = await _subscriptions.FirstOrDefaultAsync(
                new MaxioSubscriptionForUserPlanSpecification(userId, planHandle), cancellationToken);
            if (local is not null)
            {
                var existingForLocal = await FindSubscriptionAsync(reference, cancellationToken);
                if (existingForLocal is not null && IsLiveState(existingForLocal.State))
                {
                    await PersistAsync(userId, planHandle, existingForLocal, cancellationToken);
                    return new MaxioSubscribeResult(MapSubscription(existingForLocal), false);
                }
                await _subscriptions.DeleteAsync(local, cancellationToken);
            }

            var customer = await EnsureCustomerAsync(userId, email, cancellationToken);

            var found = await FindSubscriptionAsync(reference, cancellationToken);
            if (found is not null && IsLiveState(found.State))
            {
                await PersistAsync(userId, planHandle, found, cancellationToken);
                return new MaxioSubscribeResult(MapSubscription(found), false);
            }

            Subscription subscription;
            try
            {
                var response = await CallAsync("create subscription", t => _client!.Subscriptions.CreateSubscription(
                    new CreateSubscriptionRequest
                    {
                        Subscription = new CreateSubscription
                        {
                            ProductHandle = product.Handle,
                            CustomerId = customer.Id,
                            Reference = reference,
                            PaymentCollectionMethod = CollectionMethod.Remittance
                        }
                    }, t), cancellationToken);
                subscription = response.Subscription
                    ?? throw new MaxioIntegrationException(502, "The billing provider returned an empty subscription.");
            }
            catch (MaxioIntegrationException ex) when (ex.StatusCode is 502 or 504
                && ex.InnerException is HttpRequestException or TaskCanceledException)
            {
                _logger.LogWarning(ex, "The subscription create for {Reference} may not have been received; reconciling via reference lookup.", reference);
                var reconciled = await FindSubscriptionAsync(reference, cancellationToken);
                if (reconciled is null || !IsLiveState(reconciled.State))
                    throw new MaxioIntegrationException(502, "The billing provider did not confirm the subscription. Please retry.", ex);
                subscription = reconciled;
            }
            catch (SdkException<CreateSubscriptionError> ex)
            {
                if (ex.Error.TryGetErrorListResponse1(out var errorList))
                    throw new MaxioIntegrationException(422,
                        errorList.Errors is { Count: > 0 } ? string.Join(" ", errorList.Errors) : "The billing provider rejected the subscription request.", ex);
                if (ex.Error.TryGetRawError(out var raw))
                    throw FromRaw(raw, "subscribe", ex);
                throw new MaxioIntegrationException(502, "The billing provider rejected the subscription request.", ex);
            }

            await PersistAsync(userId, planHandle, subscription, cancellationToken);
            return new MaxioSubscribeResult(MapSubscription(subscription), true);
        }
        finally
        {
            planLock.Release();
        }
    }

    public async Task<IReadOnlyList<MaxioSubscriptionInfo>> GetSubscriptionsForUserAsync(string userId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(userId))
            throw new MaxioIntegrationException(400, "A user identity is required.");

        Customer? customer;
        try
        {
            var response = await CallAsync("read customer by reference", t => _client!.Customers.ReadCustomerByReference(userId, t), cancellationToken);
            customer = response.Customer;
        }
        catch (SdkException<RawError> ex) when (ex.Error.StatusCode == HttpStatusCode.NotFound)
        {
            return Array.Empty<MaxioSubscriptionInfo>();
        }
        if (customer is null || customer.Id is null)
            throw new MaxioIntegrationException(502, "The billing provider returned an empty customer.");

        var responses = await CallAsync("list customer subscriptions", t => _client!.Customers.ListCustomerSubscriptions(customer.Id.Value, t), cancellationToken);
        return responses.Select(r => r.Subscription).Where(s => s is not null).Select(s => MapSubscription(s!)).ToList();
    }

    private async Task<Customer> EnsureCustomerAsync(string userId, string email, CancellationToken cancellationToken)
    {
        var userLock = AcquireLock($"u:{userId}");
        await userLock.WaitAsync(cancellationToken);
        try
        {
            Customer? customer = await TryReadCustomerByReferenceAsync(userId, cancellationToken);
            if (customer is not null)
                return customer;

            var (firstName, lastName) = DeriveNames(email);
            try
            {
                var created = await CallAsync("create customer", t => _client!.Customers.CreateCustomer(
                    new CreateCustomerRequest
                    {
                        Customer = new CreateCustomer
                        {
                            FirstName = firstName,
                            LastName = lastName,
                            Email = email,
                            Reference = userId
                        }
                    }, t), cancellationToken);
                return created.Customer
                    ?? throw new MaxioIntegrationException(502, "The billing provider returned an empty customer.");
            }
            catch (SdkException<CreateCustomerError> ex)
            {
                var isConflict = ex.Error.TryGetCustomerErrorResponse1(out _)
                    || (ex.Error.TryGetRawError(out var raw) && raw.StatusCode == HttpStatusCode.UnprocessableEntity);
                if (!isConflict)
                    throw TranslateCreateCustomerError(ex);

                customer = await TryReadCustomerByReferenceAsync(userId, cancellationToken);
                if (customer is not null)
                    return customer;
                throw new MaxioIntegrationException(502, "The billing provider did not confirm the customer. Please retry.", ex);
            }
        }
        finally
        {
            userLock.Release();
        }
    }

    private async Task<Customer?> TryReadCustomerByReferenceAsync(string reference, CancellationToken cancellationToken)
    {
        try
        {
            var response = await CallAsync("read customer by reference", t => _client!.Customers.ReadCustomerByReference(reference, t), cancellationToken);
            return response.Customer;
        }
        catch (SdkException<RawError> ex) when (ex.Error.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    private async Task<Subscription?> FindSubscriptionAsync(string reference, CancellationToken cancellationToken)
    {
        try
        {
            var response = await CallAsync("find subscription", t => _client!.Subscriptions.FindSubscription(reference, t), cancellationToken);
            return response.Subscription;
        }
        catch (SdkException<FindSubscriptionError> ex)
        {
            if (ex.Error.TryGetNoContent(out _))
                return null;
            if (ex.Error.TryGetRawError(out var raw))
            {
                if (raw.StatusCode == HttpStatusCode.NotFound)
                    return null;
                throw FromRaw(raw, "find subscription", ex);
            }
            throw new MaxioIntegrationException(502, "The billing provider returned an error while looking up the subscription.", ex);
        }
    }

    private async Task<Product> GetPlanAsync(string planHandle, int familyId, CancellationToken cancellationToken)
    {
        ProductResponse response;
        try
        {
            response = await CallAsync("read product by handle", t => _client!.Products.ReadProductByHandle(planHandle, t), cancellationToken);
        }
        catch (SdkException<RawError> ex) when (ex.Error.StatusCode == HttpStatusCode.NotFound)
        {
            throw new MaxioIntegrationException(404, $"Plan '{planHandle}' was not found.");
        }
        var product = response.Product;
        if (product is null)
            throw new MaxioIntegrationException(502, "The billing provider returned an empty product.");
        if (product.ProductFamily?.Id != familyId)
            throw new MaxioIntegrationException(404, $"Plan '{planHandle}' is not part of the configured subscription catalog.");
        return product;
    }

    private async Task<int> ResolveFamilyIdAsync(CancellationToken cancellationToken)
    {
        if (_cachedFamilyId != 0)
            return _cachedFamilyId;

        var handle = _options.Value.ProductFamilyHandle;
        IReadOnlyList<ProductFamilyResponse> families = await CallAsync("list product families", t => _client!.ProductFamilies.ListProductFamilies(
            dateField: null,
            startDate: null,
            endDate: null,
            startDatetime: null,
            endDatetime: null,
            ct: t), cancellationToken);

        var family = families.Select(f => f.ProductFamily).FirstOrDefault(f => f?.Handle == handle);
        if (family?.Id is null)
            throw new MaxioIntegrationException(500, $"The configured Maxio product family '{handle}' was not found.");
        _cachedFamilyId = family.Id.Value;
        return _cachedFamilyId;
    }

    private async Task<List<Product>> ListAllProductsAsync(int familyId, CancellationToken cancellationToken)
    {
        var products = new List<Product>();
        for (var page = 1; page <= MaxPages; page++)
        {
            IReadOnlyList<ProductResponse> responses;
            try
            {
                responses = await CallAsync("list products", t => _client!.ProductFamilies.ListProductsForProductFamily(
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
                    ct: t), cancellationToken);
            }
            catch (SdkException<ListProductsForProductFamilyError> ex)
            {
                if (ex.Error.TryGetString(out _)
                    || (ex.Error.TryGetRawError(out var raw) && raw.StatusCode == HttpStatusCode.NotFound))
                    throw new MaxioIntegrationException(500, $"The configured Maxio product family '{_options.Value.ProductFamilyHandle}' was not found.");
                if (ex.Error.TryGetRawError(out var rawError))
                    throw FromRaw(rawError, "list products", ex);
                throw new MaxioIntegrationException(502, "The billing provider returned an error while listing products.", ex);
            }
            products.AddRange(responses.Select(r => r.Product).Where(p => p is not null).Select(p => p!));
            if (responses.Count < PageSize)
                break;
        }
        return products;
    }

    private async Task<List<Component>> ListAllComponentsAsync(int familyId, CancellationToken cancellationToken)
    {
        var components = new List<Component>();
        for (var page = 1; page <= MaxPages; page++)
        {
            var responses = await CallAsync("list components", t => _client!.Components.ListComponentsForProductFamily(
                productFamilyId: familyId,
                includeArchived: false,
                filter: null,
                dateField: null,
                endDate: null,
                endDatetime: null,
                startDate: null,
                startDatetime: null,
                page: page,
                perPage: PageSize,
                ct: t), cancellationToken);
            components.AddRange(responses.Select(r => r.Component).Where(c => c is not null).Select(c => c!));
            if (responses.Count < PageSize)
                break;
        }
        return components;
    }

    private async Task<T> CallAsync<T>(string operation, Func<CancellationToken, Task<T>> call, CancellationToken cancellationToken)
    {
        EnsureClient();
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(CallBudget);
        try
        {
            return await call(cts.Token);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "The billing provider is unreachable during '{Operation}'.", operation);
            throw new MaxioIntegrationException(502, "The billing provider is currently unreachable. Please try again later.", ex);
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogError(ex, "The billing provider did not respond in time during '{Operation}'.", operation);
            throw new MaxioIntegrationException(504, "The billing provider did not respond in time. Please try again later.", ex);
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "An unreadable response was received from the billing provider during '{Operation}'.", operation);
            throw new MaxioIntegrationException(502, "The billing provider returned a response that could not be processed.", ex);
        }
    }

    private void EnsureClient()
    {
        if (_client is not null)
            return;

        var options = _options.Value;
        var missing = new List<string>();
        if (string.IsNullOrWhiteSpace(options.ApiKey))
            missing.Add("Maxio:ApiKey");
        if (string.IsNullOrWhiteSpace(options.ProductFamilyHandle))
            missing.Add("Maxio:ProductFamilyHandle");
        if (string.IsNullOrWhiteSpace(options.BaseUrl) && string.IsNullOrWhiteSpace(options.Subdomain))
            missing.Add("Maxio:Subdomain (or Maxio:BaseUrl)");
        if (missing.Count > 0)
            throw new InvalidOperationException($"The Maxio integration is not configured. Missing configuration keys: {string.Join(", ", missing)}.");

        var clientOptions = new MaxioAdvancedBillingClientOptions
        {
            BasicAuth = new BasicAuthCredentials { Username = options.ApiKey, Password = "x" },
            Environment = ServerEnvironment.Us,
            Retry = RetryOptions.Default() with
            {
                MaxRetries = 1,
                Timeout = PerAttemptTimeout
            }
        };
        if (!string.IsNullOrWhiteSpace(options.BaseUrl))
        {
            clientOptions.Server.Production.Us.BaseUrl = options.BaseUrl;
        }
        else
        {
            clientOptions.Server.Production.Us.Site = options.Subdomain;
        }

        _client = new MaxioAdvancedBillingClient(_httpClientFactory.CreateClient(HttpClientName), clientOptions);
    }

    private static SemaphoreSlim AcquireLock(string key) =>
        Locks.GetOrAdd(key, _ => new SemaphoreSlim(1, 1));

    private static string BuildSubscriptionReference(string userId, string planHandle) =>
        $"{userId}:{planHandle}";

    private static bool IsLiveState(SubscriptionState? state) =>
        state is null
        || (state != SubscriptionState.Canceled
            && state != SubscriptionState.Expired
            && state != SubscriptionState.FailedToCreate);

    private async Task PersistAsync(string userId, string planHandle, Subscription subscription, CancellationToken cancellationToken)
    {
        var existing = await _subscriptions.FirstOrDefaultAsync(
            new MaxioSubscriptionForUserPlanSpecification(userId, planHandle), cancellationToken);
        if (existing is not null)
        {
            existing.UpdateFromMaxio(
                subscription.Id ?? 0,
                subscription.Customer?.Id ?? 0,
                subscription.State?.Value,
                subscription.ProductPriceInCents,
                subscription.Currency,
                subscription.NextAssessmentAt);
            await _subscriptions.UpdateAsync(existing, cancellationToken);
            return;
        }

        await _subscriptions.AddAsync(new MaxioSubscription(
            userId,
            planHandle,
            subscription.Id ?? 0,
            subscription.Customer?.Id ?? 0,
            subscription.State?.Value,
            subscription.ProductPriceInCents,
            subscription.Currency,
            subscription.NextAssessmentAt), cancellationToken);
    }

    private MaxioSubscriptionInfo MapSubscription(Subscription subscription) => new(
        subscription.Id ?? 0,
        subscription.Product?.Handle ?? string.Empty,
        subscription.Product?.Name,
        subscription.ProductPriceInCents,
        subscription.Currency,
        subscription.State?.Value,
        subscription.NextAssessmentAt,
        subscription.CurrentPeriodEndsAt,
        subscription.PaymentCollectionMethod?.Value);

    private static MaxioPlanDto ToPlanDto(Product product)
    {
        var interval = product.Interval ?? 1;
        var unit = product.IntervalUnit?.Value ?? "month";
        var priceInCents = product.PriceInCents ?? 0;
        var priceDisplay = interval == 1
            ? $"{FormatPrice(priceInCents)}/{unit}"
            : $"{FormatPrice(priceInCents)} every {interval} {unit}s";
        return new MaxioPlanDto(
            product.Handle ?? string.Empty,
            product.Name ?? string.Empty,
            product.Description,
            priceInCents,
            priceDisplay,
            product.Interval,
            product.IntervalUnit?.Value,
            product.RequireCreditCard ?? false);
    }

    private static MaxioComponentDto ToComponentDto(Component component) => new(
        component.Handle ?? string.Empty,
        component.Name ?? string.Empty,
        component.Kind?.Value,
        component.UnitName,
        component.PricePerUnitInCents);

    private static string FormatPrice(long priceInCents) =>
        (priceInCents / 100m).ToString("0.00", CultureInfo.InvariantCulture) + " USD";

    private static (string FirstName, string LastName) DeriveNames(string email)
    {
        var localPart = email.Split('@')[0];
        var parts = localPart.Split(new[] { '.', '_', '-' }, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
            return ("eShop", "Shopper");
        var firstName = Capitalize(parts[0]);
        var lastName = parts.Length > 1
            ? Capitalize(string.Join(" ", parts.Skip(1)))
            : "Shopper";
        return (firstName, lastName);
    }

    private static string Capitalize(string value)
    {
        if (string.IsNullOrEmpty(value))
            return value;
        return char.ToUpperInvariant(value[0]) + value[1..].ToLowerInvariant();
    }

    private static MaxioIntegrationException FromRaw(RawError raw, string operation, Exception inner) =>
        new((int)raw.StatusCode, $"The billing provider returned HTTP {(int)raw.StatusCode} during '{operation}'.", inner);

    private static MaxioIntegrationException TranslateCreateCustomerError(SdkException<CreateCustomerError> ex)
    {
        if (ex.Error.TryGetRawError(out var raw))
            return FromRaw(raw, "create customer", ex);
        return new MaxioIntegrationException(502, "The billing provider rejected the customer request.", ex);
    }
}