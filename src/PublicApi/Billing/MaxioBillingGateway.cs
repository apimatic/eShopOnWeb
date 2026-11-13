using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.ErrorResponse;
using MaxioAdvancedBilling.Core.Exceptions;
using MaxioAdvancedBilling.Errors;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MaxioCustomer = MaxioAdvancedBilling.Models.Customer;
using MaxioProduct = MaxioAdvancedBilling.Models.Product;
using MaxioSubscription = MaxioAdvancedBilling.Models.Subscription;

namespace Microsoft.eShopWeb.PublicApi.Billing;

/// <summary>The Maxio calls the subscription flow needs. Every failure surfaces as <see cref="MaxioGatewayException"/>.</summary>
public interface IMaxioBillingGateway
{
    Task<IReadOnlyList<MaxioProduct>> ListProductsPageAsync(string productFamilyHandle, int page, int perPage, CancellationToken cancellationToken);

    /// <returns>The customer, or <c>null</c> when no customer has this reference.</returns>
    Task<MaxioCustomer?> FindCustomerByReferenceAsync(string reference, CancellationToken cancellationToken);

    Task<MaxioCustomer> CreateCustomerAsync(string reference, string email, string firstName, string lastName, CancellationToken cancellationToken);

    /// <returns>The subscription, or <c>null</c> when no subscription has this reference.</returns>
    Task<MaxioSubscription?> FindSubscriptionByReferenceAsync(string reference, CancellationToken cancellationToken);

    Task<MaxioSubscription> CreateSubscriptionAsync(string productHandle, int customerId, string reference, CancellationToken cancellationToken);

    Task<IReadOnlyList<MaxioSubscription>> ListCustomerSubscriptionsAsync(int customerId, CancellationToken cancellationToken);
}

/// <summary>
/// Thin wrapper over <see cref="MaxioAdvancedBillingClient"/>: builds the SDK request records and converts every SDK
/// failure into one <see cref="MaxioGatewayException"/> through a single catch ladder. Caller cancellation
/// (<see cref="OperationCanceledException"/>) is never wrapped.
/// </summary>
public sealed class MaxioBillingGateway : IMaxioBillingGateway
{
    private readonly MaxioAdvancedBillingClient _client;
    private readonly ILogger<MaxioBillingGateway> _logger;
    private readonly MaxioAdvancedBilling.Models.Enums.CollectionMethod _collectionMethod;

    public MaxioBillingGateway(MaxioAdvancedBillingClient client, IOptions<MaxioSettings> settings, ILogger<MaxioBillingGateway> logger)
    {
        _client = client;
        _logger = logger;
        // Validated at startup (MaxioSettingsValidator), so the value is always a known collection method.
        MaxioAdvancedBilling.Models.Enums.CollectionMethod.TryGetKnownValue(settings.Value.EffectivePaymentCollectionMethod, out var method);
        _collectionMethod = method!;
    }

    public async Task<IReadOnlyList<MaxioProduct>> ListProductsPageAsync(string productFamilyHandle, int page, int perPage, CancellationToken cancellationToken)
    {
        const string operation = "ListProductsForProductFamily";
        try
        {
            var response = await _client.ProductFamilies.ListProductsForProductFamily(
                new MaxioAdvancedBilling.Requests.ProductFamilies.ListProductsForProductFamilyRequest
                {
                    // Maxio accepts a family handle in the id slot when prefixed with "handle:".
                    ProductFamilyId = "handle:" + productFamilyHandle,
                    Page = page,
                    PerPage = perPage
                },
                cancellationToken: cancellationToken);

            return response.Where(r => r?.Product is not null).Select(r => r.Product).ToList();
        }
        catch (ApiException<ListProductsForProductFamilyError> ex)
        {
            throw ex.Error.Match(
                onString: _ => Fail(operation, ex.StatusCode == HttpStatusCode.NotFound ? MaxioFailureKind.Configuration : Classify(ex.StatusCode),
                    "The configured Maxio product family was not found.", ex.StatusCode, null, ex),
                otherwise: raw => FromStatus(operation, ex.StatusCode, raw, ex));
        }
        catch (SdkException ex)
        {
            throw Translate(operation, ex);
        }
    }

    public async Task<MaxioCustomer?> FindCustomerByReferenceAsync(string reference, CancellationToken cancellationToken)
    {
        const string operation = "ReadCustomerByReference";
        try
        {
            var response = await _client.Customers.ReadCustomerByReference(
                new MaxioAdvancedBilling.Requests.Customers.ReadCustomerByReferenceRequest { Reference = reference },
                cancellationToken: cancellationToken);

            return RequireId(operation, response.Customer, response.Customer?.Id);
        }
        catch (ApiException<RawError> ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
        catch (ApiException<RawError> ex)
        {
            throw FromStatus(operation, ex.StatusCode, ex.Error, ex);
        }
        catch (SdkException ex)
        {
            throw Translate(operation, ex);
        }
    }

    public async Task<MaxioCustomer> CreateCustomerAsync(string reference, string email, string firstName, string lastName, CancellationToken cancellationToken)
    {
        const string operation = "CreateCustomer";
        try
        {
            var response = await _client.Customers.CreateCustomer(
                new MaxioAdvancedBilling.Requests.Customers.CreateCustomerOperationRequest
                {
                    Body = new MaxioAdvancedBilling.Models.CreateCustomerRequest
                    {
                        Customer = new MaxioAdvancedBilling.Models.CreateCustomer
                        {
                            FirstName = firstName,
                            LastName = lastName,
                            Email = email,
                            // Maxio keeps one customer per reference: this is what makes the create reconcilable.
                            Reference = reference
                        }
                    }
                },
                cancellationToken: cancellationToken);

            return RequireId(operation, response.Customer, response.Customer?.Id)!;
        }
        catch (ApiException<CreateCustomerError> ex)
        {
            throw ex.Error.Match(
                onCustomerErrorResponse1: body => Fail(operation, Classify(ex.StatusCode), "Maxio refused to create the billing customer.",
                    ex.StatusCode, CustomerErrors(body.Errors), ex),
                otherwise: raw => FromStatus(operation, ex.StatusCode, raw, ex));
        }
        catch (SdkException ex)
        {
            throw Translate(operation, ex);
        }
    }

    public async Task<MaxioSubscription?> FindSubscriptionByReferenceAsync(string reference, CancellationToken cancellationToken)
    {
        const string operation = "FindSubscription";
        try
        {
            var response = await _client.Subscriptions.FindSubscription(
                new MaxioAdvancedBilling.Requests.Subscriptions.FindSubscriptionRequest { Reference = reference },
                cancellationToken: cancellationToken);

            return RequireId(operation, response.Subscription, response.Subscription?.Id);
        }
        catch (ApiException<FindSubscriptionError> ex)
        {
            MaxioGatewayException? failure = ex.Error.Match<MaxioGatewayException?>(
                onNoContent: _ => null, // 404: no subscription carries this reference
                otherwise: raw => raw.StatusCode == HttpStatusCode.NotFound ? null : FromStatus(operation, ex.StatusCode, raw, ex));
            if (failure is null)
            {
                return null;
            }
            throw failure;
        }
        catch (SdkException ex)
        {
            throw Translate(operation, ex);
        }
    }

    public async Task<MaxioSubscription> CreateSubscriptionAsync(string productHandle, int customerId, string reference, CancellationToken cancellationToken)
    {
        const string operation = "CreateSubscription";
        try
        {
            var response = await _client.Subscriptions.CreateSubscription(
                new MaxioAdvancedBilling.Requests.Subscriptions.CreateSubscriptionOperationRequest
                {
                    Body = new MaxioAdvancedBilling.Models.CreateSubscriptionRequest
                    {
                        Subscription = new MaxioAdvancedBilling.Models.CreateSubscription
                        {
                            ProductHandle = productHandle,
                            // An existing customer by id - never customer_attributes, which would create a second customer.
                            CustomerId = customerId,
                            // Our deterministic subscription reference (not "Ref", which is a referral code).
                            Reference = reference,
                            // No card is captured in this flow: collect by invoice rather than automatic charge,
                            // which Maxio refuses for a paid product with no payment method on file.
                            PaymentCollectionMethod = _collectionMethod
                        }
                    }
                },
                cancellationToken: cancellationToken);

            return RequireId(operation, response.Subscription, response.Subscription?.Id)!;
        }
        catch (ApiException<CreateSubscriptionError> ex)
        {
            throw ex.Error.Match(
                onErrorListResponse1: body => Fail(operation, Classify(ex.StatusCode), "Maxio refused to create the subscription.",
                    ex.StatusCode, body.Errors, ex),
                otherwise: raw => FromStatus(operation, ex.StatusCode, raw, ex));
        }
        catch (SdkException ex)
        {
            throw Translate(operation, ex);
        }
    }

    public async Task<IReadOnlyList<MaxioSubscription>> ListCustomerSubscriptionsAsync(int customerId, CancellationToken cancellationToken)
    {
        const string operation = "ListCustomerSubscriptions";
        try
        {
            var response = await _client.Customers.ListCustomerSubscriptions(
                new MaxioAdvancedBilling.Requests.Customers.ListCustomerSubscriptionsRequest { CustomerId = customerId },
                cancellationToken: cancellationToken);

            return response.Where(r => r?.Subscription?.Id is not null).Select(r => r.Subscription!).ToList();
        }
        catch (ApiException<RawError> ex)
        {
            throw FromStatus(operation, ex.StatusCode, ex.Error, ex);
        }
        catch (SdkException ex)
        {
            throw Translate(operation, ex);
        }
    }

    // ---- one ladder for everything that is not an operation's own typed error ----

    private MaxioGatewayException Translate(string operation, SdkException ex) => ex switch
    {
        ApiException<RawError> api => FromStatus(operation, api.StatusCode, api.Error, api),
        // The server answered but the body did not fit the model. On a 2xx the outcome is unknown; on an error
        // status Maxio refused and only the detail was lost - keep its status.
        ResponseDeserializationException rde => Fail(operation,
            (int)rde.StatusCode is >= 200 and < 300 ? MaxioFailureKind.Unreadable : Classify(rde.StatusCode),
            "Maxio returned a response that could not be processed.", rde.StatusCode, null, rde),
        ApiException api => Fail(operation, Classify(api.StatusCode), "Maxio returned an error.", api.StatusCode, null, api),
        SdkTimeoutException timeout => Fail(operation, MaxioFailureKind.Unreachable,
            $"Maxio did not answer within {timeout.Timeout.TotalSeconds:0} s.", null, null, timeout),
        SdkConnectionException conn => Fail(operation, MaxioFailureKind.Unreachable, "Maxio could not be reached.", null, null, conn),
        AuthSchemeException auth => Fail(operation, MaxioFailureKind.Configuration, "Maxio credentials are missing or could not be applied.", null, null, auth),
        _ => Fail(operation, MaxioFailureKind.ProviderError, "Unexpected failure calling Maxio.", null, null, ex)
    };

    private MaxioGatewayException FromStatus(string operation, HttpStatusCode status, RawError? raw, Exception inner) =>
        Fail(operation, Classify(status), $"Maxio answered {(int)status}.", status, ErrorsFromRaw(raw), inner);

    private static MaxioFailureKind Classify(HttpStatusCode status) => (int)status switch
    {
        401 or 403 => MaxioFailureKind.Configuration,
        404 => MaxioFailureKind.NotFound,
        429 => MaxioFailureKind.RateLimited,
        >= 400 and < 500 => MaxioFailureKind.Rejected,
        _ => MaxioFailureKind.ProviderError
    };

    private MaxioGatewayException Fail(string operation, MaxioFailureKind kind, string message, HttpStatusCode? status,
        IReadOnlyList<string>? errors, Exception inner)
    {
        // Never log bodies (customer e-mail/names): operation, kind, status and Maxio's error messages only.
        var level = kind is MaxioFailureKind.Rejected or MaxioFailureKind.NotFound ? LogLevel.Warning : LogLevel.Error;
        _logger.Log(level, inner, "Maxio {Operation} failed: {Kind} (HTTP {Status}) {ProviderErrors}",
            operation, kind, status is null ? "none" : ((int)status).ToString(), errors is null ? "" : string.Join("; ", errors));
        return new MaxioGatewayException(operation, kind, message, status, errors, inner);
    }

    private T? RequireId<T>(string operation, T? model, int? id) where T : class
    {
        // A success body without the record or its id is a contract break, not "absent".
        if (model is null || id is null)
        {
            throw Fail(operation, MaxioFailureKind.Unreadable, "Maxio returned an incomplete response.", HttpStatusCode.OK, null,
                new InvalidOperationException($"{operation} returned no record id."));
        }
        return model;
    }

    private static IReadOnlyList<string>? CustomerErrors(MaxioAdvancedBilling.Models.AnyOf.Errors1? errors) =>
        errors?.Match<IReadOnlyList<string>?>(
            onCustomerError: e => string.IsNullOrWhiteSpace(e.Customer) ? null : new[] { e.Customer },
            onListOfString: list => list,
            otherwise: _ => null);

    private static IReadOnlyList<string>? ErrorsFromRaw(RawError? raw)
    {
        if (raw is null)
        {
            return null;
        }
        try
        {
            using var doc = JsonDocument.Parse(raw.ReadAsBytes());
            if (doc.RootElement.ValueKind == JsonValueKind.Object &&
                doc.RootElement.TryGetProperty("errors", out var errors))
            {
                if (errors.ValueKind == JsonValueKind.Array)
                {
                    return errors.EnumerateArray().Where(e => e.ValueKind == JsonValueKind.String).Select(e => e.GetString()!).ToList();
                }
                if (errors.ValueKind == JsonValueKind.String)
                {
                    return new[] { errors.GetString()! };
                }
            }
        }
        catch (JsonException)
        {
            // Not JSON (a gateway/proxy page): no provider messages to carry.
        }
        return null;
    }
}
