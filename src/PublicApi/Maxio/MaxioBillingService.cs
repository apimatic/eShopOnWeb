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
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

/// <summary>
/// Fronts the Maxio SDK for the subscription-billing capability. Owns idempotency
/// (deterministic references + lookup-before-create + 422-then-re-read), the per-call
/// time budget, and translation of every SDK/transport failure into
/// <see cref="MaxioBillingException"/> with a caller-safe message.
/// </summary>
public class MaxioBillingService : IMaxioBillingService
{
    private static readonly TimeSpan CallBudget = TimeSpan.FromSeconds(30);

    private readonly MaxioAdvancedBillingClient _client;
    private readonly MaxioOptions _options;
    private readonly ILogger<MaxioBillingService> _logger;

    public MaxioBillingService(
        MaxioAdvancedBillingClient client,
        IOptions<MaxioOptions> options,
        ILogger<MaxioBillingService> logger)
    {
        _client = client;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<IReadOnlyList<SubscriptionPlanDto>> ListPlansAsync(CancellationToken cancellationToken = default)
    {
        return await Bounded(async ct =>
        {
            int familyId = await GetFamilyIdAsync(ct);

            try
            {
                var products = await _client.ProductFamilies.ListProductsForProductFamily(
                    productFamilyId: familyId.ToString(),
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
                    ct: ct);

                return (IReadOnlyList<SubscriptionPlanDto>)products
                    .Where(p => p.Product is not null && p.Product.ArchivedAt is null)
                    .Select(p => new SubscriptionPlanDto
                    {
                        Id = p.Product.Id,
                        Handle = p.Product.Handle,
                        Name = p.Product.Name,
                        Description = p.Product.Description,
                        PriceInCents = p.Product.PriceInCents,
                        Interval = p.Product.Interval,
                        IntervalUnit = p.Product.IntervalUnit?.Value
                    })
                    .ToList();
            }
            catch (SdkException<ListProductsForProductFamilyError> ex)
            {
                throw TranslateListProductsError(ex);
            }
        }, cancellationToken);
    }

    public async Task<SubscriptionDto> SubscribeAsync(ShopperIdentity shopper, string productHandle, CancellationToken cancellationToken = default)
    {
        return await Bounded(async ct =>
        {
            string customerReference = CustomerReference(shopper.UserId);
            string subscriptionReference = $"{customerReference}-{productHandle}";

            var customer = await FindOrCreateCustomerAsync(shopper, customerReference, ct);

            var existing = await FindSubscriptionAsync(subscriptionReference, ct);
            if (existing?.Subscription is not null)
            {
                return Map(existing.Subscription);
            }

            try
            {
                var created = await _client.Subscriptions.CreateSubscription(
                    body: new CreateSubscriptionRequest
                    {
                        Subscription = new CreateSubscription
                        {
                            ProductHandle = productHandle,
                            CustomerReference = customerReference,
                            Reference = subscriptionReference,
                            PaymentCollectionMethod = ResolveCollectionMethod()
                        }
                    },
                    ct: ct);

                if (created.Subscription is null)
                {
                    throw new MaxioBillingException(HttpStatusCode.BadGateway,
                        "The billing provider returned an empty subscription response.");
                }

                _logger.LogInformation("Created Maxio subscription {SubscriptionId} for customer {CustomerId} ({Reference})",
                    created.Subscription.Id, customer.Id, subscriptionReference);

                return Map(created.Subscription);
            }
            catch (SdkException<CreateSubscriptionError> ex)
            {
                // A concurrent subscribe with the same deterministic reference loses the race
                // with a 422 — re-read and return the winner's subscription.
                if (ex.Error.TryGetErrorListResponse1(out var errorList))
                {
                    var raced = await FindSubscriptionAsync(subscriptionReference, ct);
                    if (raced?.Subscription is not null)
                    {
                        return Map(raced.Subscription);
                    }

                    throw new MaxioBillingException(HttpStatusCode.UnprocessableEntity,
                        $"The billing provider rejected the subscription: {string.Join("; ", errorList.Errors)}", ex);
                }

                throw TranslateRaw(ex.Error.TryGetRawError(out var raw) ? raw : null,
                    "The billing provider rejected the subscription.", ex);
            }
        }, cancellationToken);
    }

    public async Task<IReadOnlyList<SubscriptionDto>> ListMySubscriptionsAsync(ShopperIdentity shopper, CancellationToken cancellationToken = default)
    {
        return await Bounded(async ct =>
        {
            var customer = await FindCustomerAsync(CustomerReference(shopper.UserId), ct);
            if (customer?.Id is null)
            {
                return (IReadOnlyList<SubscriptionDto>)Array.Empty<SubscriptionDto>();
            }

            try
            {
                var subscriptions = await _client.Customers.ListCustomerSubscriptions(customer.Id.Value, ct: ct);
                return subscriptions
                    .Where(s => s.Subscription is not null)
                    .Select(s => Map(s.Subscription!))
                    .ToList();
            }
            catch (SdkException<RawError> ex)
            {
                throw TranslateRaw(ex.Error, "The billing provider could not list subscriptions.", ex);
            }
        }, cancellationToken);
    }

    private async Task<int> GetFamilyIdAsync(CancellationToken ct)
    {
        try
        {
            var families = await _client.ProductFamilies.ListProductFamilies(
                dateField: null,
                startDate: null,
                endDate: null,
                startDatetime: null,
                endDatetime: null,
                ct: ct);

            var family = families.FirstOrDefault(f =>
                string.Equals(f.ProductFamily?.Handle, _options.ProductFamilyHandle, StringComparison.OrdinalIgnoreCase));

            if (family?.ProductFamily?.Id is null)
            {
                throw new MaxioBillingException(HttpStatusCode.BadGateway,
                    $"The configured product family '{_options.ProductFamilyHandle}' was not found at the billing provider.");
            }

            return family.ProductFamily.Id.Value;
        }
        catch (SdkException<RawError> ex)
        {
            throw TranslateRaw(ex.Error, "The billing provider could not list product families.", ex);
        }
    }

    private async Task<Customer?> FindCustomerAsync(string reference, CancellationToken ct)
    {
        try
        {
            var response = await _client.Customers.ReadCustomerByReference(reference, ct: ct);
            return response.Customer;
        }
        catch (SdkException<RawError> ex) when (ex.Error.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
        catch (SdkException<RawError> ex)
        {
            throw TranslateRaw(ex.Error, "The billing provider could not read the customer.", ex);
        }
    }

    private async Task<Customer> FindOrCreateCustomerAsync(ShopperIdentity shopper, string reference, CancellationToken ct)
    {
        var existing = await FindCustomerAsync(reference, ct);
        if (existing is not null)
        {
            return existing;
        }

        try
        {
            var created = await _client.Customers.CreateCustomer(
                body: new CreateCustomerRequest
                {
                    Customer = new CreateCustomer
                    {
                        FirstName = shopper.FirstName,
                        LastName = shopper.LastName,
                        Email = shopper.Email,
                        Reference = reference
                    }
                },
                ct: ct);

            _logger.LogInformation("Created Maxio customer {CustomerId} ({Reference})", created.Customer.Id, reference);
            return created.Customer;
        }
        catch (SdkException<CreateCustomerError> ex)
        {
            // reference is unique per customer: a 422 here can be a lost create race —
            // re-read before reporting failure. The typed 422 payload's shape is unverified,
            // so the status drives the handling and the detail stays in the logs.
            if (ex.Error.TryGetCustomerErrorResponse1(out _))
            {
                var raced = await FindCustomerAsync(reference, ct);
                if (raced is not null)
                {
                    return raced;
                }

                _logger.LogWarning("Maxio CreateCustomer rejected with 422 for reference {Reference}: {Error}", reference, ex.ToString());
                throw new MaxioBillingException(HttpStatusCode.UnprocessableEntity,
                    "The billing provider rejected the customer details.", ex);
            }

            throw TranslateRaw(ex.Error.TryGetRawError(out var raw) ? raw : null,
                "The billing provider rejected the customer details.", ex);
        }
    }

    private async Task<SubscriptionResponse?> FindSubscriptionAsync(string reference, CancellationToken ct)
    {
        try
        {
            return await _client.Subscriptions.FindSubscription(reference: reference, ct: ct);
        }
        catch (SdkException<FindSubscriptionError> ex)
        {
            if (ex.Error.TryGetNoContent(out _))
            {
                return null;
            }

            throw TranslateRaw(ex.Error.TryGetRawError(out var raw) ? raw : null,
                "The billing provider could not look up the subscription.", ex);
        }
    }

    private static SubscriptionDto Map(Subscription subscription) => new()
    {
        Id = subscription.Id,
        State = subscription.State?.Value,
        ProductHandle = subscription.Product?.Handle,
        ProductName = subscription.Product?.Name,
        PriceInCents = subscription.ProductPriceInCents,
        PaymentCollectionMethod = subscription.PaymentCollectionMethod?.Value,
        CurrentPeriodEndsAt = subscription.CurrentPeriodEndsAt
    };

    // StringEnum constants mapped explicitly (FromValue is not guaranteed to be generated).
    // Default is invoice: signup without a card on file; automatic requires one for paid plans.
    private CollectionMethod ResolveCollectionMethod() =>
        _options.PaymentCollectionMethod?.Trim().ToLowerInvariant() switch
        {
            null or "" or "invoice" => CollectionMethod.Invoice,
            "automatic" => CollectionMethod.Automatic,
            "remittance" => CollectionMethod.Remittance,
            "prepaid" => CollectionMethod.Prepaid,
            var other => throw new MaxioBillingException(HttpStatusCode.InternalServerError,
                $"Maxio:PaymentCollectionMethod '{other}' is not one of automatic, remittance, prepaid, invoice.")
        };

    private static string CustomerReference(string userId) => $"eshop-user-{userId}";

    private async Task<T> Bounded<T>(Func<CancellationToken, Task<T>> call, CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(CallBudget);
        try
        {
            return await call(cts.Token);
        }
        catch (MaxioBillingException)
        {
            throw;
        }
        // A drifted/malformed provider body (2xx or error-path) surfaces as JsonException,
        // not SdkException. Never map it to a domain absence; report it as unprocessable.
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Maxio returned a response that could not be deserialized.");
            throw new MaxioBillingException(HttpStatusCode.BadGateway,
                "The billing provider returned a response that could not be processed.", ex);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested &&
                                   (ex is HttpRequestException or TaskCanceledException or OperationCanceledException))
        {
            _logger.LogError(ex, "Maxio call failed at transport level.");
            throw new MaxioBillingException(HttpStatusCode.ServiceUnavailable,
                "The billing provider is unreachable or did not respond in time.", ex);
        }
    }

    private MaxioBillingException TranslateRaw(RawError? raw, string safeMessage, Exception inner)
    {
        if (raw is null)
        {
            _logger.LogError("{Error}", inner.ToString());
            return new MaxioBillingException(HttpStatusCode.BadGateway, safeMessage, inner);
        }

        var status = raw.StatusCode;
        _logger.LogError("Maxio responded {Status}: {Body}", (int)status, raw.ReadAsString());

        // Carry provider 4xx through so callers can act on it; anything else is a provider failure.
        var mapped = (int)status is >= 400 and < 500 ? status : HttpStatusCode.BadGateway;
        return new MaxioBillingException(mapped, safeMessage, inner);
    }

    private MaxioBillingException TranslateListProductsError(SdkException<ListProductsForProductFamilyError> ex)
    {
        if (ex.Error.TryGetString(out var message))
        {
            _logger.LogError("Maxio ListProductsForProductFamily 404: {Message}", message);
            return new MaxioBillingException(HttpStatusCode.BadGateway,
                "The configured product family was not found at the billing provider.", ex);
        }

        return TranslateRaw(ex.Error.TryGetRawError(out var raw) ? raw : null,
            "The billing provider could not list subscription plans.", ex);
    }
}
