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
using Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MaxioCreateSubscriptionRequest = MaxioAdvancedBilling.Models.CreateSubscriptionRequest;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

/// <summary>
/// The integration boundary around the Maxio SDK. Every call is bounded by a
/// whole-call cancellation budget and every SDK/transport failure is translated
/// to <see cref="MaxioBillingException"/> — no SDK exception type escapes.
/// </summary>
public sealed class MaxioSubscriptionService : IMaxioSubscriptionService
{
    private static readonly TimeSpan CallBudget = TimeSpan.FromSeconds(30);

    private readonly MaxioAdvancedBillingClient _client;
    private readonly MaxioSettings _settings;
    private readonly ILogger<MaxioSubscriptionService> _logger;

    public MaxioSubscriptionService(
        MaxioAdvancedBillingClient client,
        IOptions<MaxioSettings> settings,
        ILogger<MaxioSubscriptionService> logger)
    {
        _client = client;
        _settings = settings.Value;
        _logger = logger;
    }

    public async Task<IReadOnlyList<SubscriptionPlanDto>> ListPlansAsync(CancellationToken cancellationToken)
    {
        EnsureConfigured();
        try
        {
            return await Bounded(async ct =>
            {
                var family = await FindProductFamilyAsync(ct);
                var products = await _client.ProductFamilies.ListProductsForProductFamily(
                    productFamilyId: family.Id!.Value.ToString(),
                    dateField: null,
                    filter: null,
                    startDate: null,
                    endDate: null,
                    startDatetime: null,
                    endDatetime: null,
                    includeArchived: false,
                    include: null,
                    ct: ct);

                return (IReadOnlyList<SubscriptionPlanDto>)products
                    .Where(p => p.Product is not null)
                    .Select(p => new SubscriptionPlanDto
                    {
                        Name = p.Product.Name,
                        Handle = p.Product.Handle,
                        PriceInCents = p.Product.PriceInCents,
                        Interval = p.Product.Interval,
                        IntervalUnit = p.Product.IntervalUnit?.Value
                    })
                    .ToList();
            }, cancellationToken);
        }
        catch (SdkException<RawError> ex)
        {
            throw Translate(ex.Error);
        }
        catch (SdkException<ListProductsForProductFamilyError> ex)
        {
            if (ex.Error.TryGetString(out var notFound))
            {
                throw new MaxioBillingException(HttpStatusCode.NotFound, $"Maxio: {notFound}", ex);
            }
            if (ex.Error.TryGetRawError(out var raw))
            {
                throw Translate(raw, ex);
            }
            throw new MaxioBillingException(HttpStatusCode.BadGateway, "Maxio rejected the request.", ex);
        }
        catch (JsonException ex)
        {
            throw UnreadableResponse(ex);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw Unreachable(ex);
        }
    }

    public async Task<SubscriptionDto> SubscribeAsync(string userId, string email, string? planHandle, CancellationToken cancellationToken)
    {
        EnsureConfigured();
        try
        {
            var handle = string.IsNullOrWhiteSpace(planHandle)
                ? await DefaultPlanHandleAsync(cancellationToken)
                : planHandle;

            var customer = await FindOrCreateCustomerAsync(userId, email, cancellationToken);

            // Deterministic reference makes the subscribe idempotent: a retry or
            // double-click converges on the existing subscription.
            var reference = $"{userId}:{handle}";
            var existing = await FindSubscriptionByReferenceAsync(reference, cancellationToken);
            if (existing is not null)
            {
                return Map(existing);
            }

            try
            {
                var created = await Bounded(ct => _client.Subscriptions.CreateSubscription(
                    body: new MaxioCreateSubscriptionRequest
                    {
                        Subscription = new CreateSubscription
                        {
                            ProductHandle = handle,
                            CustomerId = customer.Id,
                            Reference = reference,
                            // This integration never captures cards; remittance
                            // invoices the subscriber instead of charging at signup.
                            PaymentCollectionMethod = CollectionMethod.Remittance
                        }
                    },
                    ct: ct), cancellationToken);
                return Map(created.Subscription);
            }
            catch (SdkException<CreateSubscriptionError> ex)
            {
                if (ex.Error.TryGetErrorListResponse1(out var validation))
                {
                    var detail = validation.Errors is null ? "validation failed" : string.Join("; ", validation.Errors);
                    throw new MaxioBillingException((HttpStatusCode)422, $"Maxio rejected the subscription: {detail}", ex);
                }
                if (ex.Error.TryGetRawError(out var raw))
                {
                    throw Translate(raw, ex);
                }
                throw new MaxioBillingException(HttpStatusCode.BadGateway, "Maxio rejected the subscription.", ex);
            }
        }
        catch (SdkException<RawError> ex)
        {
            throw Translate(ex.Error, ex);
        }
        catch (JsonException ex)
        {
            throw UnreadableResponse(ex);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw Unreachable(ex);
        }
    }

    public async Task<IReadOnlyList<SubscriptionDto>> ListSubscriptionsAsync(string userId, CancellationToken cancellationToken)
    {
        EnsureConfigured();
        try
        {
            var customer = await FindCustomerByReferenceAsync(userId, cancellationToken);
            if (customer?.Id is null)
            {
                return Array.Empty<SubscriptionDto>();
            }

            var subscriptions = await Bounded(
                ct => _client.Customers.ListCustomerSubscriptions(customerId: customer.Id.Value, ct: ct),
                cancellationToken);

            return subscriptions
                .Select(s => Map(s.Subscription))
                .ToList();
        }
        catch (SdkException<RawError> ex)
        {
            throw Translate(ex.Error, ex);
        }
        catch (JsonException ex)
        {
            throw UnreadableResponse(ex);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw Unreachable(ex);
        }
    }

    private async Task<ProductFamily> FindProductFamilyAsync(CancellationToken ct)
    {
        var families = await _client.ProductFamilies.ListProductFamilies(
            dateField: null,
            startDate: null,
            endDate: null,
            startDatetime: null,
            endDatetime: null,
            ct: ct);

        var family = families
            .Select(f => f.ProductFamily)
            .FirstOrDefault(f => string.Equals(f?.Handle, _settings.ProductFamilyHandle, StringComparison.OrdinalIgnoreCase));

        if (family?.Id is null)
        {
            throw new MaxioBillingException(
                HttpStatusCode.BadGateway,
                $"Maxio product family '{_settings.ProductFamilyHandle}' was not found.");
        }
        return family;
    }

    private async Task<string> DefaultPlanHandleAsync(CancellationToken cancellationToken)
    {
        var plans = await ListPlansAsync(cancellationToken);
        var handle = plans.Select(p => p.Handle).FirstOrDefault(h => !string.IsNullOrWhiteSpace(h));
        if (handle is null)
        {
            throw new MaxioBillingException(
                HttpStatusCode.BadGateway,
                $"Maxio product family '{_settings.ProductFamilyHandle}' contains no plans.");
        }
        return handle;
    }

    private async Task<Customer?> FindCustomerByReferenceAsync(string userId, CancellationToken cancellationToken)
    {
        try
        {
            var response = await Bounded(ct => _client.Customers.ReadCustomerByReference(reference: userId, ct: ct), cancellationToken);
            return response.Customer;
        }
        catch (SdkException<RawError> ex) when (ex.Error.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    private async Task<Customer> FindOrCreateCustomerAsync(string userId, string email, CancellationToken cancellationToken)
    {
        var existing = await FindCustomerByReferenceAsync(userId, cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        var (firstName, lastName) = DeriveName(email);
        try
        {
            var created = await Bounded(ct => _client.Customers.CreateCustomer(
                body: new CreateCustomerRequest
                {
                    Customer = new CreateCustomer
                    {
                        FirstName = firstName,
                        LastName = lastName,
                        Email = email,
                        Reference = userId
                    }
                },
                ct: ct), cancellationToken);
            return created.Customer;
        }
        catch (SdkException<CreateCustomerError> ex) when (ex.Error.TryGetCustomerErrorResponse1(out _))
        {
            // 422 — most likely a lost race on the unique reference: the customer
            // now exists, so re-read instead of failing.
            var reread = await FindCustomerByReferenceAsync(userId, cancellationToken);
            if (reread is not null)
            {
                return reread;
            }
            throw new MaxioBillingException((HttpStatusCode)422, "Maxio rejected the customer and the customer could not be re-read.", ex);
        }
    }

    private async Task<Subscription?> FindSubscriptionByReferenceAsync(string reference, CancellationToken cancellationToken)
    {
        try
        {
            var response = await Bounded(ct => _client.Subscriptions.FindSubscription(reference: reference, ct: ct), cancellationToken);
            return response.Subscription;
        }
        catch (SdkException<FindSubscriptionError> ex) when (ex.Error.TryGetNoContent(out _))
        {
            return null;
        }
    }

    private async Task<T> Bounded<T>(Func<CancellationToken, Task<T>> call, CancellationToken cancellationToken)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(CallBudget);
        return await call(cts.Token);
    }

    private void EnsureConfigured()
    {
        if (!_settings.IsConfigured)
        {
            throw new MaxioBillingException(
                HttpStatusCode.InternalServerError,
                "Maxio integration is not configured. Set Maxio:ApiKey and Maxio:Subdomain (or Maxio:BaseUrl) via user-secrets or environment variables.");
        }
    }

    private static (string FirstName, string LastName) DeriveName(string email)
    {
        var local = email.Split('@')[0];
        return string.IsNullOrWhiteSpace(local) ? ("eShop", "Customer") : (local, "Customer");
    }

    private static SubscriptionDto Map(Subscription? subscription) => new()
    {
        Id = subscription?.Id,
        State = subscription?.State?.Value,
        ProductName = subscription?.Product?.Name,
        ProductHandle = subscription?.Product?.Handle,
        UnitPriceInCents = subscription?.ProductPriceInCents,
        NextBillingAt = subscription?.NextAssessmentAt,
        CurrentPeriodEndsAt = subscription?.CurrentPeriodEndsAt
    };

    private MaxioBillingException Translate(RawError raw, Exception? inner = null)
    {
        // Carry the provider status: 4xx stays client-actionable, 5xx stays a provider failure.
        var status = raw.StatusCode;
        _logger.LogWarning("Maxio request failed with HTTP {StatusCode}", (int)status);
        return new MaxioBillingException(status, $"Maxio request failed (HTTP {(int)status}).", inner);
    }

    private MaxioBillingException UnreadableResponse(JsonException ex)
    {
        _logger.LogWarning(ex, "Maxio returned a response that could not be deserialized");
        return new MaxioBillingException(HttpStatusCode.BadGateway, "The billing provider returned a response that could not be processed.", ex);
    }

    private MaxioBillingException Unreachable(Exception ex)
    {
        _logger.LogWarning(ex, "Maxio is unreachable or timed out");
        return new MaxioBillingException(HttpStatusCode.ServiceUnavailable, "The billing provider is unreachable or timed out.", ex);
    }
}
