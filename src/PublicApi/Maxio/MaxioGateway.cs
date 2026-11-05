using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.ErrorResponse;
using MaxioAdvancedBilling.Core.Exceptions;
using MaxioAdvancedBilling.Errors;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Sdk = MaxioAdvancedBilling;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

/// <summary>
/// The one place that talks to the Maxio SDK. It owns the catch ladders and translates every SDK failure
/// into one of the Maxio* exceptions so the rest of the app has a single failure vocabulary.
/// </summary>
public class MaxioGateway
{
    private const int PageSize = 200;
    private const int MaxPages = 5;

    private readonly MaxioAdvancedBillingClient _client;
    private readonly MaxioSettings _settings;
    private readonly ILogger<MaxioGateway> _logger;

    public MaxioGateway(MaxioAdvancedBillingClient client, IOptions<MaxioSettings> settings, ILogger<MaxioGateway> logger)
    {
        _client = client;
        _settings = settings.Value;
        _logger = logger;
    }

    /// <summary>Non-archived products of the configured family. IsPartial = the page cap cut the list short.</summary>
    public async Task<(IReadOnlyList<Sdk.Models.Product> Products, bool IsPartial)> ListPlansAsync(CancellationToken ct)
    {
        var products = new List<Sdk.Models.Product>();
        for (var page = 1; page <= MaxPages; page++)
        {
            IReadOnlyList<Sdk.Models.ProductResponse> batch;
            try
            {
                batch = await _client.ProductFamilies.ListProductsForProductFamily(
                    new Sdk.Requests.ProductFamilies.ListProductsForProductFamilyRequest
                    {
                        ProductFamilyId = "handle:" + _settings.ProductFamilyHandle,
                        Page = page,
                        PerPage = PageSize
                    }, cancellationToken: ct);
            }
            catch (ApiException<ListProductsForProductFamilyError> ex)
            {
                throw Translate(ex.StatusCode, "list plans", ex);
            }
            catch (ResponseDeserializationException ex)
            {
                throw Translate(ex.StatusCode, "list plans", ex);
            }
            catch (SdkException ex)
            {
                throw TranslateTransport(ex, "list plans");
            }

            products.AddRange(batch.Select(b => b.Product).Where(p => p is not null && p.ArchivedAt is null)!);
            if (batch.Count < PageSize)
                return (products, false);
        }
        return (products, true);
    }

    /// <summary>Returns the customer id for the reference, or null when Maxio has no such customer.</summary>
    public async Task<int?> FindCustomerIdByReferenceAsync(string reference, CancellationToken ct)
    {
        try
        {
            var response = await _client.Customers.ReadCustomerByReference(
                new Sdk.Requests.Customers.ReadCustomerByReferenceRequest { Reference = reference }, cancellationToken: ct);
            return response.Customer.Id;
        }
        catch (ApiException<RawError> ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
        catch (ApiException<RawError> ex)
        {
            throw Translate(ex.StatusCode, "read customer", ex);
        }
        catch (ResponseDeserializationException ex)
        {
            throw Translate(ex.StatusCode, "read customer", ex);
        }
        catch (SdkException ex)
        {
            throw TranslateTransport(ex, "read customer");
        }
    }

    public async Task<int> CreateCustomerAsync(string firstName, string lastName, string email, string reference, CancellationToken ct)
    {
        try
        {
            var response = await _client.Customers.CreateCustomer(
                new Sdk.Requests.Customers.CreateCustomerOperationRequest
                {
                    Body = new Sdk.Models.CreateCustomerRequest
                    {
                        Customer = new Sdk.Models.CreateCustomer
                        {
                            FirstName = firstName,
                            LastName = lastName,
                            Email = email,
                            Reference = reference
                        }
                    }
                }, cancellationToken: ct);
            return response.Customer.Id ?? throw new MaxioOutcomeUnknownException(
                "Maxio created a customer but returned no id.", new InvalidOperationException("missing customer id"));
        }
        catch (ApiException<CreateCustomerError> ex) when (ex.StatusCode == HttpStatusCode.UnprocessableEntity)
        {
            throw new MaxioRejectedException("The billing system rejected the customer details.", ex);
        }
        catch (ApiException<CreateCustomerError> ex)
        {
            throw Translate(ex.StatusCode, "create customer", ex);
        }
        catch (ResponseDeserializationException ex) when ((int)ex.StatusCode is >= 200 and < 300)
        {
            throw new MaxioOutcomeUnknownException("create customer response unreadable", ex);
        }
        catch (ResponseDeserializationException ex)
        {
            throw Translate(ex.StatusCode, "create customer", ex);
        }
        catch (SdkConnectionException ex)   // includes SdkTimeoutException: the write may have landed
        {
            throw new MaxioOutcomeUnknownException("create customer outcome unknown", ex);
        }
        catch (SdkException ex)
        {
            throw TranslateTransport(ex, "create customer");
        }
    }

    /// <summary>Returns the subscription for the reference, or null when Maxio has none.</summary>
    public async Task<Sdk.Models.Subscription?> FindSubscriptionByReferenceAsync(string reference, CancellationToken ct)
    {
        try
        {
            var response = await _client.Subscriptions.FindSubscription(
                new Sdk.Requests.Subscriptions.FindSubscriptionRequest { Reference = reference }, cancellationToken: ct);
            return response.Subscription;
        }
        catch (ApiException<FindSubscriptionError> ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
        catch (ApiException<FindSubscriptionError> ex)
        {
            throw Translate(ex.StatusCode, "find subscription", ex);
        }
        catch (ResponseDeserializationException ex)
        {
            throw Translate(ex.StatusCode, "find subscription", ex);
        }
        catch (SdkException ex)
        {
            throw TranslateTransport(ex, "find subscription");
        }
    }

    public async Task<Sdk.Models.Subscription> CreateSubscriptionAsync(int customerId, string productHandle, string reference, CancellationToken ct)
    {
        try
        {
            var response = await _client.Subscriptions.CreateSubscription(
                new Sdk.Requests.Subscriptions.CreateSubscriptionOperationRequest
                {
                    Body = new Sdk.Models.CreateSubscriptionRequest
                    {
                        Subscription = new Sdk.Models.CreateSubscription
                        {
                            ProductHandle = productHandle,
                            CustomerId = customerId,
                            // Plans require no payment method; automatic collection would demand one.
                            PaymentCollectionMethod = Sdk.Models.Enums.CollectionMethod.Remittance,
                            Reference = reference
                        }
                    }
                }, cancellationToken: ct);
            return response.Subscription ?? throw new MaxioOutcomeUnknownException(
                "Maxio returned no subscription.", new InvalidOperationException("missing subscription"));
        }
        catch (ApiException<CreateSubscriptionError> ex) when (ex.StatusCode == HttpStatusCode.UnprocessableEntity)
        {
            var message = ex.Error.TryGetErrorListResponse1(out var list) && list.Errors.Count > 0
                ? string.Join("; ", list.Errors)
                : "The billing system rejected the subscription.";
            throw new MaxioRejectedException(message, ex);
        }
        catch (ApiException<CreateSubscriptionError> ex)
        {
            throw Translate(ex.StatusCode, "create subscription", ex);
        }
        catch (ResponseDeserializationException ex) when ((int)ex.StatusCode is >= 200 and < 300)
        {
            throw new MaxioOutcomeUnknownException("create subscription response unreadable", ex);
        }
        catch (ResponseDeserializationException ex)
        {
            throw Translate(ex.StatusCode, "create subscription", ex);
        }
        catch (SdkConnectionException ex)   // includes SdkTimeoutException: the write may have landed
        {
            throw new MaxioOutcomeUnknownException("create subscription outcome unknown", ex);
        }
        catch (SdkException ex)
        {
            throw TranslateTransport(ex, "create subscription");
        }
    }

    public async Task<IReadOnlyList<Sdk.Models.Subscription>> ListCustomerSubscriptionsAsync(int customerId, CancellationToken ct)
    {
        try
        {
            var response = await _client.Customers.ListCustomerSubscriptions(
                new Sdk.Requests.Customers.ListCustomerSubscriptionsRequest { CustomerId = customerId }, cancellationToken: ct);
            return response.Select(r => r.Subscription).Where(s => s is not null).ToList()!;
        }
        catch (ApiException<RawError> ex)
        {
            throw Translate(ex.StatusCode, "list customer subscriptions", ex);
        }
        catch (ResponseDeserializationException ex)
        {
            throw Translate(ex.StatusCode, "list customer subscriptions", ex);
        }
        catch (SdkException ex)
        {
            throw TranslateTransport(ex, "list customer subscriptions");
        }
    }

    // Our credentials/quota (401/403/429), 5xx and anything unexpected => unavailable; other 4xx => rejected.
    private Exception Translate(HttpStatusCode status, string operation, Exception inner)
    {
        _logger.LogWarning("Maxio {Operation} failed with HTTP {Status}", operation, (int)status);
        var code = (int)status;
        if (code is 401 or 403 or 429 || code >= 500 || code < 400)
            return new MaxioUnavailableException("The billing system is temporarily unavailable.", status, inner);
        return new MaxioRejectedException("The billing system rejected the request.", inner);
    }

    private Exception TranslateTransport(Exception ex, string operation)
    {
        _logger.LogError(ex, "Maxio {Operation} failed before a usable response", operation);
        return new MaxioUnavailableException("The billing system is temporarily unavailable.", null, ex);
    }
}
