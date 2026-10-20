using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.Exceptions;
using MaxioAdvancedBilling.Errors;
using MaxioAdvancedBilling.Models;
using MaxioAdvancedBilling.Models.Enums;
using MaxioAdvancedBilling.Requests.Customers;
using MaxioAdvancedBilling.Requests.ProductFamilies;
using MaxioAdvancedBilling.Requests.Subscriptions;
using Microsoft.eShopWeb.ApplicationCore.Billing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.Infrastructure.Billing;

/// <summary>
/// All Maxio Advanced Billing SDK calls, and the one place SDK exceptions are translated into the
/// application's billing exceptions. Logs status codes / provider request ids only: never the API key,
/// e-mail addresses, references or request/response bodies.
/// </summary>
public class MaxioBillingGateway : IBillingGateway
{
    private const int PlansPerPage = 200;
    private const int MaxPlanPages = 10;

    private static readonly string[] RequestIdHeaders = { "X-Request-Id", "X-Chargify-Request-Id" };

    private readonly MaxioAdvancedBillingClient _client;
    private readonly MaxioSettings _settings;
    private readonly ILogger<MaxioBillingGateway> _logger;

    public MaxioBillingGateway(MaxioAdvancedBillingClient client, MaxioSettings settings, ILogger<MaxioBillingGateway> logger)
    {
        _client = client;
        _settings = settings;
        _logger = logger;
    }

    // ---------------------------------------------------------------- plans

    public async Task<PlanListResult> ListPlansAsync(CancellationToken ct)
    {
        var plans = new List<PlanInfo>();
        var truncated = false;
        try
        {
            for (var page = 1; page <= MaxPlanPages; page++)
            {
                IReadOnlyList<ProductResponse> items = await _client.ProductFamilies.ListProductsForProductFamily(
                    new ListProductsForProductFamilyRequest
                    {
                        ProductFamilyId = "handle:" + _settings.ProductFamilyHandle,
                        Page = page,
                        PerPage = PlansPerPage
                    },
                    cancellationToken: ct);

                foreach (var item in items)
                {
                    var p = item.Product;
                    if (string.IsNullOrWhiteSpace(p.Handle) || p.ArchivedAt is not null) continue;
                    plans.Add(new PlanInfo(p.Handle!, p.Name, p.Description, p.PriceInCents, p.Interval, p.IntervalUnit?.Value));
                }

                if (items.Count < PlansPerPage) break;
                if (page == MaxPlanPages) truncated = true;
            }
        }
        catch (Exception ex) when (IsSdkFailure(ex))
        {
            throw Translate("ListProducts", ex);
        }

        if (truncated)
        {
            _logger.LogWarning("Plan listing hit the {Pages}-page cap and was truncated", MaxPlanPages);
        }
        return new PlanListResult(plans, truncated);
    }

    // ------------------------------------------------------------ customers

    public async Task<BillingCustomer?> FindCustomerByReferenceAsync(string reference, CancellationToken ct)
    {
        try
        {
            var response = await _client.Customers.ReadCustomerByReference(
                new ReadCustomerByReferenceRequest { Reference = reference }, cancellationToken: ct);
            var id = response.Customer.Id;
            if (id is null)
            {
                throw new BillingProviderException(BillingFailureKind.ConfigFault, "The billing provider returned a customer without an id.");
            }
            return new BillingCustomer(id.Value);
        }
        catch (ApiException ex) when (ex is not ResponseDeserializationException && ex.StatusCode == HttpStatusCode.NotFound)
        {
            return null; // Case B read: 404 = no such customer
        }
        catch (Exception ex) when (IsSdkFailure(ex))
        {
            throw Translate("ReadCustomerByReference", ex);
        }
    }

    public async Task<BillingCustomer> CreateCustomerAsync(NewBillingCustomer customer, CancellationToken ct)
    {
        try
        {
            var response = await _client.Customers.CreateCustomer(new CreateCustomerOperationRequest
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
            }, cancellationToken: ct);

            if (response.Customer.Id is { } id)
            {
                return new BillingCustomer(id);
            }
            // 2xx without an id: the customer exists but we cannot read it; settle below.
            throw new ResponseIdMissingException();
        }
        catch (Exception ex) when (IsSdkFailure(ex) || ex is ResponseIdMissingException)
        {
            if (WriteOutcomeUncertain(ex))
            {
                _logger.LogWarning("CreateCustomer outcome uncertain ({Failure}); re-reading by reference", Describe(ex));
                BillingCustomer? found;
                try
                {
                    found = await FindCustomerByReferenceAsync(customer.Reference, ct);
                }
                catch (Exception reread) when (reread is BillingProviderException or BillingProviderRejectedException)
                {
                    throw new BillingProviderException(BillingFailureKind.OutcomeUnknown,
                        "The customer may or may not have been created.", reread);
                }
                if (found is not null) return found;
                throw new BillingProviderException(BillingFailureKind.Unavailable, "The billing provider is unavailable.", ex);
            }
            throw Translate("CreateCustomer", ex);
        }
    }

    // --------------------------------------------------------- subscriptions

    public async Task<SubscriptionInfo?> FindSubscriptionByReferenceAsync(string reference, CancellationToken ct)
    {
        try
        {
            var response = await _client.Subscriptions.FindSubscription(
                new FindSubscriptionRequest { Reference = reference }, cancellationToken: ct);
            if (response.Subscription is null)
            {
                throw new BillingProviderException(BillingFailureKind.ConfigFault, "The billing provider returned an empty subscription.");
            }
            return Map(response.Subscription);
        }
        catch (ApiException ex) when (ex is not ResponseDeserializationException && ex.StatusCode == HttpStatusCode.NotFound)
        {
            return null; // 404 = definitely no subscription with that reference
        }
        catch (Exception ex) when (IsSdkFailure(ex))
        {
            throw Translate("FindSubscription", ex);
        }
    }

    public async Task<SubscriptionInfo> CreateSubscriptionAsync(int customerId, string planHandle, string reference, CancellationToken ct)
    {
        try
        {
            var response = await _client.Subscriptions.CreateSubscription(new CreateSubscriptionOperationRequest
            {
                Body = new CreateSubscriptionRequest
                {
                    Subscription = new CreateSubscription
                    {
                        ProductHandle = planHandle,
                        CustomerId = customerId,
                        Reference = reference,
                        // YOUR CALL: the shopper supplies no payment method, so collect by remittance (invoice) instead of
                        // "automatic" card collection; without it Maxio refuses a priced plan (HTTP 422, no payment method on file).
                        PaymentCollectionMethod = CollectionMethod.Remittance
                    }
                }
            }, cancellationToken: ct);

            if (response.Subscription is { Id: not null } sub)
            {
                return Map(sub);
            }
            throw new ResponseIdMissingException();
        }
        catch (Exception ex) when (IsSdkFailure(ex) || ex is ResponseIdMissingException)
        {
            if (ex is ApiException<CreateSubscriptionError> typed && typed.Error.TryGetErrorListResponse1(out var list))
            {
                _logger.LogWarning("CreateSubscription rejected ({Status}): {Errors}", (int)typed.StatusCode, string.Join("; ", list.Errors));
            }

            if (WriteOutcomeUncertain(ex))
            {
                _logger.LogWarning("CreateSubscription outcome uncertain ({Failure}); re-reading by reference", Describe(ex));
                SubscriptionInfo? found;
                try
                {
                    found = await FindSubscriptionByReferenceAsync(reference, ct);
                    if (found is null)
                    {
                        // second source: the customer's own subscription list
                        var all = await ListCustomerSubscriptionsWithReferenceAsync(customerId, ct);
                        found = all.Where(s => string.Equals(s.Reference, reference, StringComparison.Ordinal))
                            .Select(s => s.Info).FirstOrDefault();
                    }
                }
                catch (Exception reread) when (reread is BillingProviderException or BillingProviderRejectedException)
                {
                    throw new BillingProviderException(BillingFailureKind.OutcomeUnknown,
                        "The subscription may or may not have been created.", reread);
                }
                if (found is not null) return found;
                throw new BillingProviderException(BillingFailureKind.Unavailable, "The billing provider is unavailable.", ex);
            }
            throw Translate("CreateSubscription", ex);
        }
    }

    public async Task<IReadOnlyList<SubscriptionInfo>> ListCustomerSubscriptionsAsync(int customerId, CancellationToken ct)
    {
        var all = await ListCustomerSubscriptionsWithReferenceAsync(customerId, ct);
        return all.Select(a => a.Info).ToList();
    }

    // ------------------------------------------------------------- helpers

    private async Task<IReadOnlyList<(string? Reference, SubscriptionInfo Info)>> ListCustomerSubscriptionsWithReferenceAsync(int customerId, CancellationToken ct)
    {
        try
        {
            var response = await _client.Customers.ListCustomerSubscriptions(
                new ListCustomerSubscriptionsRequest { CustomerId = customerId }, cancellationToken: ct);
            var result = new List<(string?, SubscriptionInfo)>();
            foreach (var envelope in response)
            {
                if (envelope.Subscription is not { Id: not null } sub)
                {
                    _logger.LogWarning("ListCustomerSubscriptions returned a malformed element; skipped");
                    continue;
                }
                result.Add((sub.Reference, Map(sub)));
            }
            return result;
        }
        catch (Exception ex) when (IsSdkFailure(ex))
        {
            throw Translate("ListCustomerSubscriptions", ex);
        }
    }

    private static SubscriptionInfo Map(Subscription s) => new(
        s.Id ?? 0,
        s.Product?.Handle,
        s.Product?.Name,
        s.ProductPriceInCents ?? s.Product?.PriceInCents,
        s.Currency,
        s.State?.Value,
        s.NextAssessmentAt ?? s.CurrentPeriodEndsAt,
        s.CreatedAt);

    private static bool IsSdkFailure(Exception ex) => ex is SdkException;

    /// <summary>
    /// True when the write may have landed: no usable response, an unreadable 2xx, or a 5xx.
    /// A 4xx (including 429) is a definite refusal.
    /// </summary>
    private static bool WriteOutcomeUncertain(Exception ex) => ex switch
    {
        ResponseIdMissingException => true,
        SdkConnectionException => true, // includes SdkTimeoutException
        ApiException api => (int)api.StatusCode is >= 500 or (>= 200 and < 300),
        _ => false
    };

    private Exception Translate(string operation, Exception ex)
    {
        switch (ex)
        {
            case SdkTimeoutException:
                _logger.LogWarning("Maxio {Operation} timed out", operation);
                return new BillingProviderException(BillingFailureKind.Timeout, "The billing provider did not answer in time.", ex);
            case SdkConnectionException:
                _logger.LogWarning("Maxio {Operation} could not reach the provider", operation);
                return new BillingProviderException(BillingFailureKind.Unavailable, "The billing provider is unreachable.", ex);
            case AuthSchemeException:
                _logger.LogError("Maxio {Operation}: credentials could not be applied", operation);
                return new BillingProviderException(BillingFailureKind.ConfigFault, "Billing provider credentials were rejected.", ex);
            case ApiException api:
            {
                var status = (int)api.StatusCode;
                _logger.LogWarning("Maxio {Operation} failed with HTTP {Status}{RequestId}", operation, status, RequestId(api.Headers));
                if (ex is ResponseDeserializationException && status is >= 200 and < 300)
                {
                    return new BillingProviderException(BillingFailureKind.ConfigFault, "The billing provider returned an unexpected response.", ex);
                }
                if (status is 401 or 403 or 404)
                {
                    _logger.LogError("Maxio {Operation}: HTTP {Status} indicates a credential or configuration fault", operation, status);
                    return new BillingProviderException(BillingFailureKind.ConfigFault, "The billing provider rejected our credentials or configuration.", ex);
                }
                if (status == 429 || status >= 500)
                {
                    return new BillingProviderException(BillingFailureKind.Unavailable, "The billing provider is temporarily unavailable.", ex);
                }
                return new BillingProviderRejectedException("The billing provider rejected the request.", ex);
            }
            default:
                return ex;
        }
    }

    private static string Describe(Exception ex) => ex switch
    {
        SdkTimeoutException => "timeout",
        SdkConnectionException => "connection failure",
        ApiException api => $"HTTP {(int)api.StatusCode}",
        _ => ex.GetType().Name
    };

    private static string RequestId(HttpResponseHeaders headers)
    {
        foreach (var name in RequestIdHeaders)
        {
            if (headers.TryGetValues(name, out var values))
            {
                return $" (request id {values.FirstOrDefault()})";
            }
        }
        return string.Empty;
    }

    private sealed class ResponseIdMissingException : Exception
    {
    }
}
