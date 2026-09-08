using System;
using System.Collections.Generic;
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
using Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.MaxioBilling;

public class MaxioBillingService : IMaxioBillingService
{
    public const string HttpClientName = "MaxioAdvancedBilling";

    private static readonly TimeSpan CallBudget = TimeSpan.FromSeconds(30);
    private const int ProductsPerPage = 20;
    private const int MaxProductPages = 10;

    private readonly MaxioAdvancedBillingClient _client;
    private readonly ISubscriptionMappingStore _mappingStore;
    private readonly MaxioSettings _settings;
    private readonly ILogger<MaxioBillingService> _logger;

    public MaxioBillingService(
        MaxioAdvancedBillingClient client,
        ISubscriptionMappingStore mappingStore,
        IOptions<MaxioSettings> settings,
        ILogger<MaxioBillingService> logger)
    {
        _client = client;
        _mappingStore = mappingStore;
        _settings = settings.Value;
        _logger = logger;
    }

    public async Task<IReadOnlyList<SubscriptionPlanDto>> ListPlansAsync(CancellationToken ct = default)
    {
        var family = await ResolveFamilyAsync(ct);
        var products = await ListFamilyProductsAsync(family.Id!.Value, ct);
        return products
            .Where(p => p.ArchivedAt is null)
            .Select(MapPlan)
            .ToList();
    }

    public async Task<(SubscriptionDto Subscription, bool Created)> SubscribeAsync(BillingUser user, string planHandle, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(planHandle))
        {
            throw new MaxioBillingException("A plan handle is required.", 400);
        }

        var product = await GetPlanProductAsync(planHandle.Trim(), ct);

        var customerLock = await _mappingStore.AcquireLockAsync("customer:" + user.UserId, ct);
        try
        {
            await EnsureCustomerAsync(user, ct);

            var subscriptionReference = SubscriptionReference(user.UserId, product.Handle!);
            var subscriptionLock = await _mappingStore.AcquireLockAsync("subscription:" + subscriptionReference, ct);
            try
            {
                var (subscription, created) = await EnsureSubscriptionAsync(user, product, subscriptionReference, ct);
                return (MapSubscription(subscription), created);
            }
            finally
            {
                subscriptionLock.Dispose();
            }
        }
        finally
        {
            customerLock.Dispose();
        }
    }

    public async Task<IReadOnlyList<SubscriptionDto>> ListForUserAsync(BillingUser user, CancellationToken ct = default)
    {
        var customer = await FindCustomerAsync(user.UserId, ct);
        if (customer?.Id is null)
        {
            return Array.Empty<SubscriptionDto>();
        }

        var responses = await Bounded(async token =>
        {
            try
            {
                return await _client.Customers.ListCustomerSubscriptions(customer.Id.Value, ct: token);
            }
            catch (SdkException<RawError> ex)
            {
                throw new MaxioBillingException("The billing provider rejected the subscription list request.", (int)ex.Error.StatusCode, ex);
            }
        }, ct);

        return responses
            .Select(r => r.Subscription)
            .Where(s => s is not null)
            .Select(s => MapSubscription(s!))
            .ToList();
    }

    private async Task<ProductFamily> ResolveFamilyAsync(CancellationToken ct)
    {
        var families = await Bounded(async token =>
        {
            try
            {
                return await _client.ProductFamilies.ListProductFamilies(
                    dateField: null, startDate: null, endDate: null, startDatetime: null, endDatetime: null, ct: token);
            }
            catch (SdkException<RawError> ex)
            {
                throw new MaxioBillingException("The billing provider rejected the plan catalog request.", (int)ex.Error.StatusCode, ex);
            }
        }, ct);

        var family = families
            .Select(f => f.ProductFamily)
            .FirstOrDefault(f => f is not null && string.Equals(f.Handle, _settings.ProductFamilyHandle, StringComparison.OrdinalIgnoreCase));

        if (family?.Id is null)
        {
            _logger.LogError("Configured Maxio product family {FamilyHandle} was not found", _settings.ProductFamilyHandle);
            throw new MaxioBillingException("The subscription plan catalog is not available.", 500);
        }

        return family;
    }

    private async Task<List<Product>> ListFamilyProductsAsync(int familyId, CancellationToken ct)
    {
        var products = new List<Product>();
        for (var page = 1; page <= MaxProductPages; page++)
        {
            var responses = await Bounded(async token =>
            {
                try
                {
                    return await _client.ProductFamilies.ListProductsForProductFamily(
                        familyId.ToString(),
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
                        ct: token);
                }
                catch (SdkException<ListProductsForProductFamilyError> ex)
                {
                    if (ex.Error.TryGetString(out _))
                    {
                        throw new MaxioBillingException("The subscription plan catalog is not available.", 404, ex);
                    }
                    if (ex.Error.TryGetRawError(out var raw))
                    {
                        throw new MaxioBillingException("The billing provider rejected the plan catalog request.", (int)raw.StatusCode, ex);
                    }
                    throw new MaxioBillingException("The billing provider rejected the plan catalog request.", null, ex);
                }
            }, ct);

            products.AddRange(responses.Select(r => r.Product));
            if (responses.Count < ProductsPerPage)
            {
                break;
            }
        }

        return products;
    }

    private async Task<Product> GetPlanProductAsync(string planHandle, CancellationToken ct)
    {
        var response = await Bounded(async token =>
        {
            try
            {
                return await _client.Products.ReadProductByHandle(planHandle, ct: token);
            }
            catch (SdkException<RawError> ex) when (ex.Error.StatusCode == HttpStatusCode.NotFound)
            {
                throw new MaxioBillingException($"Unknown subscription plan '{planHandle}'.", 404, ex);
            }
            catch (SdkException<RawError> ex)
            {
                throw new MaxioBillingException("The billing provider rejected the plan request.", (int)ex.Error.StatusCode, ex);
            }
        }, ct);

        var product = response.Product;
        if (product?.Id is null)
        {
            throw new MaxioBillingException($"Unknown subscription plan '{planHandle}'.", 404);
        }
        if (product.ProductFamily is not null &&
            !string.Equals(product.ProductFamily.Handle, _settings.ProductFamilyHandle, StringComparison.OrdinalIgnoreCase))
        {
            throw new MaxioBillingException($"Unknown subscription plan '{planHandle}'.", 404);
        }
        if (product.ArchivedAt is not null)
        {
            throw new MaxioBillingException($"Subscription plan '{planHandle}' is no longer available.", 410);
        }

        return product;
    }

    private async Task<Customer> EnsureCustomerAsync(BillingUser user, CancellationToken ct)
    {
        var existing = await FindCustomerAsync(user.UserId, ct);
        if (existing is not null)
        {
            if (existing.Id is not null)
            {
                _mappingStore.SetCustomerId(user.UserId, existing.Id.Value);
            }
            return existing;
        }

        var created = await CreateCustomerAsync(user, ct);
        if (created.Id is not null)
        {
            _mappingStore.SetCustomerId(user.UserId, created.Id.Value);
        }
        return created;
    }

    private async Task<Customer?> FindCustomerAsync(string userId, CancellationToken ct)
    {
        return await Bounded(async token =>
        {
            try
            {
                var response = await _client.Customers.ReadCustomerByReference(userId, ct: token);
                return response.Customer;
            }
            catch (SdkException<RawError> ex) when (ex.Error.StatusCode == HttpStatusCode.NotFound)
            {
                return null;
            }
            catch (SdkException<RawError> ex)
            {
                throw new MaxioBillingException("The billing provider rejected the customer lookup.", (int)ex.Error.StatusCode, ex);
            }
        }, ct);
    }

    private async Task<Customer> CreateCustomerAsync(BillingUser user, CancellationToken ct)
    {
        var body = new CreateCustomerRequest
        {
            Customer = new CreateCustomer
            {
                FirstName = user.FirstName,
                LastName = user.LastName,
                Email = user.Email,
                Reference = user.UserId,
            },
        };

        return await Bounded(async token =>
        {
            try
            {
                var response = await _client.Customers.CreateCustomer(body, ct: token);
                return response.Customer;
            }
            catch (SdkException<CreateCustomerError> ex)
            {
                if (ex.Error.TryGetCustomerErrorResponse1(out _))
                {
                    var recovered = await FindCustomerAsync(user.UserId, token);
                    if (recovered is not null)
                    {
                        return recovered;
                    }
                }
                else if (ex.Error.TryGetRawError(out var raw))
                {
                    throw new MaxioBillingException("The billing provider rejected the customer request.", (int)raw.StatusCode, ex);
                }
                throw new MaxioBillingException("The billing provider rejected the customer request.", null, ex);
            }
        }, ct);
    }

    private async Task<(Subscription, bool)> EnsureSubscriptionAsync(BillingUser user, Product product, string reference, CancellationToken ct)
    {
        var cachedId = _mappingStore.TryGetSubscriptionId(reference);
        if (cachedId is not null)
        {
            var cached = await ReadSubscriptionAsync(cachedId.Value, ct);
            if (cached is not null)
            {
                return (cached, false);
            }
        }

        var existing = await FindSubscriptionAsync(reference, ct);
        if (existing is not null)
        {
            if (existing.Id is not null)
            {
                _mappingStore.SetSubscriptionId(reference, existing.Id.Value);
            }
            return (existing, false);
        }

        try
        {
            var created = await CreateSubscriptionAsync(user, product, reference, ct);
            if (created.Id is not null)
            {
                _mappingStore.SetSubscriptionId(reference, created.Id.Value);
            }
            return (created, true);
        }
        catch (MaxioBillingException ex) when (ex.StatusCode == 422)
        {
            var recovered = await FindSubscriptionAsync(reference, ct);
            if (recovered is not null)
            {
                if (recovered.Id is not null)
                {
                    _mappingStore.SetSubscriptionId(reference, recovered.Id.Value);
                }
                return (recovered, false);
            }
            throw;
        }
    }

    private async Task<Subscription?> FindSubscriptionAsync(string reference, CancellationToken ct)
    {
        return await Bounded(async token =>
        {
            try
            {
                var response = await _client.Subscriptions.FindSubscription(reference: reference, ct: token);
                return response.Subscription;
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
                    throw new MaxioBillingException("The billing provider rejected the subscription lookup.", (int)raw.StatusCode, ex);
                }
                throw new MaxioBillingException("The billing provider rejected the subscription lookup.", null, ex);
            }
        }, ct);
    }

    private async Task<Subscription> CreateSubscriptionAsync(BillingUser user, Product product, string reference, CancellationToken ct)
    {
        var body = new CreateSubscriptionRequest
        {
            Subscription = new CreateSubscription
            {
                ProductHandle = product.Handle,
                CustomerReference = user.UserId,
                Reference = reference,
                PaymentCollectionMethod = CollectionMethod.Remittance,
            },
        };

        return await Bounded(async token =>
        {
            try
            {
                var response = await _client.Subscriptions.CreateSubscription(body, ct: token);
                return response.Subscription
                    ?? throw new MaxioBillingException("The billing provider returned an empty subscription.", null);
            }
            catch (SdkException<CreateSubscriptionError> ex)
            {
                if (ex.Error.TryGetErrorListResponse1(out var errors) && errors.Errors is { Count: > 0 })
                {
                    throw new MaxioBillingException(string.Join(" ", errors.Errors), 422, ex);
                }
                if (ex.Error.TryGetRawError(out var raw))
                {
                    throw new MaxioBillingException("The billing provider rejected the subscription request.", (int)raw.StatusCode, ex);
                }
                throw new MaxioBillingException("The billing provider rejected the subscription request.", null, ex);
            }
        }, ct);
    }

    private async Task<Subscription?> ReadSubscriptionAsync(int subscriptionId, CancellationToken ct)
    {
        return await Bounded(async token =>
        {
            try
            {
                var response = await _client.Subscriptions.ReadSubscription(subscriptionId, include: null, ct: token);
                return response.Subscription;
            }
            catch (SdkException<RawError> ex) when (ex.Error.StatusCode == HttpStatusCode.NotFound)
            {
                return null;
            }
            catch (SdkException<RawError> ex)
            {
                throw new MaxioBillingException("The billing provider rejected the subscription read.", (int)ex.Error.StatusCode, ex);
            }
        }, ct);
    }

    private async Task<T> Bounded<T>(Func<CancellationToken, Task<T>> call, CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(CallBudget);
        try
        {
            return await call(cts.Token);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Maxio returned a response body that could not be parsed");
            throw new MaxioBillingException("The billing provider returned a response that could not be processed.", 502, ex);
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException)
        {
            _logger.LogError(ex, "Maxio request failed or timed out");
            throw new MaxioBillingException("The billing provider is unavailable.", 503, ex);
        }
    }

    private static string SubscriptionReference(string userId, string planHandle) => $"{userId}:{planHandle}";

    private static SubscriptionPlanDto MapPlan(Product product) => new()
    {
        Handle = product.Handle ?? string.Empty,
        Name = product.Name ?? string.Empty,
        PriceInCents = product.PriceInCents ?? 0,
        PriceDisplay = FormatPrice(product.PriceInCents ?? 0),
        Interval = product.Interval,
        IntervalUnit = product.IntervalUnit?.Value,
    };

    private static SubscriptionDto MapSubscription(Subscription subscription) => new()
    {
        Id = subscription.Id ?? 0,
        Reference = subscription.Reference,
        PlanHandle = subscription.Product?.Handle ?? string.Empty,
        PlanName = subscription.Product?.Name ?? string.Empty,
        PriceInCents = subscription.ProductPriceInCents ?? 0,
        PriceDisplay = FormatPrice(subscription.ProductPriceInCents ?? 0),
        State = subscription.State?.Value,
        NextBillingDate = subscription.CurrentPeriodEndsAt ?? subscription.NextAssessmentAt,
        CreatedAt = subscription.CreatedAt,
    };

    private static string FormatPrice(long priceInCents) => $"${priceInCents / 100}.{priceInCents % 100:00}";
}
