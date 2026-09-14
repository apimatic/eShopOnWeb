using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Models.Maxio;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// HTTP client for Maxio Advanced Billing (https://developers.maxio.com). Maxio is the system
/// of record for subscriptions: this service never persists subscription state locally, it
/// always reads it live from Maxio. The one local guard is an in-process per-buyer lock (see
/// <see cref="KeyedAsyncLock"/>) used to keep a double-click from racing past the
/// "does a subscription already exist" check, since Maxio has no idempotency-key mechanism of
/// its own for subscription creation.
/// </summary>
public class MaxioBillingService : IMaxioBillingService
{
    private static readonly HashSet<string> TerminalSubscriptionStates =
        new(StringComparer.OrdinalIgnoreCase) { "canceled", "expired" };

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
    };

    private readonly HttpClient _httpClient;
    private readonly MaxioOptions _options;
    private readonly KeyedAsyncLock _locks;
    private readonly ILogger<MaxioBillingService> _logger;

    public MaxioBillingService(HttpClient httpClient, IOptions<MaxioOptions> options, KeyedAsyncLock locks, ILogger<MaxioBillingService> logger)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _locks = locks;
        _logger = logger;
    }

    public async Task<IReadOnlyList<SubscriptionPlan>> GetAvailablePlansAsync(CancellationToken cancellationToken = default)
    {
        var familyHandle = _options.ProductFamilyHandle;
        if (string.IsNullOrWhiteSpace(familyHandle))
        {
            throw new MaxioApiException("Maxio:ProductFamilyHandle is not configured.", statusCode: null);
        }

        var family = await FindProductFamilyByHandleAsync(familyHandle, cancellationToken);
        if (family is null)
        {
            throw new MaxioApiException($"Maxio product family with handle '{familyHandle}' was not found on this site.", statusCode: null);
        }

        var productsResponse = await SendAsync(HttpMethod.Get, $"product_families/{family.Id}/products.json", null, cancellationToken);
        await ThrowIfErrorAsync(productsResponse, cancellationToken);
        var products = await productsResponse.Content.ReadFromJsonAsync<List<ProductEnvelope>>(JsonOptions, cancellationToken) ?? new();

        return products
            .Select(p => p.Product)
            .Where(p => p is not null && p.ArchivedAt is null)
            .Select(p => new SubscriptionPlan
            {
                Handle = p!.Handle ?? string.Empty,
                Name = p.Name ?? p.Handle ?? string.Empty,
                Description = p.Description,
                PriceInCents = p.PriceInCents,
                Interval = p.Interval,
                IntervalUnit = p.IntervalUnit ?? "month",
                RequiresPaymentMethod = p.RequireCreditCard
            })
            .ToList();
    }

    public async Task<SubscriptionEnrollment> SubscribeAsync(string buyerId, string buyerEmail, string productHandle, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrWhiteSpace(buyerId, nameof(buyerId));
        Guard.Against.NullOrWhiteSpace(buyerEmail, nameof(buyerEmail));
        Guard.Against.NullOrWhiteSpace(productHandle, nameof(productHandle));

        // Serialize per-buyer so a double-click can't race both requests past the
        // "no existing subscription" check below and create two.
        using var _ = await _locks.AcquireAsync($"maxio-subscribe:{buyerId}", cancellationToken);

        var customer = await EnsureCustomerAsync(buyerId, buyerEmail, cancellationToken);

        var existingSubscriptions = await ListSubscriptionsAsync(customer.Id, cancellationToken);
        var existing = existingSubscriptions.FirstOrDefault(s =>
            string.Equals(s.Product?.Handle, productHandle, StringComparison.OrdinalIgnoreCase) &&
            IsLive(s.State));

        if (existing is not null)
        {
            _logger.LogInformation(
                "Buyer {BuyerId} already has a live Maxio subscription {SubscriptionId} ({State}) to {ProductHandle}; returning it instead of creating a duplicate.",
                buyerId, existing.Id, existing.State, productHandle);
            return ToSubscriptionEnrollment(existing, customer.Id);
        }

        var createResponse = await SendAsync(HttpMethod.Post, "subscriptions.json", new CreateSubscriptionRequestEnvelope
        {
            Subscription = new CreateSubscriptionRequestWire
            {
                ProductHandle = productHandle,
                CustomerId = customer.Id,
                PaymentCollectionMethod = "invoice"
            }
        }, cancellationToken);
        await ThrowIfErrorAsync(createResponse, cancellationToken);

        var created = await createResponse.Content.ReadFromJsonAsync<SubscriptionEnvelope>(JsonOptions, cancellationToken);
        if (created?.Subscription is null)
        {
            throw new MaxioApiException("Maxio did not return the created subscription.", createResponse.StatusCode);
        }

        _logger.LogInformation("Created Maxio subscription {SubscriptionId} for buyer {BuyerId} to {ProductHandle}.", created.Subscription.Id, buyerId, productHandle);
        return ToSubscriptionEnrollment(created.Subscription, customer.Id);
    }

    public async Task<IReadOnlyList<SubscriptionEnrollment>> GetSubscriptionsForBuyerAsync(string buyerId, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrWhiteSpace(buyerId, nameof(buyerId));

        var customer = await LookupCustomerByReferenceAsync(buyerId, cancellationToken);
        if (customer is null)
        {
            return Array.Empty<SubscriptionEnrollment>();
        }

        var subscriptions = await ListSubscriptionsAsync(customer.Id, cancellationToken);
        return subscriptions.Select(s => ToSubscriptionEnrollment(s, customer.Id)).ToList();
    }

    private async Task<ProductFamilyWire?> FindProductFamilyByHandleAsync(string familyHandle, CancellationToken cancellationToken)
    {
        var response = await SendAsync(HttpMethod.Get, "product_families.json", null, cancellationToken);
        await ThrowIfErrorAsync(response, cancellationToken);
        var families = await response.Content.ReadFromJsonAsync<List<ProductFamilyEnvelope>>(JsonOptions, cancellationToken) ?? new();

        return families
            .Select(f => f.ProductFamily)
            .FirstOrDefault(f => f is not null && string.Equals(f.Handle, familyHandle, StringComparison.OrdinalIgnoreCase));
    }

    private async Task<CustomerWire> EnsureCustomerAsync(string buyerId, string buyerEmail, CancellationToken cancellationToken)
    {
        var existing = await LookupCustomerByReferenceAsync(buyerId, cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        var (firstName, lastName) = SplitDisplayName(buyerEmail);
        var createResponse = await SendAsync(HttpMethod.Post, "customers.json", new CreateCustomerRequestEnvelope
        {
            Customer = new CreateCustomerRequestWire
            {
                FirstName = firstName,
                LastName = lastName,
                Email = buyerEmail,
                Reference = buyerId
            }
        }, cancellationToken);

        if (createResponse.StatusCode == HttpStatusCode.UnprocessableEntity)
        {
            // Maxio has no "find or create" endpoint and no idempotency key, so a duplicate
            // reference surfaces as a validation error. Treat it as the expected outcome of a
            // race with a concurrent request for the same buyer and recover by re-reading.
            var recovered = await LookupCustomerByReferenceAsync(buyerId, cancellationToken);
            if (recovered is not null)
            {
                return recovered;
            }
        }

        await ThrowIfErrorAsync(createResponse, cancellationToken);
        var created = await createResponse.Content.ReadFromJsonAsync<CustomerEnvelope>(JsonOptions, cancellationToken);
        if (created?.Customer is null)
        {
            throw new MaxioApiException("Maxio did not return the created customer.", createResponse.StatusCode);
        }

        _logger.LogInformation("Created Maxio customer {CustomerId} for buyer {BuyerId}.", created.Customer.Id, buyerId);
        return created.Customer;
    }

    private async Task<CustomerWire?> LookupCustomerByReferenceAsync(string buyerId, CancellationToken cancellationToken)
    {
        var response = await SendAsync(HttpMethod.Get, $"customers/lookup.json?reference={Uri.EscapeDataString(buyerId)}", null, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        await ThrowIfErrorAsync(response, cancellationToken);
        var envelope = await response.Content.ReadFromJsonAsync<CustomerEnvelope>(JsonOptions, cancellationToken);
        return envelope?.Customer;
    }

    private async Task<List<SubscriptionWire>> ListSubscriptionsAsync(int customerId, CancellationToken cancellationToken)
    {
        var response = await SendAsync(HttpMethod.Get, $"customers/{customerId}/subscriptions.json", null, cancellationToken);
        await ThrowIfErrorAsync(response, cancellationToken);
        var envelopes = await response.Content.ReadFromJsonAsync<List<SubscriptionEnvelope>>(JsonOptions, cancellationToken) ?? new();

        var subscriptions = envelopes.Select(e => e.Subscription).Where(s => s is not null).Select(s => s!).ToList();
        foreach (var subscription in subscriptions.Where(s => string.IsNullOrEmpty(s.Product?.Handle)))
        {
            _logger.LogWarning("Maxio subscription {SubscriptionId} for customer {CustomerId} was returned without a nested product handle.", subscription.Id, customerId);
        }

        return subscriptions;
    }

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, object? body, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, path);
        if (body is not null)
        {
            request.Content = JsonContent.Create(body, options: JsonOptions);
        }

        try
        {
            return await _httpClient.SendAsync(request, cancellationToken);
        }
        catch (HttpRequestException ex)
        {
            throw new MaxioApiException($"Could not reach Maxio at {_httpClient.BaseAddress}: {ex.Message}", ex);
        }
    }

    private static async Task ThrowIfErrorAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        string[]? errors = null;
        try
        {
            var body = await response.Content.ReadFromJsonAsync<ErrorsEnvelope>(JsonOptions, cancellationToken);
            errors = body?.Errors;
        }
        catch (JsonException)
        {
            // Response body wasn't the standard {"errors": [...]} shape; fall through with a generic message.
        }

        var message = errors is { Length: > 0 }
            ? $"Maxio request failed ({(int)response.StatusCode} {response.StatusCode}): {string.Join("; ", errors)}"
            : $"Maxio request failed ({(int)response.StatusCode} {response.StatusCode}).";
        throw new MaxioApiException(message, response.StatusCode, errors);
    }

    private static bool IsLive(string? state) => !string.IsNullOrEmpty(state) && !TerminalSubscriptionStates.Contains(state);

    private static (string FirstName, string LastName) SplitDisplayName(string email)
    {
        var atIndex = email.IndexOf('@');
        var localPart = atIndex > 0 ? email[..atIndex] : email;
        return (localPart, "eShopOnWeb Customer");
    }

    private static SubscriptionEnrollment ToSubscriptionEnrollment(SubscriptionWire subscription, int customerId) => new()
    {
        MaxioSubscriptionId = subscription.Id,
        MaxioCustomerId = customerId,
        ProductHandle = subscription.Product?.Handle ?? string.Empty,
        ProductName = subscription.Product?.Name ?? subscription.Product?.Handle ?? string.Empty,
        State = subscription.State ?? string.Empty,
        PriceInCents = subscription.ProductPriceInCents,
        NextAssessmentAt = subscription.NextAssessmentAt,
        CurrentPeriodEndsAt = subscription.CurrentPeriodEndsAt,
        ActivatedAt = subscription.ActivatedAt
    };
}
