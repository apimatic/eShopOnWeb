using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.ErrorResponse;
using MaxioAdvancedBilling.Core.Exceptions;
using MaxioAdvancedBilling.Errors;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public sealed class SubscriptionBillingService
{
    private const int DefaultPageSize = 100;
    private const int RequestBudgetSeconds = 30;
    private readonly MaxioAdvancedBillingClient _client;
    private readonly MaxioOptions _settings;
    private readonly ILogger<SubscriptionBillingService> _logger;
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _enrollmentGates = new(StringComparer.Ordinal);

    public SubscriptionBillingService(
        MaxioAdvancedBillingClient client,
        IOptions<MaxioOptions> settings,
        ILogger<SubscriptionBillingService> logger)
    {
        _client = client;
        _settings = settings.Value;
        _logger = logger;
    }

    public async Task<IReadOnlyList<SubscriptionPlanDto>> ListPlansAsync(CancellationToken ct)
    {
        EnsureConfiguration();
        var plans = new List<SubscriptionPlanDto>();
        var page = 1;

        while (true)
        {
            var products = await ExecuteAsync(token => _client.Products.ListProducts(
                dateField: null,
                filter: null,
                endDate: null,
                endDatetime: null,
                startDate: null,
                startDatetime: null,
                includeArchived: false,
                include: null,
                page: page,
                perPage: DefaultPageSize,
                ct: token), ct, "Could not load subscription plans.");

            foreach (var item in products)
            {
                var product = item.Product;
                if (product?.ProductFamily?.Handle != _settings.ProductFamilyHandle ||
                    string.IsNullOrWhiteSpace(product.Handle) ||
                    string.IsNullOrWhiteSpace(product.Name))
                {
                    continue;
                }

                plans.Add(new SubscriptionPlanDto
                {
                    ProductHandle = product.Handle,
                    Name = product.Name,
                    PriceInCents = product.PriceInCents,
                    Interval = product.Interval,
                    IntervalUnit = product.IntervalUnit?.Value,
                    ProductPricePointHandle = product.ProductPricePointHandle,
                    ProductPricePointName = product.ProductPricePointName
                });
            }

            if (products.Count < DefaultPageSize)
            {
                break;
            }

            page++;
        }

        return plans;
    }

    public async Task<SubscriptionDto> SubscribeAsync(
        ClaimsPrincipal principal,
        CreateSubscriptionRequest request,
        CancellationToken ct)
    {
        EnsureConfiguration();
        var email = GetIdentityEmail(principal);
        var plans = await ListPlansAsync(ct);
        var plan = plans.FirstOrDefault(x =>
            string.Equals(x.ProductHandle, request.ProductHandle, StringComparison.Ordinal) &&
            (request.ProductPricePointHandle == null ||
             string.Equals(x.ProductPricePointHandle, request.ProductPricePointHandle, StringComparison.Ordinal)));

        if (plan == null)
        {
            throw new SubscriptionBillingException((int)HttpStatusCode.NotFound, "The requested subscription plan was not found.");
        }

        var customerReference = BuildCustomerReference(email);
        var subscriptionReference = BuildSubscriptionReference(customerReference, plan.ProductHandle, plan.ProductPricePointHandle);
        var gate = _enrollmentGates.GetOrAdd(subscriptionReference, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(ct);
        try
        {
            var customer = await EnsureCustomerAsync(email, customerReference, ct);
            var existing = await FindSubscriptionAsync(subscriptionReference, ct);
            if (existing?.Subscription != null)
            {
                return MapSubscription(existing.Subscription, plan);
            }

            var body = new MaxioAdvancedBilling.Models.CreateSubscriptionRequest
            {
                Subscription = new MaxioAdvancedBilling.Models.CreateSubscription
                {
                    CustomerReference = customerReference,
                    ProductHandle = plan.ProductHandle,
                    ProductPricePointHandle = plan.ProductPricePointHandle,
                    Reference = subscriptionReference
                }
            };

            MaxioAdvancedBilling.Models.SubscriptionResponse created;
            using var writeScope = MaxioWriteSendScope.Begin("subscription:" + subscriptionReference);

            async Task<MaxioAdvancedBilling.Models.SubscriptionResponse> CreateSubscriptionAttemptAsync(
                MaxioAdvancedBilling.Models.CreateSubscriptionRequest attemptBody,
                bool allowCardlessFallback)
            {
                try
                {
                    return await ExecuteAsync(token => _client.Subscriptions.CreateSubscription(attemptBody, ct: token), ct, "Could not create the subscription.");
                }
                catch (SubscriptionBillingException ex) when (ex.StatusCode is 502 or 504)
                {
                    var reconciled = await TryFindSubscriptionAfterUnknownOutcomeAsync(subscriptionReference, ct);
                    if (reconciled?.Subscription != null)
                    {
                        return reconciled;
                    }

                    throw;
                }
                catch (SdkException<CreateSubscriptionError> ex)
                {
                    if (ex.Error.TryGetErrorListResponse1(out var validation))
                    {
                        _logger.LogWarning("Maxio rejected subscription {SubscriptionReference}: {ValidationErrors}",
                            subscriptionReference,
                            validation.Errors == null ? "unspecified validation error" : string.Join("; ", validation.Errors));

                        if (allowCardlessFallback && validation.Errors is { } validationErrors && validationErrors.Any(error =>
                                error.Contains("No payment method was on file", StringComparison.Ordinal)))
                        {
                            var cardlessBody = attemptBody with
                            {
                                Subscription = attemptBody.Subscription with
                                {
                                    NextBillingAt = CalculateNextBillingAt(plan)
                                }
                            };

                            using var fallbackWriteScope = MaxioWriteSendScope.Begin("subscription:" + subscriptionReference);
                            return await CreateSubscriptionAttemptAsync(cardlessBody, allowCardlessFallback: false);
                        }

                        var reconciled = await TryFindSubscriptionAfterUnknownOutcomeAsync(subscriptionReference, ct);
                        if (reconciled?.Subscription != null)
                        {
                            return reconciled;
                        }

                        throw new SubscriptionBillingException((int)HttpStatusCode.UnprocessableEntity, "Maxio rejected the subscription.", ex);
                    }

                    if (ex.Error.TryGetRawError(out var raw))
                    {
                        throw ProviderError(raw.StatusCode, "Could not create the subscription.", ex);
                    }

                    throw new SubscriptionBillingException((int)HttpStatusCode.BadGateway, "Maxio returned an unrecognized subscription error.", ex);
                }
            }

            created = await CreateSubscriptionAttemptAsync(body, allowCardlessFallback: true);

            if (created.Subscription == null)
            {
                throw new SubscriptionBillingException((int)HttpStatusCode.BadGateway, "Maxio returned an incomplete subscription response.");
            }

            _logger.LogInformation("Maxio subscription {SubscriptionReference} created for customer {CustomerReference}.", subscriptionReference, customer.Customer.Reference);
            return MapSubscription(created.Subscription, plan);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<IReadOnlyList<SubscriptionDto>> ListMySubscriptionsAsync(ClaimsPrincipal principal, CancellationToken ct)
    {
        EnsureConfiguration();
        var email = GetIdentityEmail(principal);
        var customerReference = BuildCustomerReference(email);
        var customerResponse = await FindCustomerAsync(customerReference, ct);
        if (customerResponse?.Customer?.Id is not int customerId)
        {
            return Array.Empty<SubscriptionDto>();
        }

        var subscriptions = await ExecuteAsync(token => _client.Customers.ListCustomerSubscriptions(customerId, ct: token), ct, "Could not load your subscriptions.");
        return subscriptions
            .Where(x => x.Subscription != null)
            .Select(x => MapSubscription(x.Subscription!, null))
            .ToArray();
    }

    private async Task<MaxioAdvancedBilling.Models.CustomerResponse> EnsureCustomerAsync(
        string email,
        string reference,
        CancellationToken ct)
    {
        var existing = await FindCustomerAsync(reference, ct);
        if (existing?.Customer != null)
        {
            return existing;
        }

        var localPart = email.Split('@')[0];
        var body = new MaxioAdvancedBilling.Models.CreateCustomerRequest
        {
            Customer = new MaxioAdvancedBilling.Models.CreateCustomer
            {
                FirstName = "eShopOnWeb",
                LastName = string.IsNullOrWhiteSpace(localPart) ? "Customer" : localPart,
                Email = email,
                Reference = reference
            }
        };

        try
        {
            using var writeScope = MaxioWriteSendScope.Begin("customer:" + reference);
            var created = await ExecuteAsync(token => _client.Customers.CreateCustomer(body, ct: token), ct, "Could not create your billing customer.");
            if (created.Customer == null)
            {
                throw new SubscriptionBillingException((int)HttpStatusCode.BadGateway, "Maxio returned an incomplete customer response.");
            }

            return created;
        }
        catch (SubscriptionBillingException ex) when (ex.StatusCode is 502 or 504)
        {
            var reconciled = await TryFindCustomerAfterUnknownOutcomeAsync(reference, ct);
            if (reconciled?.Customer != null)
            {
                return reconciled;
            }

            throw;
        }
        catch (SdkException<CreateCustomerError> ex)
        {
            if (ex.Error.TryGetCustomerErrorResponse1(out _))
            {
                var reconciled = await FindCustomerAsync(reference, ct);
                if (reconciled?.Customer != null)
                {
                    return reconciled;
                }

                throw new SubscriptionBillingException((int)HttpStatusCode.UnprocessableEntity, "Maxio rejected the billing customer.", ex);
            }

            if (ex.Error.TryGetRawError(out var raw))
            {
                throw ProviderError(raw.StatusCode, "Could not create your billing customer.", ex);
            }

            throw new SubscriptionBillingException((int)HttpStatusCode.BadGateway, "Maxio returned an unrecognized customer error.", ex);
        }
    }

    private async Task<MaxioAdvancedBilling.Models.CustomerResponse?> FindCustomerAsync(string reference, CancellationToken ct)
    {
        try
        {
            return await ExecuteAsync(token => _client.Customers.ReadCustomerByReference(reference, ct: token), ct, "Could not look up your billing customer.");
        }
        catch (SubscriptionBillingException ex) when (ex.StatusCode == (int)HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    private async Task<MaxioAdvancedBilling.Models.CustomerResponse?> TryFindCustomerAfterUnknownOutcomeAsync(string reference, CancellationToken ct)
    {
        try
        {
            return await FindCustomerAsync(reference, ct);
        }
        catch (SubscriptionBillingException ex)
        {
            _logger.LogWarning(ex, "Could not reconcile customer {CustomerReference} after a provider failure.", reference);
            return null;
        }
    }

    private async Task<MaxioAdvancedBilling.Models.SubscriptionResponse?> FindSubscriptionAsync(string reference, CancellationToken ct)
    {
        try
        {
            return await ExecuteAsync(token => _client.Subscriptions.FindSubscription(reference: reference, ct: token), ct, "Could not look up your subscription.");
        }
        catch (SubscriptionBillingException ex) when (ex.StatusCode == (int)HttpStatusCode.NotFound)
        {
            return null;
        }
        catch (SdkException<FindSubscriptionError> ex)
        {
            if (ex.Error.TryGetNoContent(out _))
            {
                return null;
            }

            if (ex.Error.TryGetRawError(out var raw))
            {
                throw ProviderError(raw.StatusCode, "Could not look up your subscription.", ex);
            }

            throw new SubscriptionBillingException((int)HttpStatusCode.BadGateway, "Maxio returned an unrecognized subscription lookup error.", ex);
        }
    }

    private async Task<MaxioAdvancedBilling.Models.SubscriptionResponse?> TryFindSubscriptionAfterUnknownOutcomeAsync(string reference, CancellationToken ct)
    {
        try
        {
            return await FindSubscriptionAsync(reference, ct);
        }
        catch (SubscriptionBillingException ex)
        {
            _logger.LogWarning(ex, "Could not reconcile subscription {SubscriptionReference} after a provider failure.", reference);
            return null;
        }
    }

    private async Task<T> ExecuteAsync<T>(Func<CancellationToken, Task<T>> operation, CancellationToken requestToken, string failureMessage)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(requestToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(RequestBudgetSeconds));
        try
        {
            return await operation(timeout.Token);
        }
        catch (SdkException<RawError> ex)
        {
            throw ProviderError(ex.Error.StatusCode, failureMessage, ex);
        }
        catch (HttpRequestException ex)
        {
            throw new SubscriptionBillingException((int)HttpStatusCode.BadGateway, "The billing service is temporarily unavailable.", ex);
        }
        catch (TaskCanceledException ex)
        {
            throw new SubscriptionBillingException((int)HttpStatusCode.GatewayTimeout, "The billing service did not respond in time.", ex);
        }
        catch (MaxioWriteReplayException ex)
        {
            throw new SubscriptionBillingException((int)HttpStatusCode.BadGateway, "The billing write outcome is being reconciled.", ex);
        }
        catch (JsonException ex)
        {
            throw new SubscriptionBillingException((int)HttpStatusCode.BadGateway, "The billing service returned an unreadable response.", ex);
        }
    }

    private static SubscriptionBillingException ProviderError(HttpStatusCode statusCode, string message, Exception inner)
    {
        var numericStatus = (int)statusCode;
        if (numericStatus < 400 || numericStatus > 599)
        {
            numericStatus = (int)HttpStatusCode.BadGateway;
        }

        return new SubscriptionBillingException(numericStatus, message, inner);
    }

    private static DateTimeOffset CalculateNextBillingAt(SubscriptionPlanDto plan)
    {
        if (plan.Interval is not int interval || interval <= 0)
        {
            throw new SubscriptionBillingException((int)HttpStatusCode.BadGateway, "Maxio returned an invalid subscription plan interval.");
        }

        return plan.IntervalUnit switch
        {
            "day" => DateTimeOffset.UtcNow.AddDays(interval),
            "month" => DateTimeOffset.UtcNow.AddMonths(interval),
            _ => throw new SubscriptionBillingException((int)HttpStatusCode.BadGateway, "Maxio returned an invalid subscription plan interval unit.")
        };
    }

    private static SubscriptionDto MapSubscription(MaxioAdvancedBilling.Models.Subscription subscription, SubscriptionPlanDto? fallback)
    {
        var product = subscription.Product;
        return new SubscriptionDto
        {
            SubscriptionId = subscription.Id,
            Reference = subscription.Reference,
            ProductHandle = product?.Handle ?? fallback?.ProductHandle ?? string.Empty,
            PlanName = product?.Name ?? fallback?.Name ?? string.Empty,
            PriceInCents = subscription.ProductPriceInCents ?? product?.PriceInCents ?? fallback?.PriceInCents,
            State = subscription.State?.Value,
            NextBillingDate = subscription.CurrentPeriodEndsAt,
            ProductPricePointHandle = product?.ProductPricePointHandle ?? fallback?.ProductPricePointHandle,
            ProductPricePointName = product?.ProductPricePointName ?? fallback?.ProductPricePointName
        };
    }

    private void EnsureConfiguration()
    {
        if (string.IsNullOrWhiteSpace(_settings.ApiKey) ||
            string.IsNullOrWhiteSpace(_settings.Subdomain) ||
            string.IsNullOrWhiteSpace(_settings.ProductFamilyHandle))
        {
            throw new SubscriptionBillingException((int)HttpStatusCode.InternalServerError, "Subscription billing is not configured.");
        }
    }

    private static string GetIdentityEmail(ClaimsPrincipal principal)
    {
        var email = principal.FindFirstValue(ClaimTypes.Email) ??
                    principal.FindFirstValue(ClaimTypes.Name) ??
                    principal.FindFirstValue("email");
        if (string.IsNullOrWhiteSpace(email))
        {
            throw new SubscriptionBillingException((int)HttpStatusCode.Unauthorized, "The authenticated identity is missing.");
        }

        return email.Trim().ToLowerInvariant();
    }

    private static string BuildCustomerReference(string email)
    {
        return "eshop-user-" + Hash(email);
    }

    private static string BuildSubscriptionReference(string customerReference, string productHandle, string? pricePointHandle)
    {
        return "eshop-subscription-" + Hash(customerReference + ":" + productHandle + ":" + pricePointHandle);
    }

    private static string Hash(string value)
    {
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
    }
}
