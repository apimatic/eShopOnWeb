using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.ErrorResponse;
using MaxioAdvancedBilling.Core.Exceptions;
using Microsoft.Extensions.Logging;
using Errors = MaxioAdvancedBilling.Errors;
using Models = MaxioAdvancedBilling.Models;
using CustomerRequests = MaxioAdvancedBilling.Requests.Customers;
using ProductFamilyRequests = MaxioAdvancedBilling.Requests.ProductFamilies;
using SubscriptionRequests = MaxioAdvancedBilling.Requests.Subscriptions;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

/// <summary>One page of products, before and after filtering out what the shop does not offer.</summary>
public sealed record ProductPage(IReadOnlyList<SubscriptionPlan> Offered, int RawCount);

/// <summary>A Maxio product family as the integration reads it.</summary>
public sealed record ProductFamilyRecord(int? Id, string? Handle);

/// <summary>
/// The only class that talks to the Maxio SDK. Each operation has its own catch ladder (Case A typed error or
/// Case B <see cref="RawError"/>), and every SDK failure leaves here as one of this integration's own types:
/// reads throw <see cref="MaxioBillingException"/>; writes throw <see cref="MaxioWriteRejectedException"/>
/// (definitely not applied) or <see cref="MaxioWriteOutcomeUnknownException"/> (may have been applied).
/// "Not found" on a lookup is returned as <c>null</c>.
/// </summary>
public class MaxioGateway
{
    private readonly MaxioAdvancedBillingClient _client;
    private readonly ILogger<MaxioGateway> _logger;

    public MaxioGateway(MaxioAdvancedBillingClient client, ILogger<MaxioGateway> logger)
    {
        _client = client;
        _logger = logger;
    }

    // ---------------------------------------------------------------- reads

    /// <summary>One page of the family's products, or <c>null</c> when Maxio answers 404 for the family id.</summary>
    public virtual async Task<ProductPage?> ListProductsPageAsync(string productFamilyId, int page, int perPage, CancellationToken ct)
    {
        IReadOnlyList<Models.ProductResponse> items;
        try
        {
            items = await _client.ProductFamilies.ListProductsForProductFamily(
                new ProductFamilyRequests.ListProductsForProductFamilyRequest
                {
                    ProductFamilyId = productFamilyId,
                    Page = page,
                    PerPage = perPage,
                },
                cancellationToken: ct);
        }
        catch (ApiException<Errors.ListProductsForProductFamilyError> ex)
        {
            if (ex.Error.TryGetString(out _))
            {
                return null; // 404: no such product family under this id
            }
            if (ex.Error.TryGetRawError(out RawError raw))
            {
                throw ReadFailure("ListProductsForProductFamily", raw.StatusCode, ex);
            }
            throw ReadFailure("ListProductsForProductFamily", ex.StatusCode, ex);
        }
        catch (ResponseDeserializationException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return null; // 404 whose detail was unreadable: the status still says "no such family"
        }
        catch (SdkException ex)
        {
            throw ReadFailure("ListProductsForProductFamily", ex);
        }

        var offered = new List<SubscriptionPlan>(items.Count);
        foreach (var item in items)
        {
            var product = item.Product;
            if (product is null || product.ArchivedAt is not null || string.IsNullOrEmpty(product.Handle))
            {
                continue;
            }
            offered.Add(new SubscriptionPlan(
                product.Id,
                product.Handle!,
                product.Name,
                product.Description,
                product.PriceInCents,
                product.Interval,
                product.IntervalUnit?.Value,
                product.RequireCreditCard));
        }
        return new ProductPage(offered, items.Count);
    }

    public virtual async Task<IReadOnlyList<ProductFamilyRecord>> ListProductFamiliesAsync(CancellationToken ct)
    {
        try
        {
            var families = await _client.ProductFamilies.ListProductFamilies(
                new ProductFamilyRequests.ListProductFamiliesRequest(),
                cancellationToken: ct);
            return families
                .Select(f => f.ProductFamily)
                .Where(f => f is not null)
                .Select(f => new ProductFamilyRecord(f!.Id, f.Handle))
                .ToList();
        }
        catch (ApiException<RawError> ex)
        {
            throw ReadFailure("ListProductFamilies", ex.StatusCode, ex);
        }
        catch (SdkException ex)
        {
            throw ReadFailure("ListProductFamilies", ex);
        }
    }

    /// <summary>The customer with this reference, or <c>null</c> when Maxio answers 404.</summary>
    public virtual async Task<MaxioCustomerRecord?> FindCustomerByReferenceAsync(string reference, CancellationToken ct)
    {
        Models.CustomerResponse response;
        try
        {
            response = await _client.Customers.ReadCustomerByReference(
                new CustomerRequests.ReadCustomerByReferenceRequest { Reference = reference },
                cancellationToken: ct);
        }
        catch (ApiException<RawError> ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
        catch (ApiException<RawError> ex)
        {
            throw ReadFailure("ReadCustomerByReference", ex.StatusCode, ex);
        }
        catch (SdkException ex)
        {
            throw ReadFailure("ReadCustomerByReference", ex);
        }

        var id = response.Customer?.Id;
        if (id is null)
        {
            _logger.LogError("Maxio ReadCustomerByReference returned a customer without an id.");
            throw new MaxioBillingException(HttpStatus.BadGateway, "The billing provider returned an unexpected response.");
        }
        return new MaxioCustomerRecord(id.Value, response.Customer!.Reference);
    }

    /// <summary>The subscription with this reference, or <c>null</c> when Maxio answers 404.</summary>
    public virtual async Task<SubscriptionSnapshot?> FindSubscriptionByReferenceAsync(string reference, CancellationToken ct)
    {
        Models.SubscriptionResponse response;
        try
        {
            response = await _client.Subscriptions.FindSubscription(
                new SubscriptionRequests.FindSubscriptionRequest { Reference = reference },
                cancellationToken: ct);
        }
        catch (ApiException<Errors.FindSubscriptionError> ex)
        {
            if (ex.Error.TryGetNoContent(out RawError _))
            {
                return null; // 404: no subscription with this reference
            }
            if (ex.Error.TryGetRawError(out RawError raw))
            {
                throw ReadFailure("FindSubscription", raw.StatusCode, ex);
            }
            throw ReadFailure("FindSubscription", ex.StatusCode, ex);
        }
        catch (ResponseDeserializationException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
        catch (SdkException ex)
        {
            throw ReadFailure("FindSubscription", ex);
        }

        return response.Subscription is null ? null : ToSnapshot(response.Subscription);
    }

    /// <summary>Every subscription of the customer (the operation has no paging inputs).</summary>
    public virtual async Task<IReadOnlyList<SubscriptionSnapshot>> ListCustomerSubscriptionsAsync(int customerId, CancellationToken ct)
    {
        try
        {
            var items = await _client.Customers.ListCustomerSubscriptions(
                new CustomerRequests.ListCustomerSubscriptionsRequest { CustomerId = customerId },
                cancellationToken: ct);
            return items
                .Select(i => i.Subscription)
                .Where(s => s is not null)
                .Select(s => ToSnapshot(s!))
                .ToList();
        }
        catch (ApiException<RawError> ex)
        {
            throw ReadFailure("ListCustomerSubscriptions", ex.StatusCode, ex);
        }
        catch (SdkException ex)
        {
            throw ReadFailure("ListCustomerSubscriptions", ex);
        }
    }

    // ---------------------------------------------------------------- writes (POST: never resent by the SDK)

    public virtual async Task<MaxioCustomerRecord> CreateCustomerAsync(string reference, string email, string firstName, string lastName, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested(); // budget already spent: nothing was sent, so this is a definite non-event

        Models.CustomerResponse response;
        try
        {
            response = await _client.Customers.CreateCustomer(
                new CustomerRequests.CreateCustomerOperationRequest
                {
                    Body = new Models.CreateCustomerRequest
                    {
                        Customer = new Models.CreateCustomer
                        {
                            FirstName = firstName,
                            LastName = lastName,
                            Email = email,
                            Reference = reference,
                        },
                    },
                },
                cancellationToken: ct);
        }
        catch (ApiException<Errors.CreateCustomerError> ex)
        {
            var messages = new List<string>();
            if (ex.Error.TryGetCustomerErrorResponse1(out Models.CustomerErrorResponse1 body))
            {
                CollectCustomerErrors(body, messages);
            }
            else if (ex.Error.TryGetRawError(out RawError _))
            {
                // untyped status: only the status is meaningful
            }
            throw WriteFailure("CreateCustomer", ex.StatusCode, messages, ex);
        }
        catch (ResponseDeserializationException ex)
        {
            throw WriteFailure("CreateCustomer", ex.StatusCode, Array.Empty<string>(), ex);
        }
        catch (SdkConnectionException ex) // includes SdkTimeoutException
        {
            _logger.LogWarning("Maxio CreateCustomer connection failed ({ExceptionType}); outcome unknown.", ex.GetType().Name);
            throw new MaxioWriteOutcomeUnknownException("The connection failed during CreateCustomer.", ex);
        }
        catch (AuthSchemeException ex)
        {
            _logger.LogError("Maxio credentials could not be applied to CreateCustomer.");
            throw new MaxioWriteRejectedException(HttpStatus.Unauthorized, Array.Empty<string>(), ex);
        }
        catch (OperationCanceledException ex)
        {
            _logger.LogWarning("Maxio CreateCustomer was cancelled in flight (call budget or client disconnect); outcome unknown.");
            throw new MaxioWriteOutcomeUnknownException("CreateCustomer was cancelled in flight.", ex);
        }

        var id = response.Customer?.Id;
        if (id is null)
        {
            throw new MaxioWriteOutcomeUnknownException("CreateCustomer returned no customer id.",
                new InvalidOperationException("CreateCustomer returned no customer id."));
        }
        return new MaxioCustomerRecord(id.Value, response.Customer!.Reference);
    }

    public virtual async Task<SubscriptionSnapshot> CreateSubscriptionAsync(string productHandle, int customerId, string reference, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        Models.SubscriptionResponse response;
        try
        {
            response = await _client.Subscriptions.CreateSubscription(
                new SubscriptionRequests.CreateSubscriptionOperationRequest
                {
                    Body = new Models.CreateSubscriptionRequest
                    {
                        Subscription = new Models.CreateSubscription
                        {
                            ProductHandle = productHandle,
                            CustomerId = customerId,
                            Reference = reference,
                            // This shop has no card-capture flow, so the subscription is invoiced (remittance)
                            // rather than auto-charged: with the default automatic collection the live site
                            // answered 422 "No payment method was on file". Valid on Relationship Invoicing
                            // sites (remittance, automatic, prepaid) per CreateSubscription.PaymentCollectionMethod.
                            PaymentCollectionMethod = Models.Enums.CollectionMethod.Remittance,
                        },
                    },
                },
                cancellationToken: ct);
        }
        catch (ApiException<Errors.CreateSubscriptionError> ex)
        {
            var messages = new List<string>();
            if (ex.Error.TryGetErrorListResponse1(out Models.ErrorListResponse1 body))
            {
                messages.AddRange(body.Errors.Where(e => !string.IsNullOrWhiteSpace(e)));
            }
            else if (ex.Error.TryGetRawError(out RawError _))
            {
                // untyped status: only the status is meaningful
            }
            throw WriteFailure("CreateSubscription", ex.StatusCode, messages, ex);
        }
        catch (ResponseDeserializationException ex)
        {
            throw WriteFailure("CreateSubscription", ex.StatusCode, Array.Empty<string>(), ex);
        }
        catch (SdkConnectionException ex) // includes SdkTimeoutException
        {
            _logger.LogWarning("Maxio CreateSubscription connection failed ({ExceptionType}); outcome unknown.", ex.GetType().Name);
            throw new MaxioWriteOutcomeUnknownException("The connection failed during CreateSubscription.", ex);
        }
        catch (AuthSchemeException ex)
        {
            _logger.LogError("Maxio credentials could not be applied to CreateSubscription.");
            throw new MaxioWriteRejectedException(HttpStatus.Unauthorized, Array.Empty<string>(), ex);
        }
        catch (OperationCanceledException ex)
        {
            _logger.LogWarning("Maxio CreateSubscription was cancelled in flight (call budget or client disconnect); outcome unknown.");
            throw new MaxioWriteOutcomeUnknownException("CreateSubscription was cancelled in flight.", ex);
        }

        if (response.Subscription?.Id is null)
        {
            throw new MaxioWriteOutcomeUnknownException("CreateSubscription returned no subscription id.",
                new InvalidOperationException("CreateSubscription returned no subscription id."));
        }
        return ToSnapshot(response.Subscription);
    }

    // ---------------------------------------------------------------- mapping

    internal static SubscriptionSnapshot ToSnapshot(Models.Subscription s) => new(
        s.Id,
        s.Reference,
        s.State?.Value,
        s.Product?.Handle,
        s.Product?.Name,
        s.ProductPriceInCents ?? s.Product?.PriceInCents,
        s.Currency,
        s.Product?.Interval,
        s.Product?.IntervalUnit?.Value,
        s.NextAssessmentAt,
        s.CurrentPeriodEndsAt,
        s.ActivatedAt,
        s.CreatedAt);

    private static void CollectCustomerErrors(Models.CustomerErrorResponse1 body, List<string> messages)
    {
        var errors = body.Errors;
        if (errors is null)
        {
            return;
        }
        if (errors.TryGetCustomerError(out Models.CustomerError customerError))
        {
            if (!string.IsNullOrWhiteSpace(customerError.Customer))
            {
                messages.Add(customerError.Customer!);
            }
        }
        else if (errors.TryGetListOfString(out IReadOnlyList<string> list))
        {
            messages.AddRange(list.Where(e => !string.IsNullOrWhiteSpace(e)));
        }
    }

    private Exception WriteFailure(string operation, HttpStatusCode status, IReadOnlyList<string> messages, Exception ex)
    {
        var code = (int)status;
        if (code is >= 200 and < 300 || code >= 500)
        {
            // 2xx we could not read, or a provider 5xx: the write may have been applied.
            _logger.LogWarning("Maxio {Operation} answered HTTP {Status} without a usable result; outcome unknown.", operation, code);
            return new MaxioWriteOutcomeUnknownException($"{operation} answered HTTP {code} without a usable result.", ex);
        }

        if (code is 401 or 403)
        {
            _logger.LogError("Maxio {Operation} rejected the shop's credentials (HTTP {Status}).", operation, code);
        }
        else
        {
            _logger.LogWarning("Maxio {Operation} refused the request (HTTP {Status}): {ProviderErrors}", operation, code, string.Join("; ", messages));
        }
        return new MaxioWriteRejectedException(code, messages, ex);
    }

    private MaxioBillingException ReadFailure(string operation, SdkException ex) => ex switch
    {
        ResponseDeserializationException rde when (int)rde.StatusCode is >= 200 and < 300 =>
            Logged(LogLevel.Error, operation, (int)rde.StatusCode, HttpStatus.BadGateway, "The billing provider returned an unexpected response.", ex),
        ApiException api => ReadFailure(operation, api.StatusCode, ex),
        SdkTimeoutException => Logged(LogLevel.Warning, operation, null, HttpStatus.GatewayTimeout, "The billing provider did not answer in time.", ex),
        SdkConnectionException => Logged(LogLevel.Warning, operation, null, HttpStatus.ServiceUnavailable, "The billing provider is unreachable.", ex),
        _ => Logged(LogLevel.Error, operation, null, HttpStatus.BadGateway, "The billing provider is unavailable.", ex),
    };

    private MaxioBillingException ReadFailure(string operation, HttpStatusCode status, Exception ex)
    {
        var code = (int)status;
        return code switch
        {
            401 or 403 => Logged(LogLevel.Error, operation, code, HttpStatus.BadGateway, "The billing provider rejected the shop's credentials.", ex),
            429 => Logged(LogLevel.Warning, operation, code, HttpStatus.ServiceUnavailable, "The billing provider is rate limiting requests; try again shortly.", ex),
            _ => Logged(code >= 500 ? LogLevel.Warning : LogLevel.Error, operation, code, HttpStatus.BadGateway, "The billing provider is unavailable.", ex),
        };
    }

    private MaxioBillingException Logged(LogLevel level, string operation, int? providerStatus, int callerStatus, string message, Exception ex)
    {
        _logger.Log(level, "Maxio {Operation} failed (provider status {ProviderStatus}, {ExceptionType}).", operation, providerStatus, ex.GetType().Name);
        return new MaxioBillingException(callerStatus, message, null, ex);
    }
}

/// <summary>HTTP status codes used by the Maxio integration.</summary>
internal static class HttpStatus
{
    public const int BadRequest = 400;
    public const int Unauthorized = 401;
    public const int UnprocessableEntity = 422;
    public const int BadGateway = 502;
    public const int ServiceUnavailable = 503;
    public const int GatewayTimeout = 504;
}
