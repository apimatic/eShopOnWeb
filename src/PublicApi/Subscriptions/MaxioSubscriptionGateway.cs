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
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

public interface IMaxioSubscriptionGateway
{
    Task<IReadOnlyList<Product>> ListPlansAsync(string productFamilyHandle, CancellationToken cancellationToken);
    Task<Customer> EnsureCustomerAsync(string reference, string firstName, string lastName, string email, CancellationToken cancellationToken);
    Task<Subscription?> FindSubscriptionAsync(string reference, CancellationToken cancellationToken);
    Task<Subscription> CreateSubscriptionAsync(int customerId, string productHandle, string reference, CancellationToken cancellationToken);
    Task<IReadOnlyList<Subscription>> ListCustomerSubscriptionsAsync(int customerId, CancellationToken cancellationToken);
}

public sealed class MaxioSubscriptionGateway : IMaxioSubscriptionGateway
{
    private readonly MaxioAdvancedBillingClient _client;
    private readonly ILogger<MaxioSubscriptionGateway> _logger;

    public MaxioSubscriptionGateway(MaxioAdvancedBillingClient client, ILogger<MaxioSubscriptionGateway> logger)
    {
        _client = client;
        _logger = logger;
    }

    public async Task<IReadOnlyList<Product>> ListPlansAsync(string productFamilyHandle, CancellationToken cancellationToken)
    {
        try
        {
            var families = await Bounded(ct => _client.ProductFamilies.ListProductFamilies(null, null, null, null, null, ct), cancellationToken);
            var family = families.Select(x => x.ProductFamily).SingleOrDefault(x => x.Handle == productFamilyHandle && x.ArchivedAt is null);
            if (family?.Id is null)
                throw new SubscriptionApiException(503, "The subscription catalog is unavailable.");

            var plans = new List<Product>();
            const int pageSize = 100;
            for (var page = 1; ; page++)
            {
                var result = await Bounded(ct => _client.ProductFamilies.ListProductsForProductFamily(
                    family.Id.Value.ToString(), null, null, null, null, null, null, false, null, page, pageSize, ct), cancellationToken);
                var products = result.Select(x => x.Product).Where(x => x is not null && x.ArchivedAt is null).ToList();
                plans.AddRange(products);
                if (result.Count < pageSize) break;
            }
            return plans;
        }
        catch (SdkException<RawError> ex) { throw ProviderFailure(ex.Error.StatusCode); }
        catch (SdkException<ListProductsForProductFamilyError> ex) { throw new SubscriptionApiException(400, "The subscription catalog request was rejected.", ex); }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException) { throw ProviderUnavailable(ex); }
    }

    public async Task<Customer> EnsureCustomerAsync(string reference, string firstName, string lastName, string email, CancellationToken cancellationToken)
    {
        try
        {
            var existing = await Bounded(ct => _client.Customers.ReadCustomerByReference(reference, ct), cancellationToken);
            return existing.Customer;
        }
        catch (SdkException<RawError> readException) when (readException.Error.StatusCode == HttpStatusCode.NotFound)
        {
            try
            {
                using var writeScope = MaxioWriteOnceHandler.BeginScope();
                var created = await Bounded(ct => _client.Customers.CreateCustomer(new CreateCustomerRequest
                {
                    Customer = new CreateCustomer { FirstName = firstName, LastName = lastName, Email = email, Reference = reference }
                }, ct), cancellationToken);
                return created.Customer;
            }
            catch (SdkException<CreateCustomerError> createException) when (createException.Error.TryGetCustomerErrorResponse1(out _))
            {
                return await ReadCustomerAfterCreateConflictAsync(reference, cancellationToken);
            }
            catch (MaxioWriteRetrySuppressedException)
            {
                return await ReadCustomerAfterCreateConflictAsync(reference, cancellationToken);
            }
            catch (SdkException<CreateCustomerError> createException) { throw new SubscriptionApiException(422, "Maxio rejected the customer enrollment.", createException); }
            catch (Exception createException) when (createException is HttpRequestException or TaskCanceledException or JsonException) { throw ProviderUnavailable(createException); }
        }
        catch (SdkException<RawError> ex) { throw ProviderFailure(ex.Error.StatusCode); }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException) { throw ProviderUnavailable(ex); }
    }

    public async Task<Subscription?> FindSubscriptionAsync(string reference, CancellationToken cancellationToken)
    {
        try
        {
            return (await Bounded(ct => _client.Subscriptions.FindSubscription(reference, ct), cancellationToken)).Subscription;
        }
        catch (SdkException<FindSubscriptionError> ex) when (ex.Error.TryGetNoContent(out _)) { return null; }
        catch (SdkException<FindSubscriptionError> ex) { throw new SubscriptionApiException(400, "The subscription lookup was rejected.", ex); }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException) { throw ProviderUnavailable(ex); }
    }

    public async Task<Subscription> CreateSubscriptionAsync(int customerId, string productHandle, string reference, CancellationToken cancellationToken)
    {
        try
        {
            using var writeScope = MaxioWriteOnceHandler.BeginScope();
            var response = await Bounded(ct => _client.Subscriptions.CreateSubscription(new MaxioAdvancedBilling.Models.CreateSubscriptionRequest
            {
                Subscription = new CreateSubscription
                {
                    CustomerId = customerId,
                    ProductHandle = productHandle,
                    Reference = reference,
                    PaymentCollectionMethod = CollectionMethod.Remittance
                }
            }, ct), cancellationToken);
            return response.Subscription ?? throw new SubscriptionApiException(502, "Maxio returned an incomplete subscription response.");
        }
        catch (MaxioWriteRetrySuppressedException)
        {
            var existing = await FindSubscriptionAsync(reference, cancellationToken);
            return existing ?? throw new SubscriptionApiException(502, "The subscription outcome could not be confirmed.");
        }
        catch (SdkException<CreateSubscriptionError> ex) when (ex.Error.TryGetErrorListResponse1(out var validation))
        {
            _logger.LogWarning("Maxio rejected subscription enrollment with validation errors: {ValidationErrors}",
                string.Join(" | ", validation.Errors.Take(8)));
            var existing = await FindSubscriptionAsync(reference, cancellationToken);
            return existing ?? throw new SubscriptionApiException(422, "Maxio rejected the subscription enrollment.", ex);
        }
        catch (SdkException<CreateSubscriptionError> ex) { throw new SubscriptionApiException(400, "The subscription enrollment was rejected.", ex); }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException) { throw ProviderUnavailable(ex); }
    }

    public async Task<IReadOnlyList<Subscription>> ListCustomerSubscriptionsAsync(int customerId, CancellationToken cancellationToken)
    {
        try
        {
            return (await Bounded(ct => _client.Customers.ListCustomerSubscriptions(customerId, ct), cancellationToken))
                .Select(x => x.Subscription).Where(x => x is not null).Cast<Subscription>().ToList();
        }
        catch (SdkException<RawError> ex) { throw ProviderFailure(ex.Error.StatusCode); }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException) { throw ProviderUnavailable(ex); }
    }

    private async Task<Customer> ReadCustomerAfterCreateConflictAsync(string reference, CancellationToken cancellationToken)
    {
        try
        {
            return (await Bounded(ct => _client.Customers.ReadCustomerByReference(reference, ct), cancellationToken)).Customer;
        }
        catch (SdkException<RawError> ex) { throw ProviderFailure(ex.Error.StatusCode); }
    }

    private static async Task<T> Bounded<T>(Func<CancellationToken, Task<T>> call, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));
        return await call(timeout.Token);
    }

    private static SubscriptionApiException ProviderFailure(HttpStatusCode statusCode) =>
        new((int)statusCode >= 400 && (int)statusCode < 500 ? (int)statusCode : 502, "Maxio could not process the request.");

    private static SubscriptionApiException ProviderUnavailable(Exception innerException) =>
        new(502, "Maxio is temporarily unavailable.", innerException);
}
