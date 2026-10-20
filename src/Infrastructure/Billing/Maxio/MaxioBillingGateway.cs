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
using MaxioAdvancedBilling.Models;
using MaxioAdvancedBilling.Models.Enums;
using MaxioAdvancedBilling.Requests.Customers;
using MaxioAdvancedBilling.Requests.ProductFamilies;
using MaxioAdvancedBilling.Requests.Subscriptions;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.Infrastructure.Billing.Maxio;

/// <summary>
/// A provider write whose outcome is not known: the request may have reached Maxio and been acted on.
/// The caller must settle it by re-reading provider state, never report it as a failure.
/// </summary>
public class MaxioWriteOutcomeUnknownException : Exception
{
    public MaxioWriteOutcomeUnknownException(string operation, Exception innerException)
        : base($"The outcome of Maxio {operation} is unknown.", innerException)
    {
    }
}

/// <summary>
/// The one place Maxio SDK calls are made. Every SDK failure is translated here into
/// <see cref="BillingProviderException"/> (definite) or <see cref="MaxioWriteOutcomeUnknownException"/>
/// (a write that may have landed), so nothing above this class handles SDK types.
/// </summary>
public class MaxioBillingGateway
{
    private readonly MaxioAdvancedBillingClient _client;
    private readonly ILogger<MaxioBillingGateway> _logger;

    public MaxioBillingGateway(MaxioAdvancedBillingClient client, ILogger<MaxioBillingGateway> logger)
    {
        _client = client;
        _logger = logger;
    }

    public async Task<IReadOnlyList<Product>> ListProductsPageAsync(string productFamilyHandle, int page, int perPage,
        CancellationToken cancellationToken)
    {
        const string operation = "list products for product family";
        try
        {
            var products = await _client.ProductFamilies.ListProductsForProductFamily(
                new ListProductsForProductFamilyRequest
                {
                    ProductFamilyId = "handle:" + productFamilyHandle,
                    Page = page,
                    PerPage = perPage
                },
                cancellationToken: cancellationToken);
            return products.Select(p => p.Product).Where(p => p is not null).ToList();
        }
        catch (ApiException<ListProductsForProductFamilyError> ex)
        {
            if (ex.Error.TryGetString(out _))
            {
                // 404: the configured product family does not exist on this site — our configuration, not the caller.
                _logger.LogError("Maxio {Operation} returned {Status}: the configured product family was not found.",
                    operation, (int)ex.StatusCode);
                throw new BillingProviderException(BillingFailureKind.Misconfigured,
                    "The subscription plan catalog is not available.", (int)ex.StatusCode, innerException: ex);
            }
            // Any other status has no typed body (TryGetRawError): the status is what we carry.
            throw FromStatus(operation, ex.StatusCode, Array.Empty<string>(), ex);
        }
        catch (SdkException ex)
        {
            throw TranslateRead(operation, ex);
        }
    }

    /// <summary>The customer with this reference, or null when Maxio has none.</summary>
    public async Task<Customer?> FindCustomerByReferenceAsync(string reference, CancellationToken cancellationToken)
    {
        const string operation = "read customer by reference";
        try
        {
            var response = await _client.Customers.ReadCustomerByReference(
                new ReadCustomerByReferenceRequest { Reference = reference },
                cancellationToken: cancellationToken);
            return response.Customer;
        }
        catch (ApiException<RawError> ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            // Only a 404 means "no such customer"; an unreadable body is never treated as absence.
            return null;
        }
        catch (ApiException<RawError> ex)
        {
            throw FromStatus(operation, ex.StatusCode, Array.Empty<string>(), ex);
        }
        catch (SdkException ex)
        {
            throw TranslateRead(operation, ex);
        }
    }

    public async Task<Customer> CreateCustomerAsync(string reference, string email, string firstName, string lastName,
        CancellationToken cancellationToken)
    {
        const string operation = "create customer";
        CustomerResponse response;
        try
        {
            response = await _client.Customers.CreateCustomer(
                new CreateCustomerOperationRequest
                {
                    Body = new CreateCustomerRequest
                    {
                        Customer = new CreateCustomer
                        {
                            FirstName = firstName,
                            LastName = lastName,
                            Email = email,
                            Reference = reference
                        }
                    }
                },
                cancellationToken: cancellationToken);
        }
        catch (ApiException<CreateCustomerError> ex)
        {
            if ((int)ex.StatusCode >= 500)
            {
                throw new MaxioWriteOutcomeUnknownException(operation, ex);
            }
            var errors = new List<string>();
            if (ex.Error.TryGetCustomerErrorResponse1(out var body))
            {
                if (body.Errors is not null && body.Errors.TryGetListOfString(out var list))
                {
                    errors.AddRange(list);
                }
                else if (body.Errors is not null && body.Errors.TryGetCustomerError(out var customerError)
                         && !string.IsNullOrWhiteSpace(customerError.Customer))
                {
                    errors.Add(customerError.Customer!);
                }
            }
            throw FromStatus(operation, ex.StatusCode, errors, ex);
        }
        catch (Exception ex) when (IsAmbiguousWriteFailure(ex))
        {
            _logger.LogWarning(ex, "Maxio {Operation}: outcome unknown ({Failure}).", operation, ex.GetType().Name);
            throw new MaxioWriteOutcomeUnknownException(operation, ex);
        }
        catch (SdkException ex)
        {
            throw TranslateRead(operation, ex);
        }

        // A 2xx without the customer is not a success we can record.
        if (response.Customer?.Id is null)
        {
            throw new MaxioWriteOutcomeUnknownException(operation,
                new InvalidOperationException("Maxio returned no customer id."));
        }
        return response.Customer;
    }

    public async Task<Subscription> CreateSubscriptionAsync(int customerId, string productHandle, string reference,
        CollectionMethod paymentCollectionMethod, CancellationToken cancellationToken)
    {
        const string operation = "create subscription";
        SubscriptionResponse response;
        try
        {
            response = await _client.Subscriptions.CreateSubscription(
                new CreateSubscriptionOperationRequest
                {
                    Body = new CreateSubscriptionRequest
                    {
                        Subscription = new CreateSubscription
                        {
                            ProductHandle = productHandle,
                            CustomerId = customerId,
                            Reference = reference,
                            PaymentCollectionMethod = paymentCollectionMethod
                        }
                    }
                },
                cancellationToken: cancellationToken);
        }
        catch (ApiException<CreateSubscriptionError> ex)
        {
            if ((int)ex.StatusCode >= 500)
            {
                throw new MaxioWriteOutcomeUnknownException(operation, ex);
            }
            IReadOnlyList<string> errors = Array.Empty<string>();
            if (ex.Error.TryGetErrorListResponse1(out var body))
            {
                errors = body.Errors ?? Array.Empty<string>();
            }
            throw FromStatus(operation, ex.StatusCode, errors, ex);
        }
        catch (Exception ex) when (IsAmbiguousWriteFailure(ex))
        {
            _logger.LogWarning(ex, "Maxio {Operation}: outcome unknown ({Failure}).", operation, ex.GetType().Name);
            throw new MaxioWriteOutcomeUnknownException(operation, ex);
        }
        catch (SdkException ex)
        {
            throw TranslateRead(operation, ex);
        }

        // The envelope's subscription is nullable: a 2xx without it is unconfirmed, not a success.
        if (response.Subscription?.Id is null)
        {
            throw new MaxioWriteOutcomeUnknownException(operation,
                new InvalidOperationException("Maxio returned no subscription id."));
        }
        return response.Subscription;
    }

    /// <summary>The subscription with this reference, or null when Maxio has none.</summary>
    public async Task<Subscription?> FindSubscriptionByReferenceAsync(string reference, CancellationToken cancellationToken)
    {
        const string operation = "find subscription";
        try
        {
            var response = await _client.Subscriptions.FindSubscription(
                new FindSubscriptionRequest { Reference = reference },
                cancellationToken: cancellationToken);
            return response.Subscription;
        }
        catch (ApiException<FindSubscriptionError> ex)
        {
            if (ex.Error.TryGetNoContent(out RawError _))
            {
                return null;
            }
            // Any other status has no typed body (TryGetRawError): the status is what we carry.
            throw FromStatus(operation, ex.StatusCode, Array.Empty<string>(), ex);
        }
        catch (SdkException ex)
        {
            throw TranslateRead(operation, ex);
        }
    }

    public async Task<IReadOnlyList<Subscription>> ListCustomerSubscriptionsAsync(int customerId, CancellationToken cancellationToken)
    {
        const string operation = "list customer subscriptions";
        try
        {
            var response = await _client.Customers.ListCustomerSubscriptions(
                new ListCustomerSubscriptionsRequest { CustomerId = customerId },
                cancellationToken: cancellationToken);
            return response.Select(r => r.Subscription).Where(s => s is not null).ToList()!;
        }
        catch (ApiException<RawError> ex)
        {
            throw FromStatus(operation, ex.StatusCode, Array.Empty<string>(), ex);
        }
        catch (SdkException ex)
        {
            throw TranslateRead(operation, ex);
        }
    }

    /// <summary>
    /// Failures after which a write may still have landed: no usable response (connection, timeout),
    /// a cancellation that may have come after the bytes left, or a 2xx body we could not read.
    /// </summary>
    private static bool IsAmbiguousWriteFailure(Exception ex) => ex switch
    {
        SdkConnectionException => true,
        OperationCanceledException => true,
        ResponseDeserializationException rde => (int)rde.StatusCode is >= 200 and < 300 or >= 500,
        ApiException api => (int)api.StatusCode >= 500,
        _ => false
    };

    private BillingProviderException FromStatus(string operation, HttpStatusCode statusCode,
        IReadOnlyList<string> providerErrors, Exception inner)
    {
        var status = (int)statusCode;
        switch (status)
        {
            case 401:
            case 403:
                _logger.LogError("Maxio {Operation} returned {Status}: credentials were refused.", operation, status);
                return new BillingProviderException(BillingFailureKind.Misconfigured,
                    "The billing provider is unavailable.", status, innerException: inner);
            case 429:
                _logger.LogWarning("Maxio {Operation} was rate limited ({Status}).", operation, status);
                return new BillingProviderException(BillingFailureKind.Unavailable,
                    "The billing provider is busy; try again shortly.", status, innerException: inner);
            case >= 400 and < 500:
                _logger.LogWarning("Maxio {Operation} was refused with {Status}: {Errors}",
                    operation, status, string.Join("; ", providerErrors));
                var message = providerErrors.Count > 0
                    ? "The billing provider refused the request: " + string.Join("; ", providerErrors)
                    : "The billing provider refused the request.";
                return new BillingProviderException(BillingFailureKind.Rejected, message, status, providerErrors, inner);
            default:
                _logger.LogError("Maxio {Operation} failed with {Status}.", operation, status);
                return new BillingProviderException(BillingFailureKind.Unavailable,
                    "The billing provider is unavailable.", status, innerException: inner);
        }
    }

    private BillingProviderException TranslateRead(string operation, SdkException ex)
    {
        switch (ex)
        {
            case ResponseDeserializationException rde when (int)rde.StatusCode is >= 200 and < 300:
                _logger.LogError(ex, "Maxio {Operation} returned an unreadable {Status} body.", operation, (int)rde.StatusCode);
                return new BillingProviderException(BillingFailureKind.Unavailable,
                    "The billing provider returned a response that could not be processed.", (int)rde.StatusCode, innerException: ex);
            case ApiException api:
                // An error body that did not match the generated error shape: keep the status.
                return FromStatus(operation, api.StatusCode, Array.Empty<string>(), ex);
            case SdkTimeoutException timeout:
                _logger.LogWarning("Maxio {Operation} timed out after {Timeout}.", operation, timeout.Timeout);
                return new BillingProviderException(BillingFailureKind.Timeout,
                    "The billing provider did not respond in time.", innerException: ex);
            case SdkConnectionException:
                _logger.LogWarning(ex, "Maxio {Operation}: provider unreachable.", operation);
                return new BillingProviderException(BillingFailureKind.Unavailable,
                    "The billing provider is unreachable.", innerException: ex);
            case AuthSchemeException:
                _logger.LogError(ex.InnerException, "Maxio {Operation}: credentials could not be applied.", operation);
                return new BillingProviderException(BillingFailureKind.Misconfigured,
                    "The billing provider is unavailable.", innerException: ex);
            default:
                _logger.LogError(ex, "Maxio {Operation} failed.", operation);
                return new BillingProviderException(BillingFailureKind.Unavailable,
                    "The billing provider is unavailable.", innerException: ex);
        }
    }
}
