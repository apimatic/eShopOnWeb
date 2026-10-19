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
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.Billing;

/// <summary>
/// The only class that talks to the Maxio SDK. Every SDK failure is translated here into a
/// <see cref="BillingException"/> subtype, so callers handle one failure family.
/// </summary>
public class MaxioBillingGateway
{
    internal const int PlansPerPage = 200;   // Maxio's documented per_page maximum
    internal const int MaxPlanPages = 10;

    private readonly MaxioAdvancedBillingClient _client;
    private readonly MaxioOptions _settings;
    private readonly ILogger<MaxioBillingGateway> _logger;

    public MaxioBillingGateway(MaxioAdvancedBillingClient client, IOptions<MaxioOptions> settings, ILogger<MaxioBillingGateway> logger)
    {
        _client = client;
        _settings = settings.Value;
        _logger = logger;
    }

    /// <summary>Lists the non-archived products of the configured product family.</summary>
    public async Task<PlanCatalog> ListPlansAsync(CancellationToken cancellationToken)
    {
        const string operation = "ListProductsForProductFamily";
        var plans = new List<SubscriptionPlan>();
        var truncated = false;

        for (var page = 1; ; page++)
        {
            IReadOnlyList<ProductResponse> items;
            try
            {
                items = await _client.ProductFamilies.ListProductsForProductFamily(
                    new ListProductsForProductFamilyRequest
                    {
                        ProductFamilyId = $"handle:{_settings.ProductFamilyHandle}",
                        Page = page,
                        PerPage = PlansPerPage
                    },
                    cancellationToken: cancellationToken);
            }
            catch (ApiException<ListProductsForProductFamilyError> ex)
            {
                if (ex.Error.TryGetString(out var notFound))
                {
                    // 404: the configured product family does not exist on this site — our configuration, not the caller's.
                    _logger.LogError("Maxio {Operation} returned {Status}: configured product family not found ({Detail}).", operation, (int)ex.StatusCode, notFound);
                }
                else if (ex.Error.TryGetRawError(out RawError raw))
                {
                    _logger.LogError("Maxio {Operation} returned {Status}.", operation, (int)raw.StatusCode);
                }
                throw Translate(operation, ex, isWrite: false);
            }
            catch (Exception ex) when (ex is SdkException or OperationCanceledException)
            {
                throw Translate(operation, ex, isWrite: false);
            }

            plans.AddRange(items
                .Select(i => i.Product)
                .Where(p => p is not null && p.ArchivedAt is null && !string.IsNullOrWhiteSpace(p.Handle))
                .Select(p => new SubscriptionPlan(
                    p.Id,
                    p.Handle!,
                    p.Name ?? p.Handle!,
                    p.Description,
                    p.PriceInCents,
                    p.Interval,
                    p.IntervalUnit?.Value)));

            if (items.Count < PlansPerPage)
            {
                break;
            }
            if (page >= MaxPlanPages)
            {
                truncated = true;
                _logger.LogWarning("Maxio {Operation}: stopped after {Pages} pages; the plan list is truncated.", operation, page);
                break;
            }
        }

        return new PlanCatalog(plans, truncated);
    }

    /// <summary>Returns the Maxio customer id for <paramref name="reference"/>, or null when no such customer exists.</summary>
    public async Task<int?> FindCustomerIdByReferenceAsync(string reference, CancellationToken cancellationToken)
    {
        const string operation = "ReadCustomerByReference";
        try
        {
            var response = await _client.Customers.ReadCustomerByReference(
                new ReadCustomerByReferenceRequest { Reference = reference },
                cancellationToken: cancellationToken);
            return response.Customer.Id
                ?? throw new MaxioUnavailableException("The billing provider returned a customer without an id.", new InvalidOperationException(operation));
        }
        catch (ApiException<RawError> ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
        catch (Exception ex) when (ex is SdkException or OperationCanceledException)
        {
            throw Translate(operation, ex, isWrite: false);
        }
    }

    public async Task<int> CreateCustomerAsync(string reference, string firstName, string lastName, string email, CancellationToken cancellationToken)
    {
        const string operation = "CreateCustomer";
        try
        {
            var response = await _client.Customers.CreateCustomer(
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

            return response.Customer.Id
                ?? throw new MaxioOutcomeUnknownException("The billing provider did not return the new customer's id.", new InvalidOperationException(operation));
        }
        catch (ApiException<CreateCustomerError> ex)
        {
            if (ex.Error.TryGetCustomerErrorResponse1(out CustomerErrorResponse1 body))
            {
                var details = ReadCustomerErrors(body);
                _logger.LogWarning("Maxio {Operation} rejected with {Status}: {Errors}", operation, (int)ex.StatusCode, string.Join("; ", details));
                throw Rejected(ex.StatusCode, "The billing provider rejected the customer record.", details, ex);
            }
            if (ex.Error.TryGetRawError(out RawError raw))
            {
                _logger.LogWarning("Maxio {Operation} returned {Status}.", operation, (int)raw.StatusCode);
            }
            throw Translate(operation, ex, isWrite: true);
        }
        catch (Exception ex) when (ex is SdkException or OperationCanceledException)
        {
            throw Translate(operation, ex, isWrite: true);
        }
    }

    public async Task<SubscriptionInfo> CreateSubscriptionAsync(int customerId, string planHandle, string reference, CancellationToken cancellationToken)
    {
        const string operation = "CreateSubscription";
        try
        {
            var response = await _client.Subscriptions.CreateSubscription(
                new CreateSubscriptionOperationRequest
                {
                    Body = new CreateSubscriptionRequest
                    {
                        Subscription = new CreateSubscription
                        {
                            ProductHandle = planHandle,
                            CustomerId = customerId,
                            Reference = reference,
                            // No card is captured in this flow: bill by invoice (remittance) instead of
                            // charging a payment method on file, which Maxio requires for "automatic".
                            PaymentCollectionMethod = CollectionMethod.Remittance
                        }
                    }
                },
                cancellationToken: cancellationToken);

            return SubscriptionInfo.From(response.Subscription)
                ?? throw new MaxioOutcomeUnknownException("The billing provider did not return the new subscription.", new InvalidOperationException(operation));
        }
        catch (ApiException<CreateSubscriptionError> ex)
        {
            if (ex.Error.TryGetErrorListResponse1(out ErrorListResponse1 body))
            {
                var details = (body.Errors ?? []).ToList();
                _logger.LogWarning("Maxio {Operation} rejected with {Status}: {Errors}", operation, (int)ex.StatusCode, string.Join("; ", details));
                throw Rejected(ex.StatusCode, "The billing provider rejected the subscription.", details, ex);
            }
            if (ex.Error.TryGetRawError(out RawError raw))
            {
                _logger.LogWarning("Maxio {Operation} returned {Status}.", operation, (int)raw.StatusCode);
            }
            throw Translate(operation, ex, isWrite: true);
        }
        catch (Exception ex) when (ex is SdkException or OperationCanceledException)
        {
            throw Translate(operation, ex, isWrite: true);
        }
    }

    /// <summary>Finds a subscription by the reference this app sent when creating it; null when Maxio has none.</summary>
    public async Task<SubscriptionInfo?> FindSubscriptionByReferenceAsync(string reference, CancellationToken cancellationToken)
    {
        const string operation = "FindSubscription";
        try
        {
            var response = await _client.Subscriptions.FindSubscription(
                new FindSubscriptionRequest { Reference = reference },
                cancellationToken: cancellationToken);
            return SubscriptionInfo.From(response.Subscription);
        }
        catch (ApiException<FindSubscriptionError> ex)
        {
            if (ex.Error.TryGetNoContent(out RawError _))
            {
                return null;
            }
            if (ex.Error.TryGetRawError(out RawError raw))
            {
                _logger.LogWarning("Maxio {Operation} returned {Status}.", operation, (int)raw.StatusCode);
            }
            throw Translate(operation, ex, isWrite: false);
        }
        catch (Exception ex) when (ex is SdkException or OperationCanceledException)
        {
            throw Translate(operation, ex, isWrite: false);
        }
    }

    public async Task<IReadOnlyList<SubscriptionInfo>> ListCustomerSubscriptionsAsync(int customerId, CancellationToken cancellationToken)
    {
        const string operation = "ListCustomerSubscriptions";
        try
        {
            var response = await _client.Customers.ListCustomerSubscriptions(
                new ListCustomerSubscriptionsRequest { CustomerId = customerId },
                cancellationToken: cancellationToken);
            return response
                .Select(r => SubscriptionInfo.From(r.Subscription))
                .OfType<SubscriptionInfo>()
                .ToList();
        }
        catch (Exception ex) when (ex is SdkException or OperationCanceledException)
        {
            throw Translate(operation, ex, isWrite: false);
        }
    }

    private static List<string> ReadCustomerErrors(CustomerErrorResponse1 body)
    {
        if (body.Errors is null)
        {
            return [];
        }
        if (body.Errors.TryGetListOfString(out var list))
        {
            return list.ToList();
        }
        if (body.Errors.TryGetCustomerError(out CustomerError error) && !string.IsNullOrWhiteSpace(error.Customer))
        {
            return [error.Customer];
        }
        return [];
    }

    private static MaxioRejectedException Rejected(HttpStatusCode status, string message, IReadOnlyList<string>? details, Exception inner) =>
        new(status, message, details, inner);

    /// <summary>
    /// The single translation ladder for everything the SDK (or the call budget) raises.
    /// On a write, anything that does not prove the request was refused is an UNKNOWN outcome.
    /// </summary>
    private Exception Translate(string operation, Exception ex, bool isWrite)
    {
        switch (ex)
        {
            case BillingException billing:
                return billing;

            case ResponseDeserializationException rde when (int)rde.StatusCode is >= 400 and < 500:
                _logger.LogWarning(rde, "Maxio {Operation} returned {Status} with an unreadable error body.", operation, (int)rde.StatusCode);
                return Rejected(rde.StatusCode, "The billing provider rejected the request.", null, rde);

            case ApiException api when (int)api.StatusCode is >= 400 and < 500:
                return Rejected(api.StatusCode, "The billing provider rejected the request.", null, api);

            case ApiException api:   // 5xx, or an unreadable 2xx body
                if (isWrite)
                {
                    _logger.LogWarning(api, "Maxio {Operation} outcome unknown (status {Status}).", operation, (int)api.StatusCode);
                    return new MaxioOutcomeUnknownException("The billing provider did not confirm the request.", api);
                }
                _logger.LogError(api, "Maxio {Operation} failed with status {Status}.", operation, (int)api.StatusCode);
                return new MaxioUnavailableException("The billing provider is unavailable. Please try again later.", api);

            case AuthSchemeException auth:
                _logger.LogError(auth.InnerException ?? auth, "Maxio {Operation}: credentials could not be applied.", operation);
                return new MaxioUnavailableException("The billing provider is unavailable. Please try again later.", auth);

            case SdkConnectionException or OperationCanceledException when isWrite:
                // The request may have reached Maxio before the connection failed or the budget ran out.
                _logger.LogWarning(ex, "Maxio {Operation} outcome unknown ({Failure}).", operation, ex.GetType().Name);
                return new MaxioOutcomeUnknownException("The billing provider did not confirm the request.", ex);

            default:   // SdkConnectionException / SdkTimeoutException / budget expiry on a read
                _logger.LogError(ex, "Maxio {Operation} could not be completed ({Failure}).", operation, ex.GetType().Name);
                return new MaxioUnavailableException("The billing provider is unavailable. Please try again later.", ex);
        }
    }
}

public record SubscriptionPlan(int? Id, string Handle, string Name, string? Description, long? PriceInCents, int? Interval, string? IntervalUnit);

public record PlanCatalog(IReadOnlyList<SubscriptionPlan> Plans, bool Truncated);

public record SubscriptionInfo(
    int Id,
    string? Reference,
    int? CustomerId,
    string? PlanHandle,
    string? PlanName,
    long? PriceInCents,
    string? Currency,
    string? State,
    DateTimeOffset? CurrentPeriodEndsAt,
    DateTimeOffset? NextAssessmentAt,
    bool IsLive)
{
    internal static SubscriptionInfo? From(Subscription? s)
    {
        if (s?.Id is not int id)
        {
            return null;
        }

        // Terminal states: a subscription in one of these no longer counts as the user's live subscription.
        var isLive = s.State is null
            || !(s.State == SubscriptionState.Canceled
                 || s.State == SubscriptionState.Expired
                 || s.State == SubscriptionState.FailedToCreate);

        return new SubscriptionInfo(
            id,
            s.Reference,
            s.Customer?.Id,
            s.Product?.Handle,
            s.Product?.Name,
            s.ProductPriceInCents,
            s.Currency,
            s.State?.Value,
            s.CurrentPeriodEndsAt,
            s.NextAssessmentAt,
            isLive);
    }
}
