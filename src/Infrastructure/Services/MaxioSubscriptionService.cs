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
using Microsoft.eShopWeb.ApplicationCore.Billing;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Services;

/// <summary>
/// Implements <see cref="IMaxioSubscriptionService"/> against the Maxio Advanced Billing SDK.
/// Maxio is the system of record: nothing here is cached locally, so the idempotency guards below
/// (customer-by-reference, subscription-by-reference) are re-checked against Maxio on every call
/// rather than relying on ephemeral local state.
/// </summary>
public class MaxioSubscriptionService : IMaxioSubscriptionService
{
    private const int PlansPerPage = 20;
    private static readonly TimeSpan CallBudget = TimeSpan.FromSeconds(30);

    private readonly MaxioAdvancedBillingClient _client;
    private readonly MaxioSettings _settings;
    private readonly ILogger<MaxioSubscriptionService> _logger;

    public MaxioSubscriptionService(MaxioAdvancedBillingClient client, IOptions<MaxioSettings> settings, ILogger<MaxioSubscriptionService> logger)
    {
        _client = client;
        _settings = settings.Value;
        _logger = logger;
    }

    public async Task<IReadOnlyList<SubscriptionPlan>> ListPlansAsync(CancellationToken ct = default)
    {
        try
        {
            var familyId = await ResolveProductFamilyIdAsync(ct);
            var plans = new List<SubscriptionPlan>();
            var page = 1;

            while (true)
            {
                IReadOnlyList<ProductResponse> pageItems;
                try
                {
                    pageItems = await Bounded(
                        ct2 => _client.ProductFamilies.ListProductsForProductFamily(
                            productFamilyId: familyId,
                            dateField: null,
                            filter: null,
                            startDate: null,
                            endDate: null,
                            startDatetime: null,
                            endDatetime: null,
                            includeArchived: false,
                            include: null,
                            page: page,
                            perPage: PlansPerPage,
                            ct: ct2),
                        ct);
                }
                catch (SdkException<ListProductsForProductFamilyError> ex)
                {
                    if (ex.Error.TryGetString(out var notFoundMessage))
                    {
                        throw new MaxioIntegrationException(notFoundMessage ?? "The configured product family could not be found.", HttpStatusCode.NotFound);
                    }
                    if (ex.Error.TryGetRawError(out var raw))
                    {
                        throw ToIntegrationException(raw);
                    }
                    throw new MaxioIntegrationException("The billing provider rejected the plan list request.");
                }

                foreach (var item in pageItems)
                {
                    var product = item.Product;
                    plans.Add(new SubscriptionPlan
                    {
                        Handle = product.Handle ?? string.Empty,
                        Name = product.Name ?? string.Empty,
                        PriceInCents = product.PriceInCents ?? 0,
                        Interval = product.Interval ?? 0,
                        IntervalUnit = product.IntervalUnit?.Value ?? string.Empty,
                        Taxable = product.Taxable ?? false,
                        RequiresPaymentMethod = product.RequireCreditCard ?? false
                    });
                }

                if (pageItems.Count < PlansPerPage) break;
                page++;
            }

            return plans;
        }
        catch (MaxioIntegrationException)
        {
            throw;
        }
        catch (Exception ex) when (IsTransportOrParseFailure(ex))
        {
            throw ToIntegrationException(ex);
        }
    }

    public async Task<CustomerSubscription> SubscribeAsync(SubscribingCustomer customer, string planHandle, CancellationToken ct = default)
    {
        try
        {
            var maxioCustomer = await EnsureCustomerAsync(customer, ct);
            if (maxioCustomer.Id is null)
            {
                throw new MaxioIntegrationException("The billing provider did not return a customer id.");
            }

            var idempotencyReference = BuildSubscriptionReference(customer.UserId, planHandle);

            var existingSubscription = await TryFindSubscriptionByReferenceAsync(idempotencyReference, ct);
            if (existingSubscription is not null)
            {
                _logger.LogInformation("Subscription already exists for reference {Reference}; returning existing enrollment.", idempotencyReference);
                return ToCustomerSubscription(existingSubscription);
            }

            var body = new CreateSubscriptionRequest
            {
                Subscription = new CreateSubscription
                {
                    ProductHandle = planHandle,
                    CustomerId = maxioCustomer.Id,
                    Reference = idempotencyReference,
                    // The plans this integration targets don't require a payment method (see plan
                    // Product.RequireCreditCard), but the site's default collection method is
                    // Automatic, which demands one anyway. Remittance defers collection instead of
                    // requiring a payment profile up front.
                    PaymentCollectionMethod = CollectionMethod.Remittance
                }
            };

            SubscriptionResponse response;
            try
            {
                response = await Bounded(ct2 => _client.Subscriptions.CreateSubscription(body, ct2), ct);
            }
            catch (SdkException<CreateSubscriptionError> ex)
            {
                if (ex.Error.TryGetErrorListResponse1(out var errorList))
                {
                    var message = errorList.Errors is { Count: > 0 }
                        ? string.Join("; ", errorList.Errors)
                        : "Subscription creation was rejected.";
                    throw new MaxioIntegrationException(message, HttpStatusCode.UnprocessableEntity);
                }
                if (ex.Error.TryGetRawError(out var raw))
                {
                    throw ToIntegrationException(raw);
                }
                throw new MaxioIntegrationException("The billing provider rejected subscription creation.");
            }

            if (response.Subscription is null)
            {
                // A concurrent duplicate request may have created it first (reference-conflict race) - recover.
                var recovered = await TryFindSubscriptionByReferenceAsync(idempotencyReference, ct);
                if (recovered is not null)
                {
                    return ToCustomerSubscription(recovered);
                }

                throw new MaxioIntegrationException("The billing provider did not return the created subscription.");
            }

            return ToCustomerSubscription(response.Subscription);
        }
        catch (MaxioIntegrationException)
        {
            throw;
        }
        catch (Exception ex) when (IsTransportOrParseFailure(ex))
        {
            throw ToIntegrationException(ex);
        }
    }

    public async Task<IReadOnlyList<CustomerSubscription>> ListSubscriptionsForUserAsync(string userId, CancellationToken ct = default)
    {
        try
        {
            var customer = await TryReadCustomerByReferenceAsync(userId, ct);
            if (customer?.Id is null)
            {
                return Array.Empty<CustomerSubscription>();
            }

            IReadOnlyList<SubscriptionResponse> subscriptions;
            try
            {
                subscriptions = await Bounded(ct2 => _client.Customers.ListCustomerSubscriptions(customer.Id.Value, ct2), ct);
            }
            catch (SdkException<RawError> ex)
            {
                throw ToIntegrationException(ex.Error);
            }

            var result = new List<CustomerSubscription>();
            foreach (var item in subscriptions)
            {
                if (item.Subscription is null)
                {
                    _logger.LogWarning("Skipping a null subscription entry for customer {CustomerId}.", customer.Id);
                    continue;
                }

                result.Add(ToCustomerSubscription(item.Subscription));
            }

            return result;
        }
        catch (MaxioIntegrationException)
        {
            throw;
        }
        catch (Exception ex) when (IsTransportOrParseFailure(ex))
        {
            throw ToIntegrationException(ex);
        }
    }

    private async Task<string> ResolveProductFamilyIdAsync(CancellationToken ct)
    {
        IReadOnlyList<ProductFamilyResponse> families;
        try
        {
            families = await Bounded(
                ct2 => _client.ProductFamilies.ListProductFamilies(
                    dateField: null,
                    startDate: null,
                    endDate: null,
                    startDatetime: null,
                    endDatetime: null,
                    ct: ct2),
                ct);
        }
        catch (SdkException<RawError> ex)
        {
            throw ToIntegrationException(ex.Error);
        }

        var match = families
            .Select(f => f.ProductFamily)
            .FirstOrDefault(f => f is not null && string.Equals(f.Handle, _settings.ProductFamilyHandle, StringComparison.OrdinalIgnoreCase));

        if (match?.Id is null)
        {
            throw new MaxioIntegrationException($"No Maxio product family with handle '{_settings.ProductFamilyHandle}' was found.", HttpStatusCode.NotFound);
        }

        return match.Id.Value.ToString();
    }

    private async Task<Customer> EnsureCustomerAsync(SubscribingCustomer customer, CancellationToken ct)
    {
        var existing = await TryReadCustomerByReferenceAsync(customer.UserId, ct);
        if (existing is not null)
        {
            return existing;
        }

        var body = new CreateCustomerRequest
        {
            Customer = new CreateCustomer
            {
                FirstName = customer.FirstName,
                LastName = customer.LastName,
                Email = customer.Email,
                Reference = customer.UserId
            }
        };

        try
        {
            var response = await Bounded(ct2 => _client.Customers.CreateCustomer(body, ct2), ct);
            return response.Customer;
        }
        catch (SdkException<CreateCustomerError> ex)
        {
            // CustomerErrorResponse1's typed body shape isn't reliably a reference-conflict message -
            // treat ANY 422 here as a possible race with a concurrent duplicate request and recover
            // unconditionally by re-reading the winning customer.
            if (ex.Error.TryGetCustomerErrorResponse1(out _))
            {
                _logger.LogWarning("CreateCustomer returned a 422 for reference {Reference}; recovering via re-lookup.", customer.UserId);
                var winner = await TryReadCustomerByReferenceAsync(customer.UserId, ct);
                if (winner is not null)
                {
                    return winner;
                }
            }
            else if (ex.Error.TryGetRawError(out var raw))
            {
                throw ToIntegrationException(raw);
            }

            throw new MaxioIntegrationException("The billing provider rejected customer creation.", HttpStatusCode.UnprocessableEntity);
        }
    }

    private async Task<Customer?> TryReadCustomerByReferenceAsync(string reference, CancellationToken ct)
    {
        try
        {
            var response = await Bounded(ct2 => _client.Customers.ReadCustomerByReference(reference, ct2), ct);
            return response.Customer;
        }
        catch (SdkException<RawError> ex)
        {
            if (ex.Error.StatusCode == HttpStatusCode.NotFound)
            {
                return null;
            }
            throw ToIntegrationException(ex.Error);
        }
    }

    private async Task<Subscription?> TryFindSubscriptionByReferenceAsync(string reference, CancellationToken ct)
    {
        try
        {
            var response = await Bounded(ct2 => _client.Subscriptions.FindSubscription(reference, ct2), ct);
            return response.Subscription;
        }
        catch (SdkException<FindSubscriptionError> ex)
        {
            if (ex.Error.TryGetNoContent(out _))
            {
                return null;
            }
            if (ex.Error.TryGetRawError(out var raw))
            {
                throw ToIntegrationException(raw);
            }
            throw new MaxioIntegrationException("The billing provider rejected the subscription lookup.");
        }
    }

    private static string BuildSubscriptionReference(string userId, string planHandle) => $"{userId}:{planHandle}";

    private static CustomerSubscription ToCustomerSubscription(Subscription subscription)
    {
        return new CustomerSubscription
        {
            MaxioSubscriptionId = subscription.Id ?? 0,
            PlanHandle = subscription.Product?.Handle ?? string.Empty,
            PlanName = subscription.Product?.Name,
            PriceInCents = subscription.Product?.PriceInCents,
            State = subscription.State?.Value ?? "unknown",
            NextAssessmentAt = subscription.NextAssessmentAt,
            CurrentPeriodEndsAt = subscription.CurrentPeriodEndsAt
        };
    }

    private async Task<T> Bounded<T>(Func<CancellationToken, Task<T>> call, CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(CallBudget);
        return await call(cts.Token);
    }

    private static bool IsTransportOrParseFailure(Exception ex) =>
        ex is JsonException or HttpRequestException or TaskCanceledException;

    private static MaxioIntegrationException ToIntegrationException(RawError raw)
    {
        var isClientError = (int)raw.StatusCode is >= 400 and < 500;
        var message = isClientError
            ? "The billing provider rejected the request."
            : "The billing provider is currently unavailable.";
        return new MaxioIntegrationException(message, raw.StatusCode);
    }

    private static MaxioIntegrationException ToIntegrationException(Exception ex) => ex switch
    {
        JsonException => new MaxioIntegrationException("The billing provider returned a response that could not be processed.", ex),
        _ => new MaxioIntegrationException("The billing provider is currently unreachable.", ex)
    };
}
