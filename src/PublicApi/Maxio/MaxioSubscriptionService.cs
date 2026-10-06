using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
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
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

public sealed class MaxioSubscriptionService : IMaxioSubscriptionService
{
    private static readonly TimeSpan CallBudget = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan ReconcileBudget = TimeSpan.FromSeconds(10);

    private readonly MaxioAdvancedBillingClient _client;
    private readonly MaxioOptions _options;
    private readonly ILogger<MaxioSubscriptionService> _logger;
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _userLocks = new();

    public MaxioSubscriptionService(
        MaxioAdvancedBillingClient client,
        IOptions<MaxioOptions> options,
        ILogger<MaxioSubscriptionService> logger)
    {
        _client = client;
        _options = options.Value;
        _logger = logger;
        ValidateOptions();
    }

    private void ValidateOptions()
    {
        if (string.IsNullOrWhiteSpace(_options.ApiKey))
        {
            throw new MaxioConfigurationException("Maxio:ApiKey is not configured.");
        }
        if (string.IsNullOrWhiteSpace(_options.ProductFamilyHandle))
        {
            throw new MaxioConfigurationException("Maxio:ProductFamilyHandle is not configured.");
        }
        if (string.IsNullOrWhiteSpace(_options.Subdomain) && string.IsNullOrWhiteSpace(_options.BaseUrl))
        {
            throw new MaxioConfigurationException("Maxio:Subdomain (or Maxio:BaseUrl) is not configured.");
        }
    }

    public Task<IReadOnlyList<SubscriptionPlanDto>> ListPlansAsync(CancellationToken cancellationToken)
    {
        return ExecuteAsync(async token =>
        {
            var products = await _client.ProductFamilies.ListProductsForProductFamily(
                productFamilyId: "handle:" + _options.ProductFamilyHandle,
                dateField: null,
                filter: null,
                startDate: null,
                endDate: null,
                startDatetime: null,
                endDatetime: null,
                includeArchived: null,
                include: null,
                page: 1,
                perPage: 100,
                ct: token);

            return products
                .Select(p => p.Product)
                .Where(p => p is not null)
                .Select(MapPlan)
                .ToList<IReadOnlyList<SubscriptionPlanDto>>();
        }, cancellationToken);
    }

    public async Task<SubscriptionDto> SubscribeAsync(string userReference, string userEmail, string planHandle, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(planHandle))
        {
            throw new MaxioException(HttpStatusCode.BadRequest, "PlanHandle is required.");
        }

        var gate = _userLocks.GetOrAdd(userReference, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            return await ExecuteAsync(async token =>
            {
                var customer = await EnsureCustomerAsync(userReference, userEmail, token);
                var subscriptionReference = $"{userReference}:{planHandle}";

                var existing = await FindSubscriptionOrNullAsync(subscriptionReference, token);
                if (existing is not null)
                {
                    return MapSubscription(existing, planHandle);
                }

                try
                {
                    var created = await _client.Subscriptions.CreateSubscription(
                        new CreateSubscriptionRequest
                        {
                            Subscription = new CreateSubscription
                            {
                                ProductHandle = planHandle,
                                CustomerReference = userReference,
                                Reference = subscriptionReference
                            }
                        },
                        ct: token);

                    return MapSubscription(created.Subscription, planHandle);
                }
                catch (Exception ex) when ((ex is HttpRequestException or TaskCanceledException) && !cancellationToken.IsCancellationRequested)
                {
                    // The create may have reached Maxio even though the response was lost.
                    // Reconcile by re-reading the subscription reference before surfacing a failure.
                    using var reconcileCts = new CancellationTokenSource(ReconcileBudget);
                    var reconciled = await FindSubscriptionOrNullAsync(subscriptionReference, reconcileCts.Token);
                    if (reconciled is not null)
                    {
                        return MapSubscription(reconciled, planHandle);
                    }
                    throw;
                }
            }, cancellationToken);
        }
        finally
        {
            gate.Release();
        }
    }

    public Task<IReadOnlyList<SubscriptionDto>> ListSubscriptionsAsync(string userReference, CancellationToken cancellationToken)
    {
        return ExecuteAsync(async token =>
        {
            var customer = await ReadCustomerByReferenceOrNullAsync(userReference, token);
            if (customer is null)
            {
                return new List<SubscriptionDto>();
            }

            var subscriptions = await _client.Customers.ListCustomerSubscriptions(customer.Id ?? 0, ct: token);
            return subscriptions
                .Select(s => MapSubscription(s.Subscription, PlanHandleFromReference(s.Subscription?.Reference)))
                .ToList();
        }, cancellationToken);
    }

    private async Task<Customer> EnsureCustomerAsync(string userReference, string userEmail, CancellationToken cancellationToken)
    {
        var existing = await ReadCustomerByReferenceOrNullAsync(userReference, cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        try
        {
            var created = await _client.Customers.CreateCustomer(
                new CreateCustomerRequest
                {
                    Customer = new CreateCustomer
                    {
                        FirstName = LocalPart(userEmail),
                        LastName = DomainPart(userEmail),
                        Email = userEmail,
                        Reference = userReference
                    }
                },
                ct: cancellationToken);

            return created.Customer;
        }
        catch (SdkException<CreateCustomerError> ex) when (ex.Error.TryGetCustomerErrorResponse1(out _))
        {
            // A 422 on create usually means the reference already exists (a concurrent
            // create won the race). Re-read; if the customer is there, use it.
            var reRead = await ReadCustomerByReferenceOrNullAsync(userReference, cancellationToken);
            if (reRead is not null)
            {
                return reRead;
            }
            throw;
        }
    }

    private async Task<Customer?> ReadCustomerByReferenceOrNullAsync(string reference, CancellationToken cancellationToken)
    {
        try
        {
            var response = await _client.Customers.ReadCustomerByReference(reference, ct: cancellationToken);
            return response.Customer;
        }
        catch (SdkException<RawError> ex) when (ex.Error.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    private async Task<Subscription?> FindSubscriptionOrNullAsync(string reference, CancellationToken cancellationToken)
    {
        try
        {
            var response = await _client.Subscriptions.FindSubscription(reference, ct: cancellationToken);
            return response.Subscription;
        }
        catch (SdkException<FindSubscriptionError> ex) when (ex.Error.TryGetNoContent(out _))
        {
            return null;
        }
    }

    private async Task<T> ExecuteAsync<T>(Func<CancellationToken, Task<T>> call, CancellationToken cancellationToken)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(CallBudget);
        try
        {
            return await call(cts.Token);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            if (ex is MaxioException maxioException)
            {
                throw maxioException;
            }
            var translated = Translate(ex);
            _logger.LogError(translated, "Maxio call failed: {Message}", translated.Message);
            throw translated;
        }
    }

    private static MaxioException Translate(Exception ex)
    {
        switch (ex)
        {
            case SdkException<RawError> sdk:
                return new MaxioException(sdk.Error.StatusCode, "Maxio request failed.", sdk);
            case SdkException<ListProductsForProductFamilyError> sdk:
                if (sdk.Error.TryGetString(out _))
                {
                    return new MaxioException(HttpStatusCode.NotFound, "The Maxio product family was not found.", sdk);
                }
                if (sdk.Error.TryGetRawError(out var rawList))
                {
                    return new MaxioException(rawList.StatusCode, "Maxio request failed.", sdk);
                }
                return new MaxioException(HttpStatusCode.BadGateway, "Maxio request failed.", sdk);
            case SdkException<CreateCustomerError> sdk:
                if (sdk.Error.TryGetCustomerErrorResponse1(out _))
                {
                    return new MaxioException(HttpStatusCode.UnprocessableEntity, "Maxio rejected the customer.", sdk);
                }
                if (sdk.Error.TryGetRawError(out var rawCustomer))
                {
                    return new MaxioException(rawCustomer.StatusCode, "Maxio request failed.", sdk);
                }
                return new MaxioException(HttpStatusCode.BadGateway, "Maxio request failed.", sdk);
            case SdkException<FindSubscriptionError> sdk:
                if (sdk.Error.TryGetNoContent(out _))
                {
                    return new MaxioException(HttpStatusCode.NotFound, "The Maxio subscription was not found.", sdk);
                }
                if (sdk.Error.TryGetRawError(out var rawFind))
                {
                    return new MaxioException(rawFind.StatusCode, "Maxio request failed.", sdk);
                }
                return new MaxioException(HttpStatusCode.BadGateway, "Maxio request failed.", sdk);
            case SdkException<CreateSubscriptionError> sdk:
                if (sdk.Error.TryGetErrorListResponse1(out _))
                {
                    return new MaxioException(HttpStatusCode.UnprocessableEntity, "Maxio rejected the subscription.", sdk);
                }
                if (sdk.Error.TryGetRawError(out var rawCreate))
                {
                    return new MaxioException(rawCreate.StatusCode, "Maxio request failed.", sdk);
                }
                return new MaxioException(HttpStatusCode.BadGateway, "Maxio request failed.", sdk);
            case HttpRequestException or TaskCanceledException:
                return new MaxioException(HttpStatusCode.BadGateway, "Maxio is unreachable.", ex);
            case JsonException json:
                var status = MaxioStatusCaptureHandler.LastStatusCode;
                if (status is int statusCode && statusCode >= 400)
                {
                    return new MaxioException((HttpStatusCode)statusCode, "Maxio rejected the request, but its response could not be read.", json);
                }
                return new MaxioException(HttpStatusCode.BadGateway, "Maxio returned a response that could not be processed.", json);
            default:
                return new MaxioException(HttpStatusCode.BadGateway, "An unexpected Maxio error occurred.", ex);
        }
    }

    private static SubscriptionPlanDto MapPlan(Product product)
    {
        return new SubscriptionPlanDto
        {
            Handle = product.Handle ?? string.Empty,
            Name = product.Name ?? string.Empty,
            Description = product.Description ?? string.Empty,
            PriceInCents = product.PriceInCents ?? 0,
            Price = (product.PriceInCents ?? 0) / 100m,
            Interval = product.Interval ?? 1,
            IntervalUnit = product.IntervalUnit?.Value ?? "month"
        };
    }

    private static SubscriptionDto MapSubscription(Subscription? subscription, string fallbackPlanHandle)
    {
        if (subscription is null)
        {
            throw new MaxioException(HttpStatusCode.BadGateway, "Maxio returned an empty subscription.", null);
        }

        var planHandle = subscription.Product?.Handle ?? fallbackPlanHandle;
        return new SubscriptionDto
        {
            Id = subscription.Id ?? 0,
            PlanHandle = planHandle,
            PlanName = subscription.Product?.Name ?? planHandle,
            PriceInCents = subscription.ProductPriceInCents ?? 0,
            State = subscription.State?.Value ?? "unknown",
            NextBillingDate = subscription.CurrentPeriodEndsAt
        };
    }

    private static string PlanHandleFromReference(string? reference)
    {
        if (string.IsNullOrEmpty(reference))
        {
            return string.Empty;
        }
        var separator = reference.LastIndexOf(':');
        return separator >= 0 ? reference[(separator + 1)..] : reference;
    }

    private static string LocalPart(string email)
    {
        var at = email.IndexOf('@');
        return at > 0 ? email[..at] : email;
    }

    private static string DomainPart(string email)
    {
        var at = email.IndexOf('@');
        return at >= 0 && at < email.Length - 1 ? email[(at + 1)..] : email;
    }
}
