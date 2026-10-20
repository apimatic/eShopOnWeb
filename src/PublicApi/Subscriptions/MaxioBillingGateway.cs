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
using MaxioAdvancedBilling.Requests.Customers;
using MaxioAdvancedBilling.Requests.ProductFamilies;
using MaxioAdvancedBilling.Requests.Subscriptions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

/// <summary>
/// The only class that talks to the Maxio SDK. Every SDK failure leaves here as a <see cref="MaxioException"/>
/// with a caller-safe message; the SDK's own message and bodies stay in the logs.
/// </summary>
public class MaxioBillingGateway
{
    public const int PlanPageSize = 200;
    public const int MaxPlanPages = 5;

    private readonly MaxioAdvancedBillingClient _client;
    private readonly string _familyId;
    private readonly ILogger<MaxioBillingGateway> _logger;

    public MaxioBillingGateway(MaxioAdvancedBillingClient client, IOptions<MaxioSettings> settings, ILogger<MaxioBillingGateway> logger)
    {
        _client = client;
        _logger = logger;
        _familyId = "handle:" + settings.Value.ProductFamilyHandle;
    }

    /// <summary>Offered (non-archived) plans of the configured product family. Walks pages up to <see cref="MaxPlanPages"/>.</summary>
    public async Task<PlanListing> ListPlansAsync(CancellationToken ct)
    {
        var plans = new List<SubscriptionPlan>();
        var complete = false;
        try
        {
            for (var page = 1; page <= MaxPlanPages; page++)
            {
                var items = await _client.ProductFamilies.ListProductsForProductFamily(
                    new ListProductsForProductFamilyRequest { ProductFamilyId = _familyId, Page = page, PerPage = PlanPageSize },
                    cancellationToken: ct);

                foreach (var item in items)
                {
                    var p = item.Product;
                    if (p.ArchivedAt is not null || string.IsNullOrWhiteSpace(p.Handle)) continue;
                    plans.Add(new SubscriptionPlan
                    {
                        Handle = p.Handle!,
                        Name = p.Name,
                        Description = p.Description,
                        PriceInCents = p.PriceInCents,
                        Interval = p.Interval,
                        IntervalUnit = p.IntervalUnit?.Value,
                        TrialInterval = p.TrialInterval,
                        RequiresPaymentMethod = p.RequireCreditCard
                    });
                }

                if (items.Count < PlanPageSize)
                {
                    complete = true;
                    break;
                }
            }
        }
        catch (ApiException<ListProductsForProductFamilyError> ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            _logger.LogError("Maxio product family not found (HTTP 404); check the Maxio:ProductFamilyHandle setting.");
            throw new MaxioException(MaxioFailureKind.Unavailable, "Plan catalog unavailable.", 404, ex);
        }
        catch (Exception ex) when (ex is SdkException)
        {
            throw Translate(ex, "ListProductsForProductFamily", write: false);
        }

        if (!complete)
        {
            _logger.LogWarning("Plan listing reached the {Pages}-page cap; the list is reported as incomplete.", MaxPlanPages);
        }
        return new PlanListing { Plans = plans, IsComplete = complete };
    }

    /// <summary>The customer id for a reference, or null when Maxio has none.</summary>
    public async Task<int?> FindCustomerIdAsync(string reference, CancellationToken ct)
    {
        try
        {
            var response = await _client.Customers.ReadCustomerByReference(
                new ReadCustomerByReferenceRequest { Reference = reference }, cancellationToken: ct);
            return response.Customer.Id;
        }
        catch (ApiException<RawError> ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return null; // a typed 404 is the provider saying "no such customer"; an unreadable body is NOT absence
        }
        catch (Exception ex) when (ex is SdkException)
        {
            throw Translate(ex, "ReadCustomerByReference", write: false);
        }
    }

    public async Task<int> CreateCustomerAsync(string reference, string firstName, string lastName, string email, CancellationToken ct)
    {
        try
        {
            var response = await _client.Customers.CreateCustomer(
                new CreateCustomerOperationRequest
                {
                    Body = new CreateCustomerRequest
                    {
                        Customer = new CreateCustomer { FirstName = firstName, LastName = lastName, Email = email, Reference = reference }
                    }
                },
                cancellationToken: ct);

            if (response.Customer?.Id is not int id)
            {
                throw new MaxioException(MaxioFailureKind.OutcomeUnknown, "Customer was created but the response had no id.");
            }
            return id;
        }
        catch (ApiException<CreateCustomerError> ex)
        {
            _logger.LogWarning("Maxio CreateCustomer refused: HTTP {Status}, validation body present: {HasBody}.",
                (int)ex.StatusCode, ex.Error.TryGetCustomerErrorResponse1(out _));
            throw Translate(ex, "CreateCustomer", write: true);
        }
        catch (Exception ex) when (ex is SdkException)
        {
            throw Translate(ex, "CreateCustomer", write: true);
        }
    }

    public async Task<MaxioSubscriptionInfo> CreateSubscriptionAsync(string planHandle, int customerId, string reference, CancellationToken ct)
    {
        try
        {
            var response = await _client.Subscriptions.CreateSubscription(
                new CreateSubscriptionOperationRequest
                {
                    Body = new CreateSubscriptionRequest
                    {
                        Subscription = new CreateSubscription { ProductHandle = planHandle, CustomerId = customerId, Reference = reference, PaymentCollectionMethod = MaxioAdvancedBilling.Models.Enums.CollectionMethod.Remittance }
                    }
                },
                cancellationToken: ct);

            return Map(response.Subscription)
                ?? throw new MaxioException(MaxioFailureKind.OutcomeUnknown, "Subscription response was incomplete.");
        }
        catch (ApiException<CreateSubscriptionError> ex)
        {
            if (ex.Error.TryGetErrorListResponse1(out var list))
            {
                _logger.LogWarning("Maxio CreateSubscription refused: HTTP {Status}: {Errors}", (int)ex.StatusCode, string.Join("; ", list.Errors));
            }
            else
            {
                _logger.LogWarning("Maxio CreateSubscription refused: HTTP {Status}.", (int)ex.StatusCode);
            }
            throw Translate(ex, "CreateSubscription", write: true);
        }
        catch (Exception ex) when (ex is SdkException)
        {
            throw Translate(ex, "CreateSubscription", write: true);
        }
    }

    /// <summary>The subscription with this id, or null when Maxio says it does not exist.</summary>
    public async Task<MaxioSubscriptionInfo?> ReadSubscriptionAsync(int subscriptionId, CancellationToken ct)
    {
        try
        {
            var response = await _client.Subscriptions.ReadSubscription(
                new ReadSubscriptionRequest { SubscriptionId = subscriptionId }, cancellationToken: ct);
            return Map(response.Subscription)
                ?? throw new MaxioException(MaxioFailureKind.Unavailable, "Subscription response was incomplete.");
        }
        catch (ApiException<RawError> ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
        catch (Exception ex) when (ex is SdkException)
        {
            throw Translate(ex, "ReadSubscription", write: false);
        }
    }

    /// <summary>The subscription carrying this reference, or null when Maxio has none.</summary>
    public async Task<MaxioSubscriptionInfo?> FindSubscriptionAsync(string reference, CancellationToken ct)
    {
        try
        {
            var response = await _client.Subscriptions.FindSubscription(
                new FindSubscriptionRequest { Reference = reference }, cancellationToken: ct);
            return Map(response.Subscription)
                ?? throw new MaxioException(MaxioFailureKind.Unavailable, "Subscription response was incomplete.");
        }
        catch (ApiException<FindSubscriptionError> ex) when (ex.StatusCode == HttpStatusCode.NotFound && ex.Error.TryGetNoContent(out _))
        {
            return null;
        }
        catch (Exception ex) when (ex is SdkException)
        {
            throw Translate(ex, "FindSubscription", write: false);
        }
    }

    private static MaxioSubscriptionInfo? Map(MaxioAdvancedBilling.Models.Subscription? s)
    {
        if (s?.Id is not int id) return null;
        return new MaxioSubscriptionInfo(
            id,
            s.Customer?.Id,
            s.State?.Value,
            s.Product?.Handle,
            s.Product?.Name,
            s.Product?.PriceInCents,
            s.Product?.Interval,
            s.Product?.IntervalUnit?.Value,
            s.NextAssessmentAt,
            s.CurrentPeriodEndsAt);
    }

    /// <summary>
    /// One mapping for every SDK failure. On a write, anything that leaves the outcome unclear is OutcomeUnknown.
    /// </summary>
    private MaxioException Translate(Exception ex, string operation, bool write)
    {
        switch (ex)
        {
            case ApiException api:
                var status = (int)api.StatusCode;
                _logger.LogError("Maxio {Operation} failed: HTTP {Status} ({Type}).", operation, status, ex.GetType().Name);
                if (status is 401 or 403 or 429)
                    return new MaxioException(MaxioFailureKind.Unavailable, "Billing provider unavailable.", status, ex);
                if (status >= 200 && status < 300 || status >= 500)
                    return new MaxioException(write ? MaxioFailureKind.OutcomeUnknown : MaxioFailureKind.Unavailable, "Billing provider response could not be used.", status, ex);
                if (status >= 400)
                    return new MaxioException(MaxioFailureKind.Rejected, "Billing provider refused the request.", status, ex);
                return new MaxioException(write ? MaxioFailureKind.OutcomeUnknown : MaxioFailureKind.Unavailable, "Billing provider response could not be used.", status, ex);

            case AuthSchemeException:
                _logger.LogError(ex, "Maxio {Operation} failed: credential could not be applied.", operation);
                return new MaxioException(MaxioFailureKind.Unavailable, "Billing provider unavailable.", null, ex);

            default: // SdkConnectionException (incl. SdkTimeoutException) and any other SdkException
                _logger.LogError(ex, "Maxio {Operation} failed: no usable response.", operation);
                return new MaxioException(write ? MaxioFailureKind.OutcomeUnknown : MaxioFailureKind.Unavailable, "Billing provider did not answer.", null, ex);
        }
    }
}
