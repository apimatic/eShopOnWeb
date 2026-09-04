using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IdentityModel.Tokens.Jwt;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.ErrorResponse;
using MaxioAdvancedBilling.Core.Exceptions;
using MaxioAdvancedBilling.Errors;
using MaxioAdvancedBilling.Models;
using MaxioAdvancedBilling.Models.Enums;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

public sealed class MaxioSubscriptionBillingService : ISubscriptionBillingService
{
    private const int ProductsPageSize = 100;
    private static readonly TimeSpan CallBudget = TimeSpan.FromSeconds(30);
    private readonly MaxioAdvancedBillingClient _client;
    private readonly MaxioOptions _options;
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _enrollmentLocks;
    private readonly ILogger<MaxioSubscriptionBillingService> _logger;

    public MaxioSubscriptionBillingService(
        MaxioAdvancedBillingClient client,
        IOptions<MaxioOptions> options,
        ConcurrentDictionary<string, SemaphoreSlim> enrollmentLocks,
        ILogger<MaxioSubscriptionBillingService> logger)
    {
        _client = client;
        _options = options.Value;
        _enrollmentLocks = enrollmentLocks;
        _logger = logger;
    }

    public async Task<IReadOnlyList<SubscriptionPlanResponse>> GetPlansAsync(CancellationToken cancellationToken)
    {
        EnsureConfigured();
        var family = await FindProductFamilyAsync(cancellationToken);
        if (family.Id is null)
            throw new SubscriptionBillingException("The configured Maxio product family has no id.", HttpStatusCode.BadGateway);

        var products = await ListProductsAsync(family.Id.Value.ToString(), cancellationToken);
        return products
            .Where(x => x.Product is not null && !string.IsNullOrWhiteSpace(x.Product.Handle))
            .Select(x => new SubscriptionPlanResponse
            {
                PlanHandle = x.Product.Handle!,
                PlanName = x.Product.Name ?? x.Product.Handle!,
                PriceInCents = x.Product.PriceInCents,
                Interval = x.Product.Interval,
                IntervalUnit = x.Product.IntervalUnit?.Value
            })
            .ToArray();
    }

    public async Task<SubscriptionResponse> SubscribeAsync(
        ClaimsPrincipal principal,
        string planHandle,
        CancellationToken cancellationToken)
    {
        EnsureConfigured();
        if (string.IsNullOrWhiteSpace(planHandle))
            throw new SubscriptionBillingException("PlanHandle is required.", HttpStatusCode.BadRequest);

        var identity = GetIdentity(principal);
        var normalizedHandle = planHandle.Trim();
        var family = await FindProductFamilyAsync(cancellationToken);
        if (family.Id is null)
            throw new SubscriptionBillingException("The configured Maxio product family has no id.", HttpStatusCode.BadGateway);

        var product = (await ListProductsAsync(family.Id.Value.ToString(), cancellationToken))
            .Select(x => x.Product)
            .FirstOrDefault(x => x is not null && string.Equals(x.Handle, normalizedHandle, StringComparison.Ordinal));
        if (product is null || string.IsNullOrWhiteSpace(product.Handle))
            throw new SubscriptionBillingException("The requested subscription plan was not found.", HttpStatusCode.NotFound);

        var subscriptionReference = BuildSubscriptionReference(identity.CustomerReference, product.Handle);
        var gate = _enrollmentLocks.GetOrAdd(subscriptionReference, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            var customer = await GetOrCreateCustomerAsync(identity, cancellationToken);
            var existing = await FindSubscriptionAsync(subscriptionReference, cancellationToken);
            var subscription = existing ?? await CreateSubscriptionWithReconciliationAsync(
                customer.Reference!, product.Handle, subscriptionReference, cancellationToken);
            return MapSubscription(subscription);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<IReadOnlyList<SubscriptionResponse>> GetMySubscriptionsAsync(
        ClaimsPrincipal principal,
        CancellationToken cancellationToken)
    {
        EnsureConfigured();
        var identity = GetIdentity(principal);
        var customer = await TryReadCustomerAsync(identity.CustomerReference, cancellationToken);
        if (customer is null || customer.Id is null)
            return Array.Empty<SubscriptionResponse>();

        try
        {
            var subscriptions = await Bounded(ct => _client.Customers.ListCustomerSubscriptions(customer.Id.Value, ct), cancellationToken);
            return subscriptions
                .Where(x => x.Subscription is not null)
                .Select(x => MapSubscription(x.Subscription!))
                .ToArray();
        }
        catch (SdkException<RawError> ex)
        {
            throw ProviderFailure("Unable to read Maxio subscriptions.", ex.Error.StatusCode, ex.Error.ReadAsString());
        }
        catch (HttpRequestException ex)
        {
            throw Unavailable("Maxio could not be reached.", ex);
        }
        catch (TaskCanceledException ex)
        {
            throw Unavailable("The Maxio request timed out.", ex);
        }
    }

    private async Task<ProductFamily> FindProductFamilyAsync(CancellationToken cancellationToken)
    {
        try
        {
            var families = await Bounded(ct => _client.ProductFamilies.ListProductFamilies(
                dateField: null,
                startDate: null,
                endDate: null,
                startDatetime: null,
                endDatetime: null,
                ct: ct), cancellationToken);
            var matches = families
                .Where(x => x.ProductFamily is not null &&
                            string.Equals(x.ProductFamily.Handle, _options.ProductFamilyHandle, StringComparison.Ordinal))
                .Select(x => x.ProductFamily!)
                .ToArray();
            if (matches.Length != 1)
                throw new SubscriptionBillingException("The configured Maxio product family was not found.", HttpStatusCode.NotFound);
            return matches[0];
        }
        catch (SdkException<RawError> ex)
        {
            throw ProviderFailure("Unable to read Maxio product families.", ex.Error.StatusCode, ex.Error.ReadAsString());
        }
        catch (HttpRequestException ex)
        {
            throw Unavailable("Maxio could not be reached.", ex);
        }
        catch (TaskCanceledException ex)
        {
            throw Unavailable("The Maxio request timed out.", ex);
        }
    }

    private async Task<IReadOnlyList<ProductResponse>> ListProductsAsync(
        string productFamilyId,
        CancellationToken cancellationToken)
    {
        var allProducts = new List<ProductResponse>();
        var page = 1;
        while (true)
        {
            try
            {
                var pageItems = await Bounded(ct => _client.ProductFamilies.ListProductsForProductFamily(
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
                    perPage: ProductsPageSize,
                    ct: ct), cancellationToken);
                allProducts.AddRange(pageItems);
                if (pageItems.Count < ProductsPageSize)
                    return allProducts;
                page++;
            }
            catch (SdkException<ListProductsForProductFamilyError> ex)
            {
                if (ex.Error.TryGetString(out var message))
                    throw ProviderFailure("Unable to read Maxio products.", HttpStatusCode.NotFound, message);
                if (ex.Error.TryGetRawError(out var raw))
                    throw ProviderFailure("Unable to read Maxio products.", raw.StatusCode, raw.ReadAsString());
                throw ProviderFailure("Unable to read Maxio products.", HttpStatusCode.BadGateway);
            }
            catch (HttpRequestException ex)
            {
                throw Unavailable("Maxio could not be reached.", ex);
            }
            catch (TaskCanceledException ex)
            {
                throw Unavailable("The Maxio request timed out.", ex);
            }
        }
    }

    private async Task<Customer> GetOrCreateCustomerAsync(CustomerIdentity identity, CancellationToken cancellationToken)
    {
        var existing = await TryReadCustomerAsync(identity.CustomerReference, cancellationToken);
        if (existing is not null)
            return existing;

        var body = new CreateCustomerRequest
        {
            Customer = new CreateCustomer
            {
                FirstName = identity.FirstName,
                LastName = identity.LastName,
                Email = identity.Email,
                Reference = identity.CustomerReference
            }
        };
        try
        {
            var response = await Bounded(ct => _client.Customers.CreateCustomer(body, ct: ct), cancellationToken);
            return response.Customer;
        }
        catch (SdkException<CreateCustomerError> ex)
        {
            if (ex.Error.TryGetCustomerErrorResponse1(out var validation))
            {
                var reconciled = await TryReadCustomerAsync(identity.CustomerReference, cancellationToken);
                if (reconciled is not null)
                    return reconciled;
                throw ProviderFailure("Maxio rejected customer enrollment.", HttpStatusCode.UnprocessableEntity,
                    validation.Errors?.ToString());
            }
            if (ex.Error.TryGetRawError(out var raw))
                throw ProviderFailure("Unable to create the Maxio customer.", raw.StatusCode, raw.ReadAsString());
            throw ProviderFailure("Unable to create the Maxio customer.", HttpStatusCode.BadGateway);
        }
        catch (HttpRequestException ex)
        {
            var reconciled = await TryReadCustomerAsync(identity.CustomerReference, cancellationToken);
            if (reconciled is not null)
                return reconciled;
            throw Unavailable("Maxio could not be reached.", ex);
        }
        catch (TaskCanceledException ex)
        {
            var reconciled = await TryReadCustomerAsync(identity.CustomerReference, cancellationToken);
            if (reconciled is not null)
                return reconciled;
            throw Unavailable("The Maxio request timed out.", ex);
        }
    }

    private async Task<Subscription> CreateSubscriptionWithReconciliationAsync(
        string customerReference,
        string productHandle,
        string subscriptionReference,
        CancellationToken cancellationToken)
    {
        var body = new CreateSubscriptionRequest
        {
            Subscription = new CreateSubscription
            {
                ProductHandle = productHandle,
                CustomerReference = customerReference,
                Reference = subscriptionReference,
                PaymentCollectionMethod = CollectionMethod.Remittance
            }
        };
        try
        {
            var response = await Bounded(ct => _client.Subscriptions.CreateSubscription(body, ct: ct), cancellationToken);
            return response.Subscription ?? throw new SubscriptionBillingException(
                "Maxio returned an empty subscription.", HttpStatusCode.BadGateway);
        }
        catch (SdkException<CreateSubscriptionError> ex)
        {
            if (ex.Error.TryGetErrorListResponse1(out var validation))
            {
                var reconciled = await FindSubscriptionAsync(subscriptionReference, cancellationToken);
                if (reconciled is not null)
                    return reconciled;
                throw ProviderFailure("Maxio rejected subscription enrollment.", HttpStatusCode.UnprocessableEntity,
                    validation.Errors is null ? null : string.Join("; ", validation.Errors));
            }
            if (ex.Error.TryGetRawError(out var raw))
                throw ProviderFailure("Unable to create the Maxio subscription.", raw.StatusCode, raw.ReadAsString());
            throw ProviderFailure("Unable to create the Maxio subscription.", HttpStatusCode.BadGateway);
        }
        catch (HttpRequestException ex)
        {
            var reconciled = await FindSubscriptionAsync(subscriptionReference, cancellationToken);
            if (reconciled is not null)
                return reconciled;
            throw Unavailable("Maxio could not be reached.", ex);
        }
        catch (TaskCanceledException ex)
        {
            var reconciled = await FindSubscriptionAsync(subscriptionReference, cancellationToken);
            if (reconciled is not null)
                return reconciled;
            throw Unavailable("The Maxio request timed out.", ex);
        }
    }

    private async Task<Customer?> TryReadCustomerAsync(string reference, CancellationToken cancellationToken)
    {
        try
        {
            var response = await Bounded(ct => _client.Customers.ReadCustomerByReference(reference, ct: ct), cancellationToken);
            return response.Customer;
        }
        catch (SdkException<RawError> ex) when (ex.Error.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
        catch (SdkException<RawError> ex)
        {
            throw ProviderFailure("Unable to read the Maxio customer.", ex.Error.StatusCode, ex.Error.ReadAsString());
        }
        catch (HttpRequestException ex)
        {
            throw Unavailable("Maxio could not be reached.", ex);
        }
        catch (TaskCanceledException ex)
        {
            throw Unavailable("The Maxio request timed out.", ex);
        }
    }

    private async Task<Subscription?> FindSubscriptionAsync(string reference, CancellationToken cancellationToken)
    {
        try
        {
            var response = await Bounded(ct => _client.Subscriptions.FindSubscription(reference: reference, ct: ct), cancellationToken);
            return response.Subscription;
        }
        catch (SdkException<FindSubscriptionError> ex)
        {
            if (ex.Error.TryGetNoContent(out _))
                return null;
            if (ex.Error.TryGetRawError(out var raw))
                throw ProviderFailure("Unable to read the Maxio subscription.", raw.StatusCode, raw.ReadAsString());
            throw ProviderFailure("Unable to read the Maxio subscription.", HttpStatusCode.BadGateway);
        }
        catch (HttpRequestException ex)
        {
            throw Unavailable("Maxio could not be reached.", ex);
        }
        catch (TaskCanceledException ex)
        {
            throw Unavailable("The Maxio request timed out.", ex);
        }
    }

    private async Task<T> Bounded<T>(Func<CancellationToken, Task<T>> operation, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(CallBudget);
        return await operation(timeout.Token);
    }

    private void EnsureConfigured()
    {
        if (string.IsNullOrWhiteSpace(_options.ApiKey) ||
            string.IsNullOrWhiteSpace(_options.Subdomain) ||
            string.IsNullOrWhiteSpace(_options.ProductFamilyHandle))
        {
            throw new SubscriptionBillingException(
                "Maxio billing is not configured.", HttpStatusCode.ServiceUnavailable, providerUnavailable: true);
        }
    }

    private static CustomerIdentity GetIdentity(ClaimsPrincipal principal)
    {
        var stableId = principal.FindFirstValue(ClaimTypes.NameIdentifier) ??
                       principal.FindFirstValue(JwtRegisteredClaimNames.Sub) ??
                       principal.Identity?.Name;
        if (string.IsNullOrWhiteSpace(stableId))
            throw new SubscriptionBillingException("The authenticated user has no stable identity.", HttpStatusCode.Unauthorized);

        var email = principal.FindFirstValue(ClaimTypes.Email) ?? stableId;
        var displayName = principal.FindFirstValue(ClaimTypes.Name) ?? stableId;
        var givenName = principal.FindFirstValue(ClaimTypes.GivenName);
        var familyName = principal.FindFirstValue(ClaimTypes.Surname);
        if (string.IsNullOrWhiteSpace(givenName) || string.IsNullOrWhiteSpace(familyName))
        {
            var parts = Regex.Split(displayName.Split('@')[0], "[^A-Za-z0-9]+")
                .Where(x => x.Length > 0)
                .ToArray();
            givenName ??= parts.FirstOrDefault() ?? "eShop";
            familyName ??= parts.Skip(1).FirstOrDefault() ?? "Customer";
        }

        return new CustomerIdentity(
            BuildUserReference(stableId),
            email,
            givenName,
            familyName);
    }

    private static string BuildUserReference(string stableId) =>
        "eshop-user-" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(stableId))).ToLowerInvariant();

    private static string BuildSubscriptionReference(string customerReference, string planHandle) =>
        "eshop-subscription-" + Convert.ToHexString(SHA256.HashData(
            Encoding.UTF8.GetBytes(customerReference + "\n" + planHandle))).ToLowerInvariant();

    private static SubscriptionResponse MapSubscription(Subscription subscription)
    {
        var product = subscription.Product;
        return new SubscriptionResponse
        {
            PlanHandle = product?.Handle ?? string.Empty,
            PlanName = product?.Name ?? product?.Handle ?? string.Empty,
            PriceInCents = subscription.ProductPriceInCents ?? product?.PriceInCents,
            Currency = subscription.Currency,
            State = subscription.State?.Value,
            NextBillingDate = subscription.CurrentPeriodEndsAt,
            Reference = subscription.Reference ?? string.Empty
        };
    }

    private SubscriptionBillingException ProviderFailure(string message, HttpStatusCode statusCode, string? detail = null)
    {
        _logger.LogWarning("Maxio provider error: {Message}; status {StatusCode}; detail {Detail}",
            message,
            (int)statusCode,
            detail);
        return new SubscriptionBillingException(message, HttpStatusCode.BadGateway);
    }

    private static SubscriptionBillingException Unavailable(string message, Exception exception) =>
        new(message, HttpStatusCode.ServiceUnavailable, providerUnavailable: true, exception);

    private sealed record CustomerIdentity(string CustomerReference, string Email, string FirstName, string LastName);
}
