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
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Billing;

/// <summary>
/// <see cref="IBillingGateway"/> over the Maxio Advanced Billing .NET SDK. This is the only class that touches
/// SDK types; every SDK failure is translated here, by one ladder, into <see cref="BillingProviderException"/>.
/// The caller's own cancellation is never translated.
/// </summary>
public class MaxioBillingGateway : IBillingGateway
{
    /// <summary>Products requested per page (Maxio's maximum).</summary>
    public const int PlanPageSize = 200;

    /// <summary>Hard cap on plan pages, independent of what Maxio returns.</summary>
    public const int MaxPlanPages = 5;

    private readonly MaxioAdvancedBillingClient _client;
    private readonly MaxioSettings _settings;
    private readonly ILogger<MaxioBillingGateway> _logger;

    public MaxioBillingGateway(MaxioAdvancedBillingClient client, IOptions<MaxioSettings> settings,
        ILogger<MaxioBillingGateway> logger)
    {
        _client = client;
        _settings = settings.Value;
        _logger = logger;
    }

    public async Task<SubscriptionPlanCatalog> ListPlansAsync(CancellationToken cancellationToken)
    {
        var plans = new List<SubscriptionPlan>();
        for (var page = 1; page <= MaxPlanPages; page++)
        {
            IReadOnlyList<ProductResponse> batch;
            try
            {
                batch = await _client.ProductFamilies.ListProductsForProductFamily(
                    new ListProductsForProductFamilyRequest
                    {
                        ProductFamilyId = $"handle:{_settings.ProductFamilyHandle}",
                        Page = page,
                        PerPage = PlanPageSize
                    },
                    cancellationToken: cancellationToken);
            }
            catch (ApiException<ListProductsForProductFamilyError> ex)
            {
                if (ex.Error.TryGetString(out _))
                {
                    _logger.LogError("Maxio product family {Family} was not found (HTTP 404).", _settings.ProductFamilyHandle);
                    throw new BillingProviderException(BillingFailureKind.Unavailable,
                        "The configured Maxio product family was not found.", (int)ex.StatusCode, innerException: ex);
                }

                throw FromStatus(ex, "listing plans", isWrite: false, errors: null);
            }
            catch (SdkException ex)
            {
                throw Translate(ex, "listing plans", isWrite: false);
            }

            plans.AddRange(batch
                .Select(item => item.Product)
                .Where(p => !string.IsNullOrEmpty(p.Handle) && p.ArchivedAt is null)
                .Select(p => new SubscriptionPlan(p.Id, p.Handle!, p.Name ?? p.Handle!, p.Description,
                    p.PriceInCents ?? 0, p.Interval, p.IntervalUnit?.Value)));

            if (batch.Count < PlanPageSize)
            {
                return new SubscriptionPlanCatalog(plans, IsTruncated: false);
            }
        }

        _logger.LogWarning("Maxio plan listing stopped at the {MaxPages}-page cap; the result is marked truncated.", MaxPlanPages);
        return new SubscriptionPlanCatalog(plans, IsTruncated: true);
    }

    public async Task<BillingCustomerAccount?> FindCustomerByReferenceAsync(string reference, CancellationToken cancellationToken)
    {
        CustomerResponse response;
        try
        {
            response = await _client.Customers.ReadCustomerByReference(
                new ReadCustomerByReferenceRequest { Reference = reference },
                cancellationToken: cancellationToken);
        }
        catch (ApiException<RawError> ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
        catch (SdkException ex)
        {
            throw Translate(ex, "looking up the customer", isWrite: false);
        }

        return ToAccount(response.Customer, isWrite: false);
    }

    public async Task<BillingCustomerAccount> CreateCustomerAsync(NewBillingCustomer customer, CancellationToken cancellationToken)
    {
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
                            FirstName = customer.FirstName,
                            LastName = customer.LastName,
                            Email = customer.Email,
                            Reference = customer.Reference
                        }
                    }
                },
                cancellationToken: cancellationToken);
        }
        catch (ApiException<CreateCustomerError> ex)
        {
            if (ex.Error.TryGetCustomerErrorResponse1(out var body))
            {
                throw FromStatus(ex, "creating the customer", isWrite: true, errors: CustomerErrors(body));
            }

            throw FromStatus(ex, "creating the customer", isWrite: true, errors: null);
        }
        catch (SdkException ex)
        {
            throw Translate(ex, "creating the customer", isWrite: true);
        }

        return ToAccount(response.Customer, isWrite: true);
    }

    public async Task<BillingSubscription> CreateSubscriptionAsync(NewBillingSubscription subscription,
        CancellationToken cancellationToken)
    {
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
                            ProductHandle = subscription.PlanHandle,
                            CustomerId = subscription.CustomerId,
                            Reference = subscription.Reference,
                            // No card is captured in this flow: bill by invoice (remittance) instead of charging a
                            // payment method on file, which Maxio requires for automatic collection.
                            PaymentCollectionMethod = CollectionMethod.Remittance
                        }
                    }
                },
                cancellationToken: cancellationToken);
        }
        catch (ApiException<CreateSubscriptionError> ex)
        {
            if (ex.Error.TryGetErrorListResponse1(out var body))
            {
                throw FromStatus(ex, "creating the subscription", isWrite: true, errors: body.Errors);
            }

            throw FromStatus(ex, "creating the subscription", isWrite: true, errors: null);
        }
        catch (SdkException ex)
        {
            throw Translate(ex, "creating the subscription", isWrite: true);
        }

        return ToSubscription(response.Subscription)
            ?? throw new BillingProviderException(BillingFailureKind.Unavailable,
                "Maxio accepted the subscription request but returned no subscription.", outcomeUnknown: true);
    }

    public async Task<BillingSubscription?> FindSubscriptionByReferenceAsync(string reference, CancellationToken cancellationToken)
    {
        SubscriptionResponse response;
        try
        {
            response = await _client.Subscriptions.FindSubscription(
                new FindSubscriptionRequest { Reference = reference },
                cancellationToken: cancellationToken);
        }
        catch (ApiException<FindSubscriptionError> ex)
        {
            if (ex.Error.TryGetNoContent(out _))
            {
                return null;
            }

            throw FromStatus(ex, "looking up the subscription", isWrite: false, errors: null);
        }
        catch (SdkException ex)
        {
            throw Translate(ex, "looking up the subscription", isWrite: false);
        }

        return ToSubscription(response.Subscription);
    }

    public async Task<IReadOnlyList<BillingSubscription>> ListCustomerSubscriptionsAsync(int customerId,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<SubscriptionResponse> responses;
        try
        {
            responses = await _client.Customers.ListCustomerSubscriptions(
                new ListCustomerSubscriptionsRequest { CustomerId = customerId },
                cancellationToken: cancellationToken);
        }
        catch (SdkException ex)
        {
            throw Translate(ex, "listing subscriptions", isWrite: false);
        }

        return responses
            .Select(r => ToSubscription(r.Subscription))
            .Where(s => s is not null)
            .Select(s => s!)
            .ToList();
    }

    private static IReadOnlyList<string> CustomerErrors(CustomerErrorResponse1 body)
    {
        if (body.Errors is null)
        {
            return Array.Empty<string>();
        }

        if (body.Errors.TryGetListOfString(out var list))
        {
            return list;
        }

        if (body.Errors.TryGetCustomerError(out var single) && !string.IsNullOrWhiteSpace(single.Customer))
        {
            return new[] { single.Customer };
        }

        return Array.Empty<string>();
    }

    private static BillingCustomerAccount ToAccount(Customer customer, bool isWrite) =>
        customer.Id is int id
            ? new BillingCustomerAccount(id, customer.Reference)
            : throw new BillingProviderException(BillingFailureKind.Unavailable,
                "Maxio returned a customer without an id.", outcomeUnknown: isWrite);

    private static BillingSubscription? ToSubscription(Subscription? s)
    {
        if (s?.Id is not int id)
        {
            return null;
        }

        return new BillingSubscription(
            id,
            s.Reference,
            s.State?.Value ?? "unknown",
            s.Product?.Handle,
            s.Product?.Name,
            s.ProductPriceInCents ?? s.Product?.PriceInCents,
            s.Currency,
            s.Product?.Interval,
            s.Product?.IntervalUnit?.Value,
            s.NextAssessmentAt ?? s.CurrentPeriodEndsAt,
            s.ActivatedAt,
            s.CreatedAt);
    }

    /// <summary>The one translation ladder for every SDK failure that is not an operation-specific typed body.</summary>
    private BillingProviderException Translate(SdkException ex, string action, bool isWrite)
    {
        switch (ex)
        {
            case SdkTimeoutException timeout:
                _logger.LogWarning("Maxio did not respond while {Action} within {Timeout}s.", action, timeout.Timeout.TotalSeconds);
                return BillingProviderException.NoResponse(
                    $"Maxio did not respond while {action}.", outcomeUnknown: isWrite, innerException: ex);
            case SdkConnectionException:
                _logger.LogWarning(ex, "Maxio could not be reached while {Action}.", action);
                return BillingProviderException.NoResponse(
                    $"Maxio did not respond while {action} (connection failed).", outcomeUnknown: isWrite, innerException: ex);
            case ResponseDeserializationException unreadable when (int)unreadable.StatusCode is >= 200 and < 300:
                _logger.LogError("Maxio returned an unreadable {Status} body while {Action} (expected {Type}).",
                    (int)unreadable.StatusCode, action, unreadable.TargetType.Name);
                return new BillingProviderException(BillingFailureKind.Unavailable,
                    $"Maxio returned a response that could not be processed while {action}.",
                    (int)unreadable.StatusCode, outcomeUnknown: isWrite, innerException: ex);
            case ApiException api:
                // Includes ApiException<RawError> and an error body that did not match its declared shape
                // (ResponseDeserializationException with a non-2xx status): the status is still meaningful.
                return FromStatus(api, action, isWrite, errors: null);
            case AuthSchemeException:
                _logger.LogError(ex, "Maxio credentials could not be applied while {Action}.", action);
                return new BillingProviderException(BillingFailureKind.Unavailable,
                    "Billing is temporarily unavailable.", innerException: ex);
            default:
                _logger.LogError(ex, "Unexpected Maxio SDK failure while {Action}.", action);
                return new BillingProviderException(BillingFailureKind.Unavailable,
                    "Billing is temporarily unavailable.", outcomeUnknown: isWrite, innerException: ex);
        }
    }

    private BillingProviderException FromStatus(ApiException ex, string action, bool isWrite, IReadOnlyList<string>? errors)
    {
        var status = (int)ex.StatusCode;
        switch (status)
        {
            case 400 or 409 or 422:
                _logger.LogWarning("Maxio rejected the request while {Action} (HTTP {Status}): {Errors}",
                    action, status, string.Join("; ", errors ?? Array.Empty<string>()));
                return new BillingProviderException(BillingFailureKind.Rejected,
                    $"Maxio rejected the request while {action}.", status, errors, innerException: ex);
            case 401 or 403:
                _logger.LogError("Maxio refused the configured credentials while {Action} (HTTP {Status}).", action, status);
                return new BillingProviderException(BillingFailureKind.Unavailable,
                    "Billing is temporarily unavailable.", status, innerException: ex);
            case 429:
                _logger.LogWarning("Maxio rate limit reached while {Action}.", action);
                return new BillingProviderException(BillingFailureKind.Unavailable,
                    "Billing is busy; try again shortly.", status, innerException: ex);
            case >= 500:
                _logger.LogError("Maxio failed while {Action} (HTTP {Status}).", action, status);
                return new BillingProviderException(BillingFailureKind.Unavailable,
                    $"Maxio failed while {action}.", status, outcomeUnknown: isWrite, innerException: ex);
            default:
                _logger.LogError("Maxio answered HTTP {Status} while {Action}.", status, action);
                return new BillingProviderException(BillingFailureKind.Unavailable,
                    $"Maxio could not complete the request while {action}.", status, innerException: ex);
        }
    }
}
