using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IdentityModel.Tokens.Jwt;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Claims;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.ErrorResponse;
using MaxioAdvancedBilling.Core.Exceptions;
using MaxioAdvancedBilling.Errors;
using MaxioAdvancedBilling.Models;
using MaxioAdvancedBilling.Models.Enums;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.eShopWeb.Infrastructure.Identity;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

public interface IMaxioSubscriptionService
{
    Task<IReadOnlyList<SubscriptionPlanDto>> GetPlansAsync(CancellationToken cancellationToken);
    Task<SubscriptionDto> SubscribeAsync(SubscribeRequest request, CancellationToken cancellationToken);
    Task<IReadOnlyList<SubscriptionDto>> GetMySubscriptionsAsync(CancellationToken cancellationToken);
}

public sealed class MaxioSubscriptionService : IMaxioSubscriptionService
{
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> UserLocks = new(StringComparer.Ordinal);
    private static readonly TimeSpan ProviderCallBudget = TimeSpan.FromSeconds(30);

    private readonly MaxioAdvancedBillingClient _client;
    private readonly MaxioOptions _options;
    private readonly CatalogContext _catalogContext;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly ILogger<MaxioSubscriptionService> _logger;

    public MaxioSubscriptionService(
        MaxioAdvancedBillingClient client,
        IOptions<MaxioOptions> options,
        CatalogContext catalogContext,
        UserManager<ApplicationUser> userManager,
        IHttpContextAccessor httpContextAccessor,
        ILogger<MaxioSubscriptionService> logger)
    {
        _client = client;
        _options = options.Value;
        _catalogContext = catalogContext;
        _userManager = userManager;
        _httpContextAccessor = httpContextAccessor;
        _logger = logger;
    }

    public async Task<IReadOnlyList<SubscriptionPlanDto>> GetPlansAsync(CancellationToken cancellationToken)
    {
        EnsureConfigured();
        using var budget = CreateProviderBudget(cancellationToken);
        IReadOnlyList<ProductFamilyResponse> families;
        try
        {
            families = await _client.ProductFamilies.ListProductFamilies(
                dateField: null,
                startDate: null,
                endDate: null,
                startDatetime: null,
                endDatetime: null,
                ct: budget.Token);
        }
        catch (SdkException<RawError> ex)
        {
            throw FromRaw("listing product families", ex.Error, ex);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            throw FromTransport("listing product families", ex);
        }

        var family = families
            .Select(response => response.ProductFamily)
            .FirstOrDefault(candidate => string.Equals(candidate?.Handle, _options.ProductFamilyHandle, StringComparison.Ordinal));

        if (family?.Id is not int familyId)
        {
            throw new MaxioProviderException("The configured subscription catalog is unavailable.", HttpStatusCode.BadGateway);
        }

        var products = new List<ProductResponse>();
        const int pageSize = 100;
        for (var page = 1; ; page++)
        {
            IReadOnlyList<ProductResponse> pageProducts;
            try
            {
                pageProducts = await _client.ProductFamilies.ListProductsForProductFamily(
                    familyId.ToString(CultureInfo.InvariantCulture),
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
                    ct: budget.Token);
            }
            catch (SdkException<ListProductsForProductFamilyError> ex)
            {
                if (ex.Error.TryGetString(out _))
                {
                    throw new MaxioProviderException("The configured subscription catalog is unavailable.", HttpStatusCode.BadGateway, ex);
                }

                if (ex.Error.TryGetRawError(out var raw))
                {
                    throw FromRaw("listing subscription products", raw, ex);
                }

                throw new MaxioProviderException("The subscription catalog could not be read.", HttpStatusCode.BadGateway, ex);
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
            {
                throw FromTransport("listing subscription products", ex);
            }

            products.AddRange(pageProducts);
            if (pageProducts.Count < pageSize)
            {
                break;
            }
        }

        return products
            .Where(response => response.Product is not null && !string.IsNullOrWhiteSpace(response.Product.Handle))
            .Select(response => ToPlan(response.Product!))
            .ToArray();
    }

    public async Task<SubscriptionDto> SubscribeAsync(SubscribeRequest request, CancellationToken cancellationToken)
    {
        EnsureConfigured();
        if (string.IsNullOrWhiteSpace(request.PlanHandle))
        {
            throw new MaxioProviderException("A subscription plan is required.", HttpStatusCode.BadRequest);
        }

        var user = await GetCurrentUserAsync();
        var lockKey = $"{user.Id}:{request.PlanHandle}";
        var gate = UserLocks.GetOrAdd(lockKey, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            using var budget = CreateProviderBudget(cancellationToken);
            var customer = await GetOrCreateCustomerAsync(user, budget.Token);
            var reference = SubscriptionReference(user.Id, request.PlanHandle);
            var existing = await FindSubscriptionAsync(reference, budget.Token);

            if (existing is not null)
            {
                await SaveSubscriptionMappingAsync(user.Id, request.PlanHandle, customer.Id!.Value, existing.Id!.Value, reference);
                return ToSubscription(existing);
            }

            Subscription subscription;
            try
            {
                var body = new CreateSubscriptionRequest
                {
                    Subscription = new CreateSubscription
                    {
                        CustomerId = customer.Id,
                        ProductHandle = request.PlanHandle,
                        ProductPricePointHandle = request.PricePointHandle,
                        PaymentCollectionMethod = CollectionMethod.Remittance,
                        Reference = reference
                    }
                };

                using (MaxioWriteOnceHandler.BeginScope())
                {
                    var response = await _client.Subscriptions.CreateSubscription(body, ct: budget.Token);
                    subscription = response.Subscription
                        ?? throw new MaxioProviderException("Maxio returned an unusable subscription response.", HttpStatusCode.BadGateway);
                }
            }
            catch (SdkException<CreateSubscriptionError> ex)
            {
                if (ex.Error.TryGetErrorListResponse1(out var validation))
                {
                    _logger.LogWarning(
                        "Maxio rejected subscription enrollment for plan {PlanHandle}: {ValidationErrors}",
                        request.PlanHandle,
                        string.Join("; ", validation.Errors));

                    var reconciled = await FindSubscriptionAsync(reference, budget.Token);
                    if (reconciled is not null)
                    {
                        await SaveSubscriptionMappingAsync(user.Id, request.PlanHandle, customer.Id!.Value, reconciled.Id!.Value, reference);
                        return ToSubscription(reconciled);
                    }

                    throw new MaxioProviderException("Maxio rejected the subscription request.", HttpStatusCode.UnprocessableEntity, ex);
                }

                if (ex.Error.TryGetRawError(out var raw))
                {
                    throw FromRaw("creating a subscription", raw, ex);
                }

                throw new MaxioProviderException("The subscription could not be created.", HttpStatusCode.BadGateway, ex);
            }
            catch (Exception ex) when (ex is MaxioWriteRetryRefusedException or HttpRequestException or TaskCanceledException or JsonException)
            {
                var reconciled = await FindSubscriptionAsync(reference, budget.Token);
                if (reconciled is not null)
                {
                    await SaveSubscriptionMappingAsync(user.Id, request.PlanHandle, customer.Id!.Value, reconciled.Id!.Value, reference);
                    return ToSubscription(reconciled);
                }

                throw FromTransport("creating a subscription", ex);
            }

            if (subscription.Id is not int subscriptionId)
            {
                throw new MaxioProviderException("Maxio returned an unusable subscription response.", HttpStatusCode.BadGateway);
            }

            await SaveSubscriptionMappingAsync(user.Id, request.PlanHandle, customer.Id!.Value, subscriptionId, reference);
            return ToSubscription(subscription);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<IReadOnlyList<SubscriptionDto>> GetMySubscriptionsAsync(CancellationToken cancellationToken)
    {
        EnsureConfigured();
        var user = await GetCurrentUserAsync();
        var customerMapping = await _catalogContext.MaxioCustomerMappings
            .AsNoTracking()
            .SingleOrDefaultAsync(mapping => mapping.ApplicationUserId == user.Id, cancellationToken);

        if (customerMapping is null)
        {
            return Array.Empty<SubscriptionDto>();
        }

        using var budget = CreateProviderBudget(cancellationToken);
        IReadOnlyList<SubscriptionResponse> responses;
        try
        {
            responses = await _client.Customers.ListCustomerSubscriptions(customerMapping.MaxioCustomerId, ct: budget.Token);
        }
        catch (SdkException<RawError> ex)
        {
            throw FromRaw("listing customer subscriptions", ex.Error, ex);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            throw FromTransport("listing customer subscriptions", ex);
        }

        return responses
            .Where(response => response.Subscription is not null)
            .Select(response => ToSubscription(response.Subscription!))
            .ToArray();
    }

    private async Task<ApplicationUser> GetCurrentUserAsync()
    {
        var userName = _httpContextAccessor.HttpContext?.User.FindFirstValue(ClaimTypes.Name);
        if (string.IsNullOrWhiteSpace(userName))
        {
            throw new MaxioProviderException("The authenticated user could not be resolved.", HttpStatusCode.Unauthorized);
        }

        var user = await _userManager.FindByNameAsync(userName);
        return user ?? throw new MaxioProviderException("The authenticated user could not be resolved.", HttpStatusCode.Unauthorized);
    }

    private async Task<Customer> GetOrCreateCustomerAsync(ApplicationUser user, CancellationToken cancellationToken)
    {
        var mapping = await _catalogContext.MaxioCustomerMappings
            .SingleOrDefaultAsync(candidate => candidate.ApplicationUserId == user.Id, cancellationToken);
        if (mapping is not null)
        {
            return new Customer { Id = mapping.MaxioCustomerId, Reference = mapping.MaxioReference, Email = user.Email };
        }

        var reference = CustomerReference(user.Id);
        var existing = await ReadCustomerAsync(reference, cancellationToken);
        if (existing is not null)
        {
            await SaveCustomerMappingAsync(user.Id, existing.Id!.Value, reference, cancellationToken);
            return existing;
        }

        Customer customer;
        try
        {
            var body = new CreateCustomerRequest
            {
                Customer = new CreateCustomer
                {
                    FirstName = "eShopOnWeb",
                    LastName = user.UserName ?? user.Id,
                    Email = user.Email ?? user.UserName ?? $"{user.Id}@invalid.local",
                    Reference = reference
                }
            };

            using (MaxioWriteOnceHandler.BeginScope())
            {
                var response = await _client.Customers.CreateCustomer(body, ct: cancellationToken);
                customer = response.Customer;
            }
        }
        catch (SdkException<CreateCustomerError> ex)
        {
            if (ex.Error.TryGetCustomerErrorResponse1(out _))
            {
                var racedCustomer = await ReadCustomerAsync(reference, cancellationToken);
                if (racedCustomer is not null)
                {
                    await SaveCustomerMappingAsync(user.Id, racedCustomer.Id!.Value, reference, cancellationToken);
                    return racedCustomer;
                }

                throw new MaxioProviderException("Maxio rejected the customer request.", HttpStatusCode.UnprocessableEntity, ex);
            }

            if (ex.Error.TryGetRawError(out var raw))
            {
                throw FromRaw("creating a customer", raw, ex);
            }

            throw new MaxioProviderException("The Maxio customer could not be created.", HttpStatusCode.BadGateway, ex);
        }
        catch (Exception ex) when (ex is MaxioWriteRetryRefusedException or HttpRequestException or TaskCanceledException or JsonException)
        {
            var racedCustomer = await ReadCustomerAsync(reference, cancellationToken);
            if (racedCustomer is not null)
            {
                await SaveCustomerMappingAsync(user.Id, racedCustomer.Id!.Value, reference, cancellationToken);
                return racedCustomer;
            }

            throw FromTransport("creating a customer", ex);
        }

        if (customer.Id is not int customerId)
        {
            throw new MaxioProviderException("Maxio returned an unusable customer response.", HttpStatusCode.BadGateway);
        }

        await SaveCustomerMappingAsync(user.Id, customerId, reference, cancellationToken);
        return customer;
    }

    private async Task<Customer?> ReadCustomerAsync(string reference, CancellationToken cancellationToken)
    {
        try
        {
            var response = await _client.Customers.ReadCustomerByReference(reference, ct: cancellationToken);
            return response.Customer;
        }
        catch (SdkException<RawError> ex) when (ex.Error.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
        catch (SdkException<RawError> ex)
        {
            throw FromRaw("reading a customer", ex.Error, ex);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            throw FromTransport("reading a customer", ex);
        }
    }

    private async Task<Subscription?> FindSubscriptionAsync(string reference, CancellationToken cancellationToken)
    {
        try
        {
            var response = await _client.Subscriptions.FindSubscription(reference, ct: cancellationToken);
            return response.Subscription;
        }
        catch (SdkException<FindSubscriptionError> ex)
        {
            if (ex.Error.TryGetNoContent(out var notFound) && notFound.StatusCode == HttpStatusCode.NotFound)
            {
                return null;
            }

            if (ex.Error.TryGetRawError(out var raw))
            {
                if (raw.StatusCode == HttpStatusCode.NotFound)
                {
                    return null;
                }

                throw FromRaw("finding a subscription", raw, ex);
            }

            throw new MaxioProviderException("The subscription could not be looked up.", HttpStatusCode.BadGateway, ex);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            throw FromTransport("finding a subscription", ex);
        }
    }

    private async Task SaveCustomerMappingAsync(string userId, int customerId, string reference, CancellationToken cancellationToken)
    {
        var current = await _catalogContext.MaxioCustomerMappings
            .SingleOrDefaultAsync(mapping => mapping.ApplicationUserId == userId, cancellationToken);
        if (current is null)
        {
            _catalogContext.MaxioCustomerMappings.Add(new MaxioCustomerMapping(userId, customerId, reference));
            try
            {
                await _catalogContext.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateException)
            {
                _catalogContext.Entry(_catalogContext.MaxioCustomerMappings.Local.Last()).State = EntityState.Detached;
                if (!await _catalogContext.MaxioCustomerMappings.AnyAsync(mapping => mapping.ApplicationUserId == userId, cancellationToken))
                {
                    throw new MaxioProviderException("The local subscription record could not be saved.", HttpStatusCode.ServiceUnavailable);
                }
            }
        }
    }

    private async Task SaveSubscriptionMappingAsync(string userId, string planHandle, int customerId, int subscriptionId, string reference)
    {
        var current = await _catalogContext.MaxioSubscriptionMappings
            .SingleOrDefaultAsync(mapping => mapping.ApplicationUserId == userId && mapping.PlanHandle == planHandle);
        if (current is null)
        {
            _catalogContext.MaxioSubscriptionMappings.Add(new MaxioSubscriptionMapping(userId, planHandle, customerId, subscriptionId, reference));
        }
        else
        {
            current.ReplaceSubscription(subscriptionId);
        }

        try
        {
            await _catalogContext.SaveChangesAsync();
        }
        catch (DbUpdateException)
        {
            _logger.LogWarning("A concurrent subscription mapping was saved for user {UserId} and plan {PlanHandle}.", userId, planHandle);
            _catalogContext.ChangeTracker.Clear();
        }
    }

    private static SubscriptionPlanDto ToPlan(Product product) => new()
    {
        Handle = product.Handle ?? string.Empty,
        Name = product.Name ?? product.Handle ?? string.Empty,
        Price = product.PriceInCents.HasValue ? product.PriceInCents.Value / 100m : null,
        Interval = product.Interval,
        IntervalUnit = product.IntervalUnit?.Value,
        PricePointHandle = product.ProductPricePointHandle,
        PricePointId = product.ProductPricePointId
    };

    private static SubscriptionDto ToSubscription(Subscription subscription) => new()
    {
        Id = subscription.Id,
        PlanHandle = subscription.Product?.Handle ?? string.Empty,
        PlanName = subscription.Product?.Name,
        Price = subscription.ProductPriceInCents.HasValue ? subscription.ProductPriceInCents.Value / 100m : null,
        State = subscription.State?.Value,
        NextBillingDate = subscription.CurrentPeriodEndsAt
    };

    private static string CustomerReference(string userId) => $"eshop-user:{userId}";
    private static string SubscriptionReference(string userId, string planHandle) => $"eshop-subscription:{userId}:{planHandle}";

    private static CancellationTokenSource CreateProviderBudget(CancellationToken requestToken)
    {
        var source = CancellationTokenSource.CreateLinkedTokenSource(requestToken);
        source.CancelAfter(ProviderCallBudget);
        return source;
    }

    private static MaxioProviderException FromRaw(string operation, RawError raw, Exception inner) =>
        new($"Maxio could not complete the request while {operation}.",
            IsClientStatus(raw.StatusCode) ? raw.StatusCode : HttpStatusCode.BadGateway,
            inner);

    private static MaxioProviderException FromTransport(string operation, Exception inner) =>
        new($"Maxio could not be reached while {operation}.", HttpStatusCode.BadGateway, inner);

    private static bool IsClientStatus(HttpStatusCode statusCode) =>
        (int)statusCode >= 400 && (int)statusCode < 500;

    private void EnsureConfigured()
    {
        if (string.IsNullOrWhiteSpace(_options.ApiKey) ||
            string.IsNullOrWhiteSpace(_options.Subdomain) ||
            string.IsNullOrWhiteSpace(_options.ProductFamilyHandle))
        {
            throw new MaxioProviderException("Subscription billing is not configured.", HttpStatusCode.ServiceUnavailable);
        }
    }
}
