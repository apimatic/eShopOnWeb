using System;
using System.Collections.Concurrent;
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
using MaxioAdvancedBilling.Models;
using MaxioAdvancedBilling.Models.Enums;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using CreateCustomerSdkException = MaxioAdvancedBilling.Core.Exceptions.SdkException<MaxioAdvancedBilling.Errors.CreateCustomerError>;
using CreateSubscriptionSdkException = MaxioAdvancedBilling.Core.Exceptions.SdkException<MaxioAdvancedBilling.Errors.CreateSubscriptionError>;
using FindSubscriptionSdkException = MaxioAdvancedBilling.Core.Exceptions.SdkException<MaxioAdvancedBilling.Errors.FindSubscriptionError>;
using ListProductsSdkException = MaxioAdvancedBilling.Core.Exceptions.SdkException<MaxioAdvancedBilling.Errors.ListProductsForProductFamilyError>;
using RawSdkException = MaxioAdvancedBilling.Core.Exceptions.SdkException<MaxioAdvancedBilling.Core.ErrorResponse.RawError>;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

public sealed class MaxioSubscriptionService : IMaxioSubscriptionService
{
    private const int ProductsPageSize = 100;
    private static readonly TimeSpan OperationTimeout = TimeSpan.FromSeconds(30);
    private readonly MaxioAdvancedBillingClient _client;
    private readonly MaxioOptions _options;
    private readonly ILogger<MaxioSubscriptionService> _logger;
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _userLocks = new(StringComparer.Ordinal);

    public MaxioSubscriptionService(
        MaxioAdvancedBillingClient client,
        IOptions<MaxioOptions> options,
        ILogger<MaxioSubscriptionService> logger)
    {
        _client = client;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<IReadOnlyList<SubscriptionPlan>> GetPlansAsync(CancellationToken cancellationToken)
    {
        EnsureConfiguration();

        var family = await FindConfiguredFamilyAsync(cancellationToken);
        if (family?.Id is not int familyId)
        {
            throw Failure("The configured Maxio product family could not be found.", HttpStatusCode.BadGateway);
        }

        var plans = new List<SubscriptionPlan>();
        var page = 1;
        while (true)
        {
            var products = await ListProductsAsync(familyId, page, cancellationToken);
            foreach (var productResponse in products)
            {
                var product = productResponse.Product;
                if (!string.Equals(product.ProductFamily?.Handle, _options.ProductFamilyHandle, StringComparison.Ordinal)
                    || string.IsNullOrWhiteSpace(product.Handle))
                {
                    continue;
                }

                plans.Add(new SubscriptionPlan(
                    product.Handle,
                    product.Name ?? product.Handle,
                    product.PriceInCents));
            }

            if (products.Count < ProductsPageSize)
            {
                break;
            }

            page++;
        }

        return plans;
    }

    public async Task<SubscriptionSummary> SubscribeAsync(
        string userIdentity,
        string planHandle,
        CancellationToken cancellationToken)
    {
        EnsureConfiguration();
        var normalizedIdentity = NormalizeIdentity(userIdentity);
        var normalizedPlanHandle = planHandle.Trim();
        if (string.IsNullOrWhiteSpace(normalizedPlanHandle))
        {
            throw Failure("A plan handle is required.", HttpStatusCode.BadRequest);
        }

        var userLock = _userLocks.GetOrAdd(normalizedIdentity, static _ => new SemaphoreSlim(1, 1));
        await userLock.WaitAsync(cancellationToken);
        try
        {
            var product = await ReadAndValidateProductAsync(normalizedPlanHandle, cancellationToken);
            var subscriptionReference = BuildSubscriptionReference(normalizedIdentity, normalizedPlanHandle);
            await GetOrCreateCustomerAsync(normalizedIdentity, cancellationToken);

            var existing = await FindSubscriptionAsync(subscriptionReference, cancellationToken);
            if (existing is not null)
            {
                return ToSummary(existing, product);
            }

            try
            {
                Subscription created;
                using (MaxioWriteGuardHandler.BeginScope())
                {
                    var body = new CreateSubscriptionRequest
                    {
                        Subscription = new CreateSubscription
                        {
                            ProductHandle = normalizedPlanHandle,
                            CustomerReference = BuildCustomerReference(normalizedIdentity),
                            Reference = subscriptionReference,
                            PaymentCollectionMethod = CollectionMethod.Invoice
                        }
                    };

                    var response = await BoundedAsync(ct => _client.Subscriptions.CreateSubscription(body, ct: ct), cancellationToken);
                    created = response.Subscription
                        ?? throw Failure("Maxio returned an incomplete subscription response.", HttpStatusCode.BadGateway);
                }

                _logger.LogInformation("Created Maxio subscription for configured user reference {UserReference} and plan {PlanHandle}.",
                    BuildCustomerReference(normalizedIdentity), normalizedPlanHandle);
                return ToSummary(created, product);
            }
            catch (CreateSubscriptionSdkException ex)
            {
                if (ex.Error.TryGetErrorListResponse1(out _))
                {
                    throw Failure("Maxio rejected the subscription request.", HttpStatusCode.UnprocessableEntity, ex);
                }

                if (ex.Error.TryGetRawError(out var raw))
                {
                    throw ProviderFailure("creating the Maxio subscription", raw.StatusCode, ex);
                }

                throw ProviderFailure("creating the Maxio subscription", null, ex);
            }
            catch (Exception createException) when (createException is not OperationCanceledException)
            {
                // A transport failure or a blocked SDK retry has an unknown outcome. Reconcile
                // by the deterministic reference before allowing the caller to retry.
                var reconciled = await FindSubscriptionAsync(subscriptionReference, cancellationToken);
                if (reconciled is not null)
                {
                    return ToSummary(reconciled, product);
                }

                if (createException is MaxioWriteResendException)
                {
                    throw ProviderFailure("creating the Maxio subscription", null, createException);
                }

                throw;
            }
        }
        finally
        {
            userLock.Release();
        }
    }

    public async Task<IReadOnlyList<SubscriptionSummary>> GetMySubscriptionsAsync(
        string userIdentity,
        CancellationToken cancellationToken)
    {
        EnsureConfiguration();
        var customer = await FindCustomerAsync(BuildCustomerReference(NormalizeIdentity(userIdentity)), cancellationToken);
        if (customer?.Id is not int customerId)
        {
            return Array.Empty<SubscriptionSummary>();
        }

        var subscriptions = await ListCustomerSubscriptionsAsync(customerId, cancellationToken);
        return subscriptions
            .Where(item => item.Subscription is not null)
            .Select(item => ToSummary(item.Subscription!, null))
            .ToArray();
    }

    private async Task<ProductFamily?> FindConfiguredFamilyAsync(CancellationToken cancellationToken)
    {
        try
        {
            var families = await BoundedAsync(ct => _client.ProductFamilies.ListProductFamilies(
                dateField: null,
                startDate: null,
                endDate: null,
                startDatetime: null,
                endDatetime: null,
                ct: ct), cancellationToken);

            return families
                .Select(wrapper => wrapper.ProductFamily)
                .FirstOrDefault(family => string.Equals(
                    family?.Handle,
                    _options.ProductFamilyHandle,
                    StringComparison.Ordinal));
        }
        catch (RawSdkException ex)
        {
            throw ProviderFailure("listing product families", ex.Error.StatusCode, ex);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            throw ProviderFailure("listing product families", null, ex);
        }
    }

    private async Task<IReadOnlyList<ProductResponse>> ListProductsAsync(
        int familyId,
        int page,
        CancellationToken cancellationToken)
    {
        try
        {
            return await BoundedAsync(ct => _client.ProductFamilies.ListProductsForProductFamily(
                productFamilyId: familyId.ToString(),
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
        }
        catch (ListProductsSdkException ex)
        {
            if (ex.Error.TryGetString(out _))
            {
                throw ProviderFailure("listing products", HttpStatusCode.NotFound, ex);
            }

            if (ex.Error.TryGetRawError(out var raw))
            {
                throw ProviderFailure("listing products", raw.StatusCode, ex);
            }

            throw ProviderFailure("listing products", null, ex);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            throw ProviderFailure("listing products", null, ex);
        }
    }

    private async Task<Product> ReadAndValidateProductAsync(string planHandle, CancellationToken cancellationToken)
    {
        try
        {
            var response = await BoundedAsync(ct => _client.Products.ReadProductByHandle(planHandle, ct: ct), cancellationToken);
            var product = response.Product;
            if (!string.Equals(product.ProductFamily?.Handle, _options.ProductFamilyHandle, StringComparison.Ordinal))
            {
                throw Failure("The selected plan is not available in the configured product family.", HttpStatusCode.BadRequest);
            }

            return product;
        }
        catch (MaxioSubscriptionException)
        {
            throw;
        }
        catch (RawSdkException ex) when (ex.Error.StatusCode == HttpStatusCode.NotFound)
        {
            throw Failure("The selected subscription plan was not found.", HttpStatusCode.BadRequest, ex);
        }
        catch (RawSdkException ex)
        {
            throw ProviderFailure("reading the selected product", ex.Error.StatusCode, ex);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            throw ProviderFailure("reading the selected product", null, ex);
        }
    }

    private async Task<Customer?> FindCustomerAsync(string reference, CancellationToken cancellationToken)
    {
        try
        {
            var response = await BoundedAsync(ct => _client.Customers.ReadCustomerByReference(reference, ct: ct), cancellationToken);
            return response.Customer;
        }
        catch (RawSdkException ex) when (ex.Error.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
        catch (RawSdkException ex)
        {
            throw ProviderFailure("reading the Maxio customer", ex.Error.StatusCode, ex);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            throw ProviderFailure("reading the Maxio customer", null, ex);
        }
    }

    private async Task<Customer> GetOrCreateCustomerAsync(string normalizedIdentity, CancellationToken cancellationToken)
    {
        var reference = BuildCustomerReference(normalizedIdentity);
        var existing = await FindCustomerAsync(reference, cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        try
        {
            return await CreateCustomerCoreAsync(reference, normalizedIdentity, cancellationToken);
        }
        catch (Exception createException) when (!cancellationToken.IsCancellationRequested)
        {
            var reconciled = await FindCustomerAsync(reference, cancellationToken);
            if (reconciled is not null)
            {
                return reconciled;
            }

            if (createException is MaxioWriteResendException)
            {
                throw ProviderFailure("creating the Maxio customer", null, createException);
            }

            throw;
        }
    }

    private async Task<Customer> CreateCustomerCoreAsync(
        string reference,
        string normalizedIdentity,
        CancellationToken cancellationToken)
    {
        var body = new CreateCustomerRequest
        {
            Customer = new CreateCustomer
            {
                FirstName = "eShopOnWeb",
                LastName = "Subscriber",
                Email = normalizedIdentity,
                Reference = reference
            }
        };

        try
        {
            using (MaxioWriteGuardHandler.BeginScope())
            {
                var response = await BoundedAsync(ct => _client.Customers.CreateCustomer(body, ct: ct), cancellationToken);
                return response.Customer;
            }
        }
        catch (CreateCustomerSdkException ex)
        {
            if (ex.Error.TryGetCustomerErrorResponse1(out _))
            {
                throw Failure("Maxio rejected the customer details.", HttpStatusCode.UnprocessableEntity, ex);
            }

            if (ex.Error.TryGetRawError(out var raw))
            {
                throw ProviderFailure("creating the Maxio customer", raw.StatusCode, ex);
            }

            throw ProviderFailure("creating the Maxio customer", null, ex);
        }
        catch (MaxioSubscriptionException)
        {
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            throw ProviderFailure("creating the Maxio customer", null, ex);
        }
        catch (MaxioWriteResendException)
        {
            throw;
        }
    }

    private async Task<Subscription?> FindSubscriptionAsync(string reference, CancellationToken cancellationToken)
    {
        try
        {
            var response = await BoundedAsync(ct => _client.Subscriptions.FindSubscription(reference, ct: ct), cancellationToken);
            return response.Subscription
                ?? throw Failure("Maxio returned an incomplete subscription lookup response.", HttpStatusCode.BadGateway);
        }
        catch (FindSubscriptionSdkException ex)
        {
            if (ex.Error.TryGetNoContent(out _))
            {
                return null;
            }

            if (ex.Error.TryGetRawError(out var raw))
            {
                throw ProviderFailure("finding the Maxio subscription", raw.StatusCode, ex);
            }

            throw ProviderFailure("finding the Maxio subscription", null, ex);
        }
        catch (MaxioSubscriptionException)
        {
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            throw ProviderFailure("finding the Maxio subscription", null, ex);
        }
    }

    private async Task<IReadOnlyList<SubscriptionResponse>> ListCustomerSubscriptionsAsync(
        int customerId,
        CancellationToken cancellationToken)
    {
        try
        {
            return await BoundedAsync(ct => _client.Customers.ListCustomerSubscriptions(customerId, ct: ct), cancellationToken);
        }
        catch (RawSdkException ex)
        {
            throw ProviderFailure("listing Maxio subscriptions", ex.Error.StatusCode, ex);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            throw ProviderFailure("listing Maxio subscriptions", null, ex);
        }
    }

    private async Task<T> BoundedAsync<T>(Func<CancellationToken, Task<T>> operation, CancellationToken requestCancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(requestCancellationToken);
        timeout.CancelAfter(OperationTimeout);
        return await operation(timeout.Token);
    }

    private SubscriptionSummary ToSummary(Subscription subscription, Product? fallbackProduct)
    {
        return new SubscriptionSummary(
            subscription.Id,
            subscription.Reference,
            subscription.Product?.Handle ?? fallbackProduct?.Handle,
            subscription.Product?.Name ?? fallbackProduct?.Name,
            subscription.ProductPriceInCents ?? fallbackProduct?.PriceInCents,
            subscription.State?.Value,
            subscription.CurrentPeriodEndsAt);
    }

    private void EnsureConfiguration()
    {
        if (string.IsNullOrWhiteSpace(_options.ApiKey)
            || string.IsNullOrWhiteSpace(_options.Subdomain)
            || string.IsNullOrWhiteSpace(_options.ProductFamilyHandle))
        {
            throw Failure("Maxio subscription billing is not configured.", HttpStatusCode.BadGateway);
        }
    }

    private static string NormalizeIdentity(string identity) => identity.Trim().ToLowerInvariant();

    private static string BuildCustomerReference(string normalizedIdentity) => $"eshop-user-{Hash(normalizedIdentity)}";

    private static string BuildSubscriptionReference(string normalizedIdentity, string planHandle) =>
        $"eshop-sub-{Hash($"{normalizedIdentity}|{planHandle.ToLowerInvariant()}")}";

    private static string Hash(string value)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static MaxioSubscriptionException ProviderFailure(string operation, HttpStatusCode? statusCode, Exception? innerException) =>
        Failure($"Maxio failed while {operation}.", statusCode, innerException);

    private static MaxioSubscriptionException Failure(string message, HttpStatusCode? statusCode, Exception? innerException = null) =>
        new(message, statusCode, innerException);
}
