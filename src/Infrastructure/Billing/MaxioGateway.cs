using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.Exceptions;
using MaxioAdvancedBilling.Core.ErrorResponse;
using MaxioAdvancedBilling.Errors;
using MaxioAdvancedBilling.Models;
using MaxioAdvancedBilling.Requests.Customers;
using MaxioAdvancedBilling.Requests.ProductFamilies;
using MaxioAdvancedBilling.Requests.Subscriptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Billing;

public class MaxioGateway : IMaxioGateway
{
    private const int PLAN_PAGE_SIZE = 200;
    private const int PLAN_MAX_PAGES = 5;
    private static readonly TimeSpan CallBudget = TimeSpan.FromSeconds(30);

    private readonly MaxioAdvancedBillingClient _client;
    private readonly string _familyHandle;
    private readonly ILogger<MaxioGateway> _logger;

    public MaxioGateway(MaxioAdvancedBillingClient client, IOptions<MaxioSettings> settings, ILogger<MaxioGateway> logger)
    {
        _client = client;
        _familyHandle = settings.Value.ProductFamilyHandle!;
        _logger = logger;
    }

    public Task<MaxioPlanListing> ListPlansAsync(CancellationToken cancellationToken) =>
        ExecuteAsync("ListPlans", isWrite: false, async ct =>
        {
            var plans = new List<SubscriptionPlan>();
            var complete = false;
            for (var page = 1; page <= PLAN_MAX_PAGES; page++)
            {
                IReadOnlyList<ProductResponse> items;
                try
                {
                    items = await _client.ProductFamilies.ListProductsForProductFamily(
                        new ListProductsForProductFamilyRequest
                        {
                            ProductFamilyId = "handle:" + _familyHandle,
                            Page = page,
                            PerPage = PLAN_PAGE_SIZE
                        },
                        cancellationToken: ct);
                }
                catch (ApiException<ListProductsForProductFamilyError> ex) when (ex.StatusCode == HttpStatusCode.NotFound)
                {
                    throw new MaxioGatewayException(MaxioFailureKind.Unavailable,
                        "The configured Maxio product family was not found.", 404, ex);
                }

                foreach (var item in items)
                {
                    var p = item.Product;
                    if (p.Handle is null || p.ArchivedAt is not null) continue;
                    plans.Add(new SubscriptionPlan(p.Handle, p.Name ?? p.Handle, p.Description,
                        p.PriceInCents ?? 0, p.Interval, p.IntervalUnit?.Value));
                }

                if (items.Count < PLAN_PAGE_SIZE)
                {
                    complete = true;
                    break;
                }
            }

            return new MaxioPlanListing(plans, complete);
        }, cancellationToken);

    public Task<int?> FindCustomerIdByReferenceAsync(string reference, CancellationToken cancellationToken) =>
        ExecuteAsync("FindCustomer", isWrite: false, async ct =>
        {
            try
            {
                var response = await _client.Customers.ReadCustomerByReference(
                    new ReadCustomerByReferenceRequest { Reference = reference }, cancellationToken: ct);
                return response.Customer.Id;
            }
            catch (ApiException<RawError> ex) when (ex.StatusCode == HttpStatusCode.NotFound)
            {
                return (int?)null;
            }
        }, cancellationToken);

    public Task<int> CreateCustomerAsync(string reference, string firstName, string lastName, string email, CancellationToken cancellationToken) =>
        ExecuteAsync("CreateCustomer", isWrite: true, async ct =>
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
                }, cancellationToken: ct);
            return response.Customer.Id
                ?? throw new MaxioGatewayException(MaxioFailureKind.UnknownOutcome, "Maxio created a customer but returned no id.");
        }, cancellationToken, typed: ex =>
            ex is ApiException<CreateCustomerError> t && t.Error.TryGetCustomerErrorResponse1(out _)
                ? Rejected(ex, "Maxio rejected the customer details.")
                : null);

    public Task<SubscriptionSummary?> FindSubscriptionByReferenceAsync(string reference, CancellationToken cancellationToken) =>
        ExecuteAsync("FindSubscription", isWrite: false, async ct =>
        {
            try
            {
                var response = await _client.Subscriptions.FindSubscription(
                    new FindSubscriptionRequest { Reference = reference }, cancellationToken: ct);
                return response.Subscription is null ? null : Map(response.Subscription);
            }
            catch (ApiException<FindSubscriptionError> ex) when (ex.StatusCode == HttpStatusCode.NotFound)
            {
                return null;
            }
        }, cancellationToken);

    public Task<SubscriptionSummary> CreateSubscriptionAsync(int customerId, string planHandle, string reference, CancellationToken cancellationToken) =>
        ExecuteAsync("CreateSubscription", isWrite: true, async ct =>
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
                            // No payment method is captured: bill by invoice (remittance) instead of auto-charging a card.
                            PaymentCollectionMethod = MaxioAdvancedBilling.Models.Enums.CollectionMethod.Remittance
                        }
                    }
                }, cancellationToken: ct);
            if (response.Subscription is null)
                throw new MaxioGatewayException(MaxioFailureKind.UnknownOutcome, "Maxio returned no subscription.");
            return Map(response.Subscription);
        }, cancellationToken, typed: ex =>
            ex is ApiException<CreateSubscriptionError> t && t.Error.TryGetErrorListResponse1(out var errors)
                ? Rejected(ex, "Maxio rejected the subscription: " + string.Join("; ", errors.Errors))
                : null);

    public Task<IReadOnlyList<SubscriptionSummary>> ListCustomerSubscriptionsAsync(int customerId, CancellationToken cancellationToken) =>
        ExecuteAsync<IReadOnlyList<SubscriptionSummary>>("ListCustomerSubscriptions", isWrite: false, async ct =>
        {
            var items = await _client.Customers.ListCustomerSubscriptions(
                new ListCustomerSubscriptionsRequest { CustomerId = customerId }, cancellationToken: ct);
            return items.Where(i => i.Subscription is not null).Select(i => Map(i.Subscription!)).ToList();
        }, cancellationToken);

    private static SubscriptionSummary Map(Subscription s)
    {
        if (s.Id is null)
            throw new MaxioGatewayException(MaxioFailureKind.Unavailable, "Maxio returned a subscription without an id.");
        return new SubscriptionSummary(
            s.Id.Value,
            s.Product?.Handle ?? string.Empty,
            s.Product?.Name ?? s.Product?.Handle ?? string.Empty,
            s.CurrentBillingAmountInCents ?? s.ProductPriceInCents ?? s.Product?.PriceInCents ?? 0,
            s.State?.Value ?? "unknown",
            s.NextAssessmentAt,
            s.CurrentPeriodEndsAt,
            s.CreatedAt);
    }

    private static MaxioGatewayException Rejected(ApiException ex, string message) =>
        new(MaxioFailureKind.Rejected, message, (int)ex.StatusCode, ex);

    /// <summary>
    /// The single error boundary: bounds every call by <see cref="CallBudget"/> and converts every SDK failure into a
    /// <see cref="MaxioGatewayException"/> with a caller-safe message. For a write, "no usable response" is an unknown outcome.
    /// </summary>
    private async Task<T> ExecuteAsync<T>(string operation, bool isWrite, Func<CancellationToken, Task<T>> call,
        CancellationToken cancellationToken, Func<ApiException, MaxioGatewayException?>? typed = null)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(CallBudget);
        var unclear = isWrite ? MaxioFailureKind.UnknownOutcome : MaxioFailureKind.Unavailable;
        try
        {
            return await call(cts.Token);
        }
        catch (MaxioGatewayException)
        {
            throw;
        }
        catch (ApiException ex)
        {
            var status = (int)ex.StatusCode;
            var mapped = typed?.Invoke(ex);
            if (mapped is not null)
            {
                _logger.LogWarning("Maxio {Operation} rejected with HTTP {Status}: {Message}", operation, status, mapped.Message);
                throw mapped;
            }

            if (ex is ResponseDeserializationException && status is >= 200 and < 300)
            {
                _logger.LogError(ex, "Maxio {Operation} returned an unreadable success body (HTTP {Status})", operation, status);
                throw new MaxioGatewayException(unclear, "Billing provider returned a response that could not be processed.", status, ex);
            }

            if (status is 401 or 403 or 429 || status >= 500)
            {
                _logger.LogError(ex, "Maxio {Operation} failed with HTTP {Status}", operation, status);
                throw new MaxioGatewayException(status >= 500 ? unclear : MaxioFailureKind.Unavailable,
                    "Billing provider is unavailable.", status, ex);
            }

            _logger.LogWarning("Maxio {Operation} rejected with HTTP {Status}", operation, status);
            throw new MaxioGatewayException(MaxioFailureKind.Rejected, "Billing provider rejected the request.", status, ex);
        }
        catch (SdkException ex) // timeouts, connection failures, credential application failures
        {
            _logger.LogError(ex, "Maxio {Operation} produced no usable response", operation);
            var kind = ex is AuthSchemeException ? MaxioFailureKind.Unavailable : unclear;
            throw new MaxioGatewayException(kind, "Billing provider is unavailable.", null, ex);
        }
        catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogError(ex, "Maxio {Operation} exceeded its {Budget} budget", operation, CallBudget);
            throw new MaxioGatewayException(unclear, "Billing provider did not answer in time.", null, ex);
        }
    }
}
