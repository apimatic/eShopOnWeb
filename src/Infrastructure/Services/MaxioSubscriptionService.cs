using System;
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
using MaxioAdvancedBilling.Models.Enums;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Models.Billing;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Services;

/// <summary>
/// <see cref="ISubscriptionService"/> backed by Maxio Advanced Billing.
/// Customers are matched to eShopOnWeb users by the Maxio customer
/// <c>reference</c> field, which Maxio enforces as unique — that is what makes
/// find-or-create and subscribe idempotent.
/// </summary>
public class MaxioSubscriptionService : ISubscriptionService
{
    private static readonly TimeSpan CallBudget = TimeSpan.FromSeconds(30);

    // States in which a subscription no longer counts against the duplicate guard.
    private static readonly ISet<string> TerminalStates = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "canceled", "expired", "failed_to_create"
    };

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

    public async Task<IReadOnlyList<SubscriptionPlanDto>> ListSubscriptionPlansAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            return await ListSubscriptionPlansCoreAsync(cancellationToken);
        }
        catch (BillingException)
        {
            throw;
        }
        catch (SdkException<ListProductsForProductFamilyError> ex)
        {
            throw TranslateListProductsError(ex);
        }
        catch (JsonException ex)
        {
            throw UnprocessableProviderResponse(ex);
        }
        catch (Exception ex) when (IsTransportFailure(ex, cancellationToken))
        {
            throw ProviderUnreachable(ex);
        }
    }

    public async Task<CustomerSubscriptionDto> SubscribeAsync(string userName, string productHandle, CancellationToken cancellationToken = default)
    {
        try
        {
            return await SubscribeCoreAsync(userName, productHandle, cancellationToken);
        }
        catch (BillingException)
        {
            throw;
        }
        catch (SdkException<CreateSubscriptionError> ex)
        {
            throw TranslateCreateSubscriptionError(ex);
        }
        catch (SdkException<RawError> ex)
        {
            throw TranslateRawError(ex.Error);
        }
        catch (JsonException ex)
        {
            throw UnprocessableProviderResponse(ex);
        }
        catch (Exception ex) when (IsTransportFailure(ex, cancellationToken))
        {
            // The create may have reached Maxio before the connection failed
            // (the SDK resends writes on transport errors), so settle the outcome
            // by re-reading provider state before reporting failure.
            var settled = await TryReconcileSubscriptionAsync(userName, productHandle);
            if (settled is not null)
            {
                return settled;
            }
            throw new BillingException(
                (int)HttpStatusCode.BadGateway,
                "The billing provider could not be reached; the subscription may not have been created. Please retry — repeating the request is safe.",
                ex);
        }
    }

    public async Task<IReadOnlyList<CustomerSubscriptionDto>> ListSubscriptionsForUserAsync(string userName, CancellationToken cancellationToken = default)
    {
        try
        {
            var customer = await FindCustomerByReferenceAsync(userName, cancellationToken);
            if (customer?.Id is null)
            {
                return Array.Empty<CustomerSubscriptionDto>();
            }
            return await ListSubscriptionsAsync(customer.Id.Value, cancellationToken);
        }
        catch (BillingException)
        {
            throw;
        }
        catch (SdkException<RawError> ex)
        {
            throw TranslateRawError(ex.Error);
        }
        catch (JsonException ex)
        {
            throw UnprocessableProviderResponse(ex);
        }
        catch (Exception ex) when (IsTransportFailure(ex, cancellationToken))
        {
            throw ProviderUnreachable(ex);
        }
    }

    private async Task<IReadOnlyList<SubscriptionPlanDto>> ListSubscriptionPlansCoreAsync(CancellationToken cancellationToken)
    {
        var plans = new List<SubscriptionPlanDto>();
        const int perPage = 100;
        var page = 1;

        while (true)
        {
            var currentPage = page;
            var products = await Bounded(
                ct => _client.ProductFamilies.ListProductsForProductFamily(
                    productFamilyId: "handle:" + _options.ProductFamilyHandle,
                    dateField: null,
                    filter: null,
                    startDate: null,
                    endDate: null,
                    startDatetime: null,
                    endDatetime: null,
                    includeArchived: null,
                    include: null,
                    page: currentPage,
                    perPage: perPage,
                    ct: ct),
                cancellationToken);

            foreach (var wrapper in products)
            {
                var product = wrapper.Product;
                if (product is null || product.ArchivedAt is not null)
                {
                    continue;
                }

                plans.Add(new SubscriptionPlanDto
                {
                    Name = product.Name,
                    Handle = product.Handle,
                    Description = product.Description,
                    PriceInCents = product.PriceInCents,
                    Interval = product.Interval,
                    IntervalUnit = product.IntervalUnit?.Value
                });
            }

            if (products.Count < perPage)
            {
                break;
            }
            page++;
        }

        return plans;
    }

    private async Task<CustomerSubscriptionDto> SubscribeCoreAsync(string userName, string productHandle, CancellationToken cancellationToken)
    {
        var customer = await FindOrCreateCustomerAsync(userName, cancellationToken);
        if (customer.Id is null)
        {
            _logger.LogError("Maxio customer for reference '{Reference}' has no id.", userName);
            throw new BillingException(
                (int)HttpStatusCode.BadGateway,
                "The billing provider returned an incomplete customer record.");
        }

        // Double-click guard: never create a second non-terminal subscription
        // for the same customer and plan.
        var existing = await FindActiveSubscriptionAsync(customer.Id.Value, productHandle, cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        var response = await Bounded(
            ct => _client.Subscriptions.CreateSubscription(
                new CreateSubscriptionRequest
                {
                    Subscription = new CreateSubscription
                    {
                        ProductHandle = productHandle,
                        CustomerId = customer.Id.Value,
                        Reference = userName + ":" + productHandle,
                        // The plans this integration serves do not capture a card at
                        // signup; remittance bills the balance by invoice instead of
                        // auto-charging a payment method on file.
                        PaymentCollectionMethod = CollectionMethod.Remittance
                    }
                },
                ct: ct),
            cancellationToken);

        var subscription = response.Subscription;
        if (subscription is null)
        {
            _logger.LogError("Maxio returned an empty subscription on create for '{Reference}'.", userName);
            throw new BillingException(
                (int)HttpStatusCode.BadGateway,
                "The billing provider returned an incomplete subscription record.");
        }

        return Map(subscription);
    }

    private async Task<Customer> FindOrCreateCustomerAsync(string userName, CancellationToken cancellationToken)
    {
        var existing = await FindCustomerByReferenceAsync(userName, cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        var localPart = userName.Split('@')[0];
        try
        {
            var created = await Bounded(
                ct => _client.Customers.CreateCustomer(
                    new CreateCustomerRequest
                    {
                        Customer = new CreateCustomer
                        {
                            FirstName = localPart,
                            LastName = "Customer",
                            Email = userName,
                            Reference = userName
                        }
                    },
                    ct: ct),
                cancellationToken);
            return created.Customer;
        }
        catch (SdkException<CreateCustomerError> ex)
        {
            // A 422 here typically means a concurrent request won the race to
            // create this reference — re-run the lookup and use the winner.
            var winner = await FindCustomerByReferenceAsync(userName, cancellationToken);
            if (winner is not null)
            {
                return winner;
            }
            throw TranslateCreateCustomerError(ex);
        }
    }

    private async Task<Customer?> FindCustomerByReferenceAsync(string userName, CancellationToken cancellationToken)
    {
        try
        {
            var response = await Bounded(
                ct => _client.Customers.ReadCustomerByReference(userName, ct: ct),
                cancellationToken);
            return response.Customer;
        }
        catch (SdkException<RawError> ex) when (ex.Error.StatusCode == HttpStatusCode.NotFound)
        {
            // No billing customer yet — the normal find-or-create branch.
            return null;
        }
    }

    private async Task<CustomerSubscriptionDto?> FindActiveSubscriptionAsync(int customerId, string productHandle, CancellationToken cancellationToken)
    {
        var subscriptions = await ListSubscriptionsAsync(customerId, cancellationToken);
        return subscriptions.FirstOrDefault(s =>
            string.Equals(s.PlanHandle, productHandle, StringComparison.OrdinalIgnoreCase) &&
            (s.State is null || !TerminalStates.Contains(s.State)));
    }

    private async Task<IReadOnlyList<CustomerSubscriptionDto>> ListSubscriptionsAsync(int customerId, CancellationToken cancellationToken)
    {
        var responses = await Bounded(
            ct => _client.Customers.ListCustomerSubscriptions(customerId, ct: ct),
            cancellationToken);

        return responses
            .Select(r => r.Subscription)
            .Where(s => s is not null)
            .Select(s => Map(s!))
            .ToList();
    }

    private async Task<CustomerSubscriptionDto?> TryReconcileSubscriptionAsync(string userName, string productHandle)
    {
        try
        {
            // The caller may already be gone, so reconcile on a fresh budget.
            var customer = await FindCustomerByReferenceAsync(userName, CancellationToken.None);
            if (customer?.Id is null)
            {
                return null;
            }
            return await FindActiveSubscriptionAsync(customer.Id.Value, productHandle, CancellationToken.None);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not reconcile subscription state with Maxio after a transport failure.");
            return null;
        }
    }

    private static CustomerSubscriptionDto Map(Subscription subscription) => new()
    {
        SubscriptionId = subscription.Id,
        PlanName = subscription.Product?.Name,
        PlanHandle = subscription.Product?.Handle,
        PriceInCents = subscription.ProductPriceInCents ?? subscription.Product?.PriceInCents,
        State = subscription.State?.Value,
        NextBillingDate = subscription.CurrentPeriodEndsAt
    };

    private static async Task<T> Bounded<T>(Func<CancellationToken, Task<T>> call, CancellationToken cancellationToken)
    {
        // The only bound that caps a whole call (retries included) is a token;
        // the SDK and HttpClient timeouts are per attempt.
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(CallBudget);
        return await call(cts.Token);
    }

    private static bool IsTransportFailure(Exception ex, CancellationToken cancellationToken) =>
        (ex is HttpRequestException or TaskCanceledException) && !cancellationToken.IsCancellationRequested;

    private BillingException TranslateListProductsError(SdkException<ListProductsForProductFamilyError> ex)
    {
        if (ex.Error.TryGetString(out var notFoundMessage))
        {
            // 404: the configured product family handle does not resolve — broken
            // server-side configuration, not an empty catalog.
            _logger.LogError(
                "Maxio product family '{Handle}' was not found: {Message}",
                _options.ProductFamilyHandle,
                notFoundMessage);
            return new BillingException(
                (int)HttpStatusCode.InternalServerError,
                "The subscription plan catalog is not configured correctly.");
        }
        if (ex.Error.TryGetRawError(out var raw))
        {
            return TranslateRawError(raw);
        }
        return new BillingException(
            (int)HttpStatusCode.BadGateway,
            "The billing provider rejected the request.");
    }

    private BillingException TranslateCreateCustomerError(SdkException<CreateCustomerError> ex)
    {
        if (ex.Error.TryGetCustomerErrorResponse1(out var customerError))
        {
            _logger.LogWarning("Maxio rejected customer creation (422): {Error}", customerError);
            return new BillingException(
                (int)HttpStatusCode.UnprocessableEntity,
                "The billing provider rejected the customer record.");
        }
        if (ex.Error.TryGetRawError(out var raw))
        {
            return TranslateRawError(raw);
        }
        return new BillingException(
            (int)HttpStatusCode.BadGateway,
            "The billing provider rejected the request.");
    }

    private BillingException TranslateCreateSubscriptionError(SdkException<CreateSubscriptionError> ex)
    {
        if (ex.Error.TryGetErrorListResponse1(out var errorList))
        {
            // 422 validation failure, e.g. an unknown product handle.
            var detail = errorList.Errors is { Count: > 0 }
                ? string.Join("; ", errorList.Errors)
                : "validation failed";
            return new BillingException(
                (int)HttpStatusCode.UnprocessableEntity,
                $"The billing provider rejected the subscription: {detail}");
        }
        if (ex.Error.TryGetRawError(out var raw))
        {
            return TranslateRawError(raw);
        }
        return new BillingException(
            (int)HttpStatusCode.BadGateway,
            "The billing provider rejected the request.");
    }

    private BillingException TranslateRawError(RawError raw)
    {
        var status = (int)raw.StatusCode;
        if (status >= 400 && status < 500)
        {
            _logger.LogWarning("Maxio rejected the request: HTTP {Status} {Body}", status, raw.ReadAsString());
            return new BillingException(
                status,
                $"The billing provider rejected the request (HTTP {status}).");
        }

        _logger.LogError("Maxio provider error: HTTP {Status} {Body}", status, raw.ReadAsString());
        return new BillingException(
            (int)HttpStatusCode.BadGateway,
            "The billing provider is unavailable.");
    }

    private BillingException UnprocessableProviderResponse(JsonException ex)
    {
        _logger.LogError(ex, "Maxio returned a response that could not be deserialized.");
        return new BillingException(
            (int)HttpStatusCode.BadGateway,
            "The billing provider returned a response that could not be processed.",
            ex);
    }

    private BillingException ProviderUnreachable(Exception ex)
    {
        _logger.LogError(ex, "Maxio could not be reached.");
        return new BillingException(
            (int)HttpStatusCode.BadGateway,
            "The billing provider could not be reached. Please retry.",
            ex);
    }
}
