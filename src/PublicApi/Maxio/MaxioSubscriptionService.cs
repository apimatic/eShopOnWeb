using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
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
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

/// <summary>
/// <see cref="IMaxioSubscriptionService"/> implemented over the Maxio Advanced Billing SDK.
/// This is the only type in the application that talks to Maxio; every SDK call goes through
/// the shared <see cref="ExecuteAsync{T}"/> boundary, which applies the whole-call time budget,
/// maps every SDK failure (API error, connection failure, unreadable body) to a safe
/// <see cref="MaxioApiException"/>, and keeps Maxio types behind this facade.
/// </summary>
public class MaxioSubscriptionService : IMaxioSubscriptionService
{
    private const string MaxioNotConfiguredMessage =
        "Maxio is not configured. Set Maxio:ApiKey, Maxio:ProductFamilyHandle and Maxio:Subdomain (or Maxio:BaseUrl).";
    private const string CredentialsRejectedMessage =
        "The billing provider rejected the configured credentials.";
    private const string UpstreamErrorMessage =
        "The billing provider could not complete the request.";
    private const string RequestRejectedMessage =
        "The billing provider rejected the request.";
    private const string UnreachableMessage =
        "The billing provider could not be reached. Try again shortly.";
    private const string UnreadableResponseMessage =
        "The billing provider returned an unreadable response.";
    private const string MissingProductFamilyMessage =
        "The configured product family was not found in the billing provider.";

    private static readonly TimeSpan CallBudget = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Serializes subscribe work per customer so two double-click requests for the same user
    /// cannot both pass the "no subscription yet" check before either creates one. Per-process;
    /// a multi-instance deployment would need the same guarantee from a distributed lock or a
    /// uniqueness constraint in its own store (Maxio documents no subscription-level dedupe).
    /// </summary>
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> SubscribeGates =
        new ConcurrentDictionary<string, SemaphoreSlim>();

    private readonly MaxioAdvancedBillingClient _client;
    private readonly MaxioOptions _options;
    private readonly ILogger<MaxioSubscriptionService> _logger;

    public MaxioSubscriptionService(
        MaxioAdvancedBillingClient client,
        IOptions<MaxioOptions> options,
        ILogger<MaxioSubscriptionService> logger)
    {
        _client = client;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<IReadOnlyList<SubscriptionPlan>> ListPlansAsync(CancellationToken cancellationToken)
    {
        return await ExecuteAsync(async token =>
        {
            var products = await _client.ProductFamilies.ListProductsForProductFamily(
                productFamilyId: "handle:" + _options.ProductFamilyHandle,
                dateField: null,
                filter: null,
                startDate: null,
                endDate: null,
                startDatetime: null,
                endDatetime: null,
                includeArchived: false,
                include: null,
                page: 1,
                perPage: 20,
                ct: token);

            var plans = new List<SubscriptionPlan>();
            foreach (var productResponse in products)
            {
                var product = productResponse.Product;
                // Only surface plans this card-free flow can actually fulfil, and skip anything
                // the provider considers archived (includeArchived is already false above).
                if (product is null || product.RequireCreditCard == true)
                {
                    continue;
                }

                plans.Add(new SubscriptionPlan
                {
                    Handle = product.Handle,
                    Name = product.Name,
                    PriceAmount = ToDollars(product.PriceInCents),
                    Interval = product.Interval,
                    IntervalUnit = product.IntervalUnit?.Value
                });
            }

            return (IReadOnlyList<SubscriptionPlan>)plans;
        }, cancellationToken);
    }

    public async Task<SubscriptionEnrollment> SubscribeAsync(MaxioCustomer customer, string planHandle, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(planHandle))
        {
            throw new MaxioApiException(HttpStatusCode.BadRequest, "planHandle is required.");
        }

        var subscriptionReference = BuildSubscriptionReference(customer, planHandle);
        var gate = SubscribeGates.GetOrAdd(customer.Reference, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            return await ExecuteAsync(async token =>
            {
                var customerId = await GetOrCreateCustomerIdAsync(customer, token);

                var existing = await FindSubscriptionAsync(subscriptionReference, token);
                if (existing is not null)
                {
                    return new SubscriptionEnrollment
                    {
                        Subscription = MapSubscription(existing),
                        IsNew = false
                    };
                }

                var created = await CreateSubscriptionAsync(customerId, planHandle, subscriptionReference, token);
                return new SubscriptionEnrollment
                {
                    Subscription = MapSubscription(created),
                    IsNew = true
                };
            }, cancellationToken);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<IReadOnlyList<SubscriptionInfo>> ListSubscriptionsAsync(MaxioCustomer customer, CancellationToken cancellationToken)
    {
        return await ExecuteAsync(async token =>
        {
            var customerRecord = await ReadCustomerAsync(customer.Reference, token);
            if (customerRecord?.Id is null)
            {
                return (IReadOnlyList<SubscriptionInfo>)new List<SubscriptionInfo>();
            }

            var subscriptions = await _client.Customers.ListCustomerSubscriptions(customerRecord.Id.Value, token);

            var result = new List<SubscriptionInfo>();
            foreach (var response in subscriptions)
            {
                if (response.Subscription is not null)
                {
                    result.Add(MapSubscription(response.Subscription));
                }
            }

            return (IReadOnlyList<SubscriptionInfo>)result;
        }, cancellationToken);
    }

    private async Task<int?> GetOrCreateCustomerIdAsync(MaxioCustomer customer, CancellationToken cancellationToken)
    {
        var existing = await ReadCustomerAsync(customer.Reference, cancellationToken);
        if (existing?.Id is not null)
        {
            return existing.Id;
        }

        try
        {
            var created = await CreateCustomerAsync(customer, cancellationToken);
            return created?.Id;
        }
        catch (MaxioApiException ex) when (ex.StatusCode == HttpStatusCode.UnprocessableEntity)
        {
            // The only 422 we expect on create is a duplicate reference: Maxio enforces one
            // customer per reference, so a concurrent request that lost the race is the usual
            // cause. Re-read to settle the outcome instead of surfacing the rejection.
            var winner = await ReadCustomerAsync(customer.Reference, cancellationToken);
            if (winner?.Id is not null)
            {
                return winner.Id;
            }

            throw;
        }
    }

    private async Task<Customer?> ReadCustomerAsync(string reference, CancellationToken cancellationToken)
    {
        try
        {
            return await ExecuteAsync(async token =>
            {
                var response = await _client.Customers.ReadCustomerByReference(reference, token);
                return response.Customer;
            }, cancellationToken);
        }
        catch (MaxioApiException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    private Task<Customer?> CreateCustomerAsync(MaxioCustomer customer, CancellationToken cancellationToken)
    {
        return ExecuteAsync(async token =>
        {
            var response = await _client.Customers.CreateCustomer(new CreateCustomerRequest
            {
                Customer = new CreateCustomer
                {
                    FirstName = customer.FirstName,
                    LastName = customer.LastName,
                    Email = customer.Email,
                    Reference = customer.Reference
                }
            }, token);
            return response.Customer;
        }, cancellationToken);
    }

    private async Task<Subscription?> FindSubscriptionAsync(string reference, CancellationToken cancellationToken)
    {
        try
        {
            return await ExecuteAsync(async token =>
            {
                var response = await _client.Subscriptions.FindSubscription(reference, token);
                return response.Subscription;
            }, cancellationToken);
        }
        catch (MaxioApiException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    private async Task<Subscription> CreateSubscriptionAsync(int? customerId, string planHandle, string subscriptionReference, CancellationToken cancellationToken)
    {
        var response = await ExecuteAsync(async token =>
        {
            return await _client.Subscriptions.CreateSubscription(new CreateSubscriptionRequest
            {
                Subscription = new CreateSubscription
                {
                    CustomerId = customerId,
                    ProductHandle = planHandle,
                    Reference = subscriptionReference,
                    // These demo plans bill the first period at signup, so an automatic-collection
                    // create with no card on file is rejected. "Remittance" tells Maxio the balance
                    // is collected via invoice rather than a card/3-DS, which is what makes the
                    // "no payment method required" subscribe flow work without card capture.
                    PaymentCollectionMethod = CollectionMethod.Remittance
                }
            }, token);
        }, cancellationToken);

        // A successful create is expected to carry the subscription; an empty envelope means the
        // provider returned something we cannot act on, so surface it as an upstream failure.
        return response.Subscription
            ?? throw new MaxioApiException(HttpStatusCode.BadGateway, UnreadableResponseMessage);
    }

    private async Task<T> ExecuteAsync<T>(Func<CancellationToken, Task<T>> operation, CancellationToken cancellationToken)
    {
        if (!_options.IsConfigured)
        {
            throw new MaxioApiException(HttpStatusCode.ServiceUnavailable, MaxioNotConfiguredMessage);
        }

        // The only bound that covers a whole call (retries are bounded per attempt by the client
        // options). One budget here applies to every operation by construction.
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        budget.CancelAfter(CallBudget);

        try
        {
            return await operation(budget.Token);
        }
        catch (SdkException<ListProductsForProductFamilyError> ex)
        {
            if (ex.Error.TryGetString(out _))
            {
                // 404 from listing the configured family is a server-side configuration problem.
                throw Logged(new MaxioApiException(HttpStatusCode.BadGateway, MissingProductFamilyMessage, ex));
            }
            if (ex.Error.TryGetRawError(out var raw))
            {
                throw Logged(MapRawError(raw, ex));
            }
            throw Logged(new MaxioApiException(HttpStatusCode.BadGateway, UpstreamErrorMessage, ex));
        }
        catch (SdkException<CreateCustomerError> ex)
        {
            if (ex.Error.TryGetCustomerErrorResponse1(out _))
            {
                // 422 create-customer. The payload model drops the per-field message (the SDK's
                // generated Errors model only carries per_page/price_point), so the caller-facing
                // message cannot include the specific reason.
                throw Logged(new MaxioApiException(HttpStatusCode.UnprocessableEntity,
                    "The billing provider rejected the customer details.", ex));
            }
            if (ex.Error.TryGetRawError(out var raw))
            {
                throw Logged(MapRawError(raw, ex));
            }
            throw Logged(new MaxioApiException(HttpStatusCode.BadGateway, UpstreamErrorMessage, ex));
        }
        catch (SdkException<CreateSubscriptionError> ex)
        {
            if (ex.Error.TryGetErrorListResponse1(out var validation))
            {
                var message = validation.Errors is { Count: > 0 }
                    ? validation.Errors[0]
                    : RequestRejectedMessage;
                throw Logged(new MaxioApiException(HttpStatusCode.UnprocessableEntity, message, ex));
            }
            if (ex.Error.TryGetRawError(out var raw))
            {
                throw Logged(MapRawError(raw, ex));
            }
            throw Logged(new MaxioApiException(HttpStatusCode.BadGateway, UpstreamErrorMessage, ex));
        }
        catch (SdkException<FindSubscriptionError> ex)
        {
            if (ex.Error.TryGetNoContent(out _))
            {
                // 404 = no subscription with that reference; treated as "absent" by the flow.
                throw Logged(new MaxioApiException(HttpStatusCode.NotFound, "No such subscription.", ex));
            }
            if (ex.Error.TryGetRawError(out var raw))
            {
                throw Logged(MapRawError(raw, ex));
            }
            throw Logged(new MaxioApiException(HttpStatusCode.BadGateway, UpstreamErrorMessage, ex));
        }
        catch (SdkException<RawError> ex)
        {
            throw Logged(MapRawError(ex.Error, ex));
        }
        catch (JsonException ex)
        {
            // An unreadable body — from a 2xx whose shape drifted, or a non-2xx whose error body
            // no longer matches the generated model. In neither case is the outcome known, and it
            // is never a domain "not found", so it must not be converted into an empty result.
            throw Logged(new MaxioApiException(HttpStatusCode.BadGateway, UnreadableResponseMessage, ex));
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException)
        {
            // Connection failures and timeouts (including our own call budget). A timeout from the
            // caller's token also lands here; the client has gone away, so no response is written.
            throw Logged(new MaxioApiException(HttpStatusCode.ServiceUnavailable, UnreachableMessage, ex));
        }
    }

    private MaxioApiException MapRawError(RawError raw, Exception inner)
    {
        var status = raw.StatusCode;
        if (status is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
        {
            return new MaxioApiException(HttpStatusCode.BadGateway, CredentialsRejectedMessage, inner);
        }
        if ((int)status >= 500)
        {
            return new MaxioApiException(HttpStatusCode.BadGateway, UpstreamErrorMessage, inner);
        }
        return new MaxioApiException(status, RequestRejectedMessage, inner);
    }

    private MaxioApiException Logged(MaxioApiException mapped)
    {
        if (mapped.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.UnprocessableEntity)
        {
            _logger.LogDebug(mapped, "Maxio request handled with status {Status}", mapped.StatusCode);
        }
        else
        {
            _logger.LogWarning(mapped, "Maxio request failed with status {Status}", mapped.StatusCode);
        }
        return mapped;
    }

    private static string BuildSubscriptionReference(MaxioCustomer customer, string planHandle) =>
        customer.Reference + ":plan:" + planHandle;

    private static SubscriptionInfo MapSubscription(Subscription subscription)
    {
        return new SubscriptionInfo
        {
            SubscriptionId = subscription.Id,
            PlanHandle = subscription.Product?.Handle,
            PlanName = subscription.Product?.Name,
            PriceAmount = ToDollars(subscription.ProductPriceInCents) ?? ToDollars(subscription.Product?.PriceInCents),
            State = subscription.State?.Value,
            NextBillingDate = subscription.NextAssessmentAt,
            CurrentPeriodEndsAt = subscription.CurrentPeriodEndsAt
        };
    }

    private static decimal? ToDollars(long? cents)
    {
        return cents is null ? (decimal?)null : Math.Round(cents.Value / 100m, 2);
    }
}
