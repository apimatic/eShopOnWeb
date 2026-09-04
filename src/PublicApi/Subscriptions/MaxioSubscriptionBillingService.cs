using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Api;
using MaxioAdvancedBilling.Core.ErrorResponse;
using MaxioAdvancedBilling.Core.Exceptions;
using MaxioAdvancedBilling.Errors;
using MaxioAdvancedBilling.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

public sealed class MaxioSubscriptionBillingService : ISubscriptionBillingService
{
    private const int ProductsPageSize = 100;
    private static readonly TimeSpan ProviderCallBudget = TimeSpan.FromSeconds(30);

    private readonly MaxioAdvancedBillingClient _client;
    private readonly MaxioOptions _settings;
    private readonly ILogger<MaxioSubscriptionBillingService> _logger;
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _userLocks = new();

    public MaxioSubscriptionBillingService(
        MaxioAdvancedBillingClient client,
        IOptions<MaxioOptions> settings,
        ILogger<MaxioSubscriptionBillingService> logger)
    {
        _client = client;
        _settings = settings.Value;
        _logger = logger;
    }

    public async Task<IReadOnlyList<SubscriptionPlanDto>> GetPlansAsync(CancellationToken cancellationToken)
    {
        var products = await ListProductsAsync(cancellationToken);
        return products
            .Where(response => !string.IsNullOrWhiteSpace(response.Product.Handle))
            .Select(response => ToPlan(response.Product))
            .ToArray();
    }

    public async Task<SubscriptionDto> SubscribeAsync(
        ClaimsPrincipal principal,
        string productHandle,
        CancellationToken cancellationToken)
    {
        var identity = GetIdentity(principal);
        var normalizedHandle = productHandle?.Trim() ?? string.Empty;
        if (normalizedHandle.Length == 0)
        {
            throw new MaxioProviderException((int)HttpStatusCode.BadRequest, "A subscription plan is required.");
        }

        var userLock = _userLocks.GetOrAdd(identity, _ => new SemaphoreSlim(1, 1));
        await userLock.WaitAsync(cancellationToken);
        try
        {
            var plans = await ListProductsAsync(cancellationToken);
            var plan = plans
                .Select(response => response.Product)
                .FirstOrDefault(product => string.Equals(product.Handle, normalizedHandle, StringComparison.OrdinalIgnoreCase));

            if (plan is null || string.IsNullOrWhiteSpace(plan.Handle))
            {
                throw new MaxioProviderException((int)HttpStatusCode.BadRequest, "The requested subscription plan is not available.");
            }

            var customerReference = BuildCustomerReference(identity);
            var subscriptionReference = BuildSubscriptionReference(customerReference, plan.Handle);
            _ = await FindOrCreateCustomerAsync(identity, customerReference, cancellationToken);
            var subscription = await FindSubscriptionAsync(subscriptionReference, cancellationToken);

            if (subscription is null)
            {
                subscription = await CreateSubscriptionAsync(
                    customerReference,
                    subscriptionReference,
                    plan.Handle,
                    cancellationToken);
            }

            return ToSubscription(subscription, plan.Handle, plan.Name, plan.PriceInCents);
        }
        finally
        {
            userLock.Release();
        }
    }

    public async Task<IReadOnlyList<SubscriptionDto>> GetMySubscriptionsAsync(
        ClaimsPrincipal principal,
        CancellationToken cancellationToken)
    {
        var identity = GetIdentity(principal);
        var customerReference = BuildCustomerReference(identity);
        var customer = await FindCustomerAsync(customerReference, cancellationToken);
        if (customer is null)
        {
            return Array.Empty<SubscriptionDto>();
        }

        if (!customer.Id.HasValue)
        {
            throw IncompleteProviderResponse("customer");
        }

        var subscriptions = await ListCustomerSubscriptionsAsync(customer.Id.Value, cancellationToken);
        return subscriptions
            .Where(response => response.Subscription is not null)
            .Select(response => ToSubscription(response.Subscription!, null, null, null))
            .ToArray();
    }

    private async Task<IReadOnlyList<ProductResponse>> ListProductsAsync(CancellationToken cancellationToken)
    {
        var products = new List<ProductResponse>();
        for (var page = 1; ; page++)
        {
            var pageProducts = await WithProviderBoundary(
                ct => _client.ProductFamilies.ListProductsForProductFamily(
                    productFamilyId: "handle:" + _settings.ProductFamilyHandle,
                    dateField: null,
                    filter: null,
                    startDate: null,
                    endDate: null,
                    startDatetime: null,
                    endDatetime: null,
                    includeArchived: null,
                    include: null,
                    page: page,
                    perPage: ProductsPageSize,
                    ct: ct),
                "list subscription plans",
                cancellationToken);

            products.AddRange(pageProducts);
            if (pageProducts.Count < ProductsPageSize)
            {
                return products;
            }
        }
    }

    private async Task<Customer?> FindCustomerAsync(string reference, CancellationToken cancellationToken)
    {
        try
        {
            var response = await WithBudget(
                ct => _client.Customers.ReadCustomerByReference(reference, ct: ct), cancellationToken);
            return response.Customer;
        }
        catch (SdkException<RawError> ex) when (ex.Error.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
        catch (SdkException<RawError> ex)
        {
            throw ProviderFailure("read customer", ex.Error.StatusCode, ex.Error.ReadAsString(), ex);
        }
        catch (JsonException ex)
        {
            throw ProviderFailure("read customer", null, null, ex);
        }
        catch (HttpRequestException ex)
        {
            throw ProviderFailure("read customer", null, null, ex);
        }
        catch (TaskCanceledException ex)
        {
            throw ProviderFailure("read customer", HttpStatusCode.GatewayTimeout, null, ex);
        }
    }

    private async Task<Customer> FindOrCreateCustomerAsync(
        string identity,
        string reference,
        CancellationToken cancellationToken)
    {
        var existing = await FindCustomerAsync(reference, cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        var email = identity;
        var name = GetCustomerName(identity);
        var request = new CreateCustomerRequest
        {
            Customer = new CreateCustomer
            {
                FirstName = name.FirstName,
                LastName = name.LastName,
                Email = email,
                Reference = reference
            }
        };

        try
        {
            var response = await WithBudget(
                ct => _client.Customers.CreateCustomer(body: request, ct: ct), cancellationToken);
            if (response.Customer is null || !response.Customer.Id.HasValue)
            {
                throw IncompleteProviderResponse("created customer");
            }

            return response.Customer;
        }
        catch (SdkException<CreateCustomerError> ex)
        {
            if (ex.Error.TryGetCustomerErrorResponse1(out var details))
            {
                _logger.LogWarning("Maxio rejected customer creation for {Reference}: {Details}", reference,
                    details.Errors is null ? string.Empty : string.Join("; ", details.Errors));
                throw new MaxioProviderException((int)HttpStatusCode.UnprocessableEntity, "Maxio rejected the customer details.", ex);
            }

            if (ex.Error.TryGetRawError(out var raw))
            {
                throw ProviderFailure("create customer", raw.StatusCode, raw.ReadAsString(), ex);
            }

            throw ProviderFailure("create customer", null, null, ex);
        }
        catch (JsonException ex)
        {
            throw ProviderFailure("create customer", null, null, ex);
        }
        catch (HttpRequestException ex)
        {
            throw ProviderFailure("create customer", null, null, ex);
        }
        catch (TaskCanceledException ex)
        {
            throw ProviderFailure("create customer", HttpStatusCode.GatewayTimeout, null, ex);
        }
    }

    private async Task<Subscription?> FindSubscriptionAsync(string reference, CancellationToken cancellationToken)
    {
        try
        {
            var response = await WithBudget(
                ct => _client.Subscriptions.FindSubscription(reference: reference, ct: ct), cancellationToken);
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
                throw ProviderFailure("find subscription", raw.StatusCode, raw.ReadAsString(), ex);
            }

            throw ProviderFailure("find subscription", null, null, ex);
        }
        catch (JsonException ex)
        {
            throw ProviderFailure("find subscription", null, null, ex);
        }
        catch (HttpRequestException ex)
        {
            throw ProviderFailure("find subscription", null, null, ex);
        }
        catch (TaskCanceledException ex)
        {
            throw ProviderFailure("find subscription", HttpStatusCode.GatewayTimeout, null, ex);
        }
    }

    private async Task<Subscription> CreateSubscriptionAsync(
        string customerReference,
        string subscriptionReference,
        string productHandle,
        CancellationToken cancellationToken)
    {
        var request = new CreateSubscriptionRequest
        {
            Subscription = new CreateSubscription
            {
                ProductHandle = productHandle,
                CustomerReference = customerReference,
                Reference = subscriptionReference,
                PaymentCollectionMethod = MaxioAdvancedBilling.Models.Enums.CollectionMethod.Invoice
            }
        };

        try
        {
            var response = await WithBudget(
                ct => _client.Subscriptions.CreateSubscription(body: request, ct: ct), cancellationToken);
            if (response.Subscription is null)
            {
                throw IncompleteProviderResponse("created subscription");
            }

            return response.Subscription;
        }
        catch (SdkException<CreateSubscriptionError> ex)
        {
            if (ex.Error.TryGetErrorListResponse1(out var details))
            {
                _logger.LogWarning("Maxio rejected subscription creation for {Reference}: {Details}", subscriptionReference,
                    details.Errors is null ? string.Empty : string.Join("; ", details.Errors));
                throw new MaxioProviderException((int)HttpStatusCode.UnprocessableEntity, "Maxio rejected the subscription details.", ex);
            }

            if (ex.Error.TryGetRawError(out var raw))
            {
                throw ProviderFailure("create subscription", raw.StatusCode, raw.ReadAsString(), ex);
            }

            throw ProviderFailure("create subscription", null, null, ex);
        }
        catch (JsonException ex)
        {
            throw ProviderFailure("create subscription", null, null, ex);
        }
        catch (HttpRequestException ex)
        {
            throw ProviderFailure("create subscription", null, null, ex);
        }
        catch (TaskCanceledException ex)
        {
            throw ProviderFailure("create subscription", HttpStatusCode.GatewayTimeout, null, ex);
        }
    }

    private async Task<IReadOnlyList<SubscriptionResponse>> ListCustomerSubscriptionsAsync(
        int customerId,
        CancellationToken cancellationToken)
    {
        try
        {
            return await WithBudget(
                ct => _client.Customers.ListCustomerSubscriptions(customerId, ct: ct), cancellationToken);
        }
        catch (SdkException<RawError> ex)
        {
            throw ProviderFailure("list customer subscriptions", ex.Error.StatusCode, ex.Error.ReadAsString(), ex);
        }
        catch (JsonException ex)
        {
            throw ProviderFailure("list customer subscriptions", null, null, ex);
        }
        catch (HttpRequestException ex)
        {
            throw ProviderFailure("list customer subscriptions", null, null, ex);
        }
        catch (TaskCanceledException ex)
        {
            throw ProviderFailure("list customer subscriptions", HttpStatusCode.GatewayTimeout, null, ex);
        }
    }

    private async Task<T> WithProviderBoundary<T>(
        Func<CancellationToken, Task<T>> operation,
        string operationName,
        CancellationToken cancellationToken)
    {
        try
        {
            return await WithBudget(operation, cancellationToken);
        }
        catch (SdkException<ListProductsForProductFamilyError> ex)
        {
            if (ex.Error.TryGetString(out var detail))
            {
                _logger.LogWarning("Maxio rejected {OperationName}: {Details}", operationName, detail);
                throw new MaxioProviderException((int)HttpStatusCode.BadGateway, "Maxio could not load subscription plans.", ex);
            }

            if (ex.Error.TryGetRawError(out var raw))
            {
                throw ProviderFailure(operationName, raw.StatusCode, raw.ReadAsString(), ex);
            }

            throw ProviderFailure(operationName, null, null, ex);
        }
        catch (JsonException ex)
        {
            throw ProviderFailure(operationName, null, null, ex);
        }
        catch (HttpRequestException ex)
        {
            throw ProviderFailure(operationName, null, null, ex);
        }
        catch (TaskCanceledException ex)
        {
            throw ProviderFailure(operationName, HttpStatusCode.GatewayTimeout, null, ex);
        }
    }

    private static async Task<T> WithBudget<T>(
        Func<CancellationToken, Task<T>> operation,
        CancellationToken requestCancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(requestCancellationToken);
        timeout.CancelAfter(ProviderCallBudget);
        return await operation(timeout.Token);
    }

    private static SubscriptionPlanDto ToPlan(Product product)
    {
        return new SubscriptionPlanDto
        {
            Id = product.Id,
            Handle = product.Handle ?? string.Empty,
            Name = product.Name ?? string.Empty,
            Description = product.Description,
            PriceInCents = product.PriceInCents,
            Price = ToPrice(product.PriceInCents),
            Interval = product.Interval,
            IntervalUnit = product.IntervalUnit?.Value,
            TrialInterval = product.TrialInterval,
            TrialIntervalUnit = product.TrialIntervalUnit?.Value,
            TrialPriceInCents = product.TrialPriceInCents
        };
    }

    private static SubscriptionDto ToSubscription(
        Subscription subscription,
        string? requestedPlanHandle,
        string? requestedPlanName,
        long? requestedPriceInCents)
    {
        var priceInCents = subscription.ProductPriceInCents ?? subscription.Product?.PriceInCents ?? requestedPriceInCents;
        return new SubscriptionDto
        {
            Id = subscription.Id,
            Reference = subscription.Reference ?? string.Empty,
            PlanHandle = subscription.Product?.Handle ?? requestedPlanHandle ?? string.Empty,
            PlanName = subscription.Product?.Name ?? requestedPlanName ?? string.Empty,
            PriceInCents = priceInCents,
            Price = ToPrice(priceInCents),
            State = subscription.State?.Value ?? string.Empty,
            NextBillingDate = subscription.CurrentPeriodEndsAt ?? subscription.NextAssessmentAt
        };
    }

    private static decimal? ToPrice(long? priceInCents)
    {
        return priceInCents.HasValue ? priceInCents.Value / 100m : null;
    }

    private static string GetIdentity(ClaimsPrincipal principal)
    {
        var identity = principal.Identity?.Name?.Trim();
        if (principal.Identity?.IsAuthenticated != true || string.IsNullOrWhiteSpace(identity))
        {
            throw new MaxioProviderException((int)HttpStatusCode.Unauthorized, "Authentication is required.");
        }

        return identity.ToLowerInvariant();
    }

    private static string BuildCustomerReference(string identity)
    {
        return "eshop-user-" + Hash(identity);
    }

    private static string BuildSubscriptionReference(string customerReference, string productHandle)
    {
        return "eshop-subscription-" + Hash(customerReference + ":" + productHandle.ToLowerInvariant());
    }

    private static string Hash(string value)
    {
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
    }

    private static (string FirstName, string LastName) GetCustomerName(string identity)
    {
        var localPart = identity.Split('@', 2)[0];
        var words = localPart.Split(new[] { '.', '-', '_', ' ' }, StringSplitOptions.RemoveEmptyEntries);
        var firstName = words.FirstOrDefault() ?? "Shopper";
        var lastName = words.Skip(1).FirstOrDefault() ?? "Customer";
        return (firstName, lastName);
    }

    private MaxioProviderException IncompleteProviderResponse(string resource)
    {
        return new MaxioProviderException((int)HttpStatusCode.BadGateway, "Maxio returned an incomplete response.");
    }

    private MaxioProviderException ProviderFailure(
        string operation,
        HttpStatusCode? statusCode,
        string? providerDetail,
        Exception innerException)
    {
        var status = statusCode.HasValue ? (int)statusCode.Value : (int)HttpStatusCode.BadGateway;
        if (status < 400 || status > 599)
        {
            status = (int)HttpStatusCode.BadGateway;
        }

        _logger.LogError(innerException, "Maxio operation {Operation} failed with status {StatusCode}. Provider detail: {ProviderDetail}",
            operation, status, providerDetail);
        return new MaxioProviderException(status, "The subscription billing provider could not complete the request.", innerException);
    }
}
