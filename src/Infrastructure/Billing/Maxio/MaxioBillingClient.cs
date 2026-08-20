using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Billing.Maxio;

/// <summary>
/// Maxio Advanced Billing HTTP client.
/// Verified against official Maxio SDK docs (ab-dotnet-sdk 9.1.0 / ab-ruby-sdk 10.0.0):
/// <list type="bullet">
/// <item>Auth: HTTP Basic, API key as username, literal "x" as password</item>
/// <item>US base URL: https://{subdomain}.chargify.com</item>
/// <item>GET product_families/{handle:family}/products.json</item>
/// <item>GET products/handle/{handle}.json</item>
/// <item>POST customers.json / GET customers/lookup.json?reference=</item>
/// <item>POST subscriptions.json / GET subscriptions/lookup.json?reference=</item>
/// <item>GET customers/{id}/subscriptions.json</item>
/// </list>
/// Maxio is the system of record; customer and subscription uniqueness is the Maxio
/// <c>reference</c> field (lookup-then-create, with 422 recovery for races).
/// </summary>
public sealed class MaxioBillingClient : ISubscriptionBillingService
{
    private readonly HttpClient _httpClient;
    private readonly MaxioOptions _options;
    private readonly ILogger<MaxioBillingClient> _logger;
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _enrollLocks = new(StringComparer.Ordinal);

    public MaxioBillingClient(HttpClient httpClient, IOptions<MaxioOptions> options, ILogger<MaxioBillingClient> logger)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<IReadOnlyList<SubscriptionPlan>> ListPlansAsync(CancellationToken cancellationToken = default)
    {
        EnsureConfigured();

        var familyKey = Uri.EscapeDataString("handle:" + _options.ProductFamilyHandle.Trim());
        var path = $"product_families/{familyKey}/products.json?per_page=200&page=1";
        var envelopes = await GetJsonAsync<List<MaxioProductEnvelope>>(path, cancellationToken);

        var plans = new List<SubscriptionPlan>();
        foreach (var envelope in envelopes ?? Enumerable.Empty<MaxioProductEnvelope>())
        {
            var product = envelope.Product;
            if (product is null || product.ArchivedAt is not null || string.IsNullOrWhiteSpace(product.Handle))
            {
                continue;
            }

            plans.Add(MapPlan(product));
        }

        return plans;
    }

    public async Task<SubscribeResult> SubscribeAsync(SubscribeToPlanCommand command, CancellationToken cancellationToken = default)
    {
        EnsureConfigured();

        if (string.IsNullOrWhiteSpace(command.ProductHandle))
        {
            throw new BillingValidationException("productHandle is required.");
        }

        var productHandle = command.ProductHandle.Trim();
        await EnsureProductInFamilyAsync(productHandle, cancellationToken);

        var customerReference = BillingReference.ForCustomer(command.ShopperIdentity);
        var subscriptionReference = BillingReference.ForSubscription(command.ShopperIdentity, productHandle);
        var gate = _enrollLocks.GetOrAdd(subscriptionReference, _ => new SemaphoreSlim(1, 1));

        await gate.WaitAsync(cancellationToken);
        try
        {
            var customer = await EnsureCustomerAsync(command, customerReference, cancellationToken);

            var existing = await FindSubscriptionByReferenceAsync(subscriptionReference, cancellationToken);
            if (existing is not null)
            {
                _logger.LogInformation(
                    "Returning existing Maxio subscription {SubscriptionId} for {CustomerReference} / {ProductHandle}",
                    existing.Id, customerReference, productHandle);
                return new SubscribeResult(MapSubscription(existing), created: false);
            }

            var created = await CreateSubscriptionAsync(customer, productHandle, subscriptionReference, cancellationToken);
            return new SubscribeResult(MapSubscription(created), created: true);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<IReadOnlyList<ShopperSubscription>> ListSubscriptionsAsync(string shopperIdentity, CancellationToken cancellationToken = default)
    {
        EnsureConfigured();

        var customerReference = BillingReference.ForCustomer(shopperIdentity);
        var customer = await FindCustomerByReferenceAsync(customerReference, cancellationToken);
        if (customer?.Id is null)
        {
            return Array.Empty<ShopperSubscription>();
        }

        var envelopes = await GetJsonAsync<List<MaxioSubscriptionEnvelope>>(
            $"customers/{customer.Id.Value}/subscriptions.json",
            cancellationToken);

        var result = new List<ShopperSubscription>();
        foreach (var envelope in envelopes ?? Enumerable.Empty<MaxioSubscriptionEnvelope>())
        {
            if (envelope.Subscription is null)
            {
                continue;
            }

            result.Add(MapSubscription(envelope.Subscription));
        }

        return result;
    }

    private async Task EnsureProductInFamilyAsync(string productHandle, CancellationToken cancellationToken)
    {
        var envelope = await GetJsonAsync<MaxioProductEnvelope>(
            $"products/handle/{Uri.EscapeDataString(productHandle)}.json",
            cancellationToken,
            allowNotFound: true);

        var product = envelope?.Product;
        if (product is null)
        {
            throw new BillingValidationException($"Unknown subscription plan '{productHandle}'.");
        }

        if (product.ArchivedAt is not null)
        {
            throw new BillingValidationException($"Subscription plan '{productHandle}' is no longer available.");
        }

        var familyHandle = product.ProductFamily?.Handle;
        if (!string.Equals(familyHandle, _options.ProductFamilyHandle.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            throw new BillingValidationException($"Subscription plan '{productHandle}' is not in the configured product family.");
        }
    }

    private async Task<MaxioCustomer> EnsureCustomerAsync(
        SubscribeToPlanCommand command,
        string customerReference,
        CancellationToken cancellationToken)
    {
        var existing = await FindCustomerByReferenceAsync(customerReference, cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        var body = new MaxioCreateCustomerRequest
        {
            Customer = new MaxioCreateCustomer
            {
                FirstName = command.FirstName,
                LastName = command.LastName,
                Email = command.Email,
                Reference = customerReference
            }
        };

        try
        {
            var created = await PostJsonAsync<MaxioCreateCustomerRequest, MaxioCustomerEnvelope>(
                "customers.json",
                body,
                cancellationToken);

            if (created?.Customer?.Id is null)
            {
                throw new BillingGatewayException("Maxio created a customer without an id.", (int)HttpStatusCode.BadGateway);
            }

            _logger.LogInformation("Created Maxio customer {CustomerId} for {CustomerReference}", created.Customer.Id, customerReference);
            return created.Customer;
        }
        catch (BillingGatewayException ex) when (ex.StatusCode == (int)HttpStatusCode.UnprocessableEntity)
        {
            var raced = await FindCustomerByReferenceAsync(customerReference, cancellationToken);
            if (raced is not null)
            {
                return raced;
            }

            throw;
        }
    }

    private async Task<MaxioCustomer?> FindCustomerByReferenceAsync(string reference, CancellationToken cancellationToken)
    {
        var envelope = await GetJsonAsync<MaxioCustomerEnvelope>(
            $"customers/lookup.json?reference={Uri.EscapeDataString(reference)}",
            cancellationToken,
            allowNotFound: true);

        return envelope?.Customer;
    }

    private async Task<MaxioSubscription?> FindSubscriptionByReferenceAsync(string reference, CancellationToken cancellationToken)
    {
        var envelope = await GetJsonAsync<MaxioSubscriptionEnvelope>(
            $"subscriptions/lookup.json?reference={Uri.EscapeDataString(reference)}",
            cancellationToken,
            allowNotFound: true);

        return envelope?.Subscription;
    }

    private async Task<MaxioSubscription> CreateSubscriptionAsync(
        MaxioCustomer customer,
        string productHandle,
        string subscriptionReference,
        CancellationToken cancellationToken)
    {
        if (customer.Id is null)
        {
            throw new BillingGatewayException("Cannot create a subscription without a Maxio customer id.", (int)HttpStatusCode.BadGateway);
        }

        var body = new MaxioCreateSubscriptionRequest
        {
            Subscription = new MaxioCreateSubscription
            {
                ProductHandle = productHandle,
                CustomerId = customer.Id.Value,
                Reference = subscriptionReference,
                PaymentCollectionMethod = "remittance"
            }
        };

        try
        {
            var created = await PostJsonAsync<MaxioCreateSubscriptionRequest, MaxioSubscriptionEnvelope>(
                "subscriptions.json",
                body,
                cancellationToken);

            if (created?.Subscription is null)
            {
                throw new BillingGatewayException("Maxio created a subscription without a body.", (int)HttpStatusCode.BadGateway);
            }

            _logger.LogInformation(
                "Created Maxio subscription {SubscriptionId} for customer {CustomerId} on {ProductHandle}",
                created.Subscription.Id, customer.Id, productHandle);

            return created.Subscription;
        }
        catch (BillingGatewayException ex) when (ex.StatusCode == (int)HttpStatusCode.UnprocessableEntity)
        {
            var raced = await FindSubscriptionByReferenceAsync(subscriptionReference, cancellationToken);
            if (raced is not null)
            {
                return raced;
            }

            throw;
        }
    }

    private static SubscriptionPlan MapPlan(MaxioProduct product)
    {
        var cents = product.PriceInCents ?? 0;
        return new SubscriptionPlan
        {
            Id = product.Id ?? 0,
            Handle = product.Handle ?? string.Empty,
            Name = product.Name ?? string.Empty,
            Description = product.Description ?? string.Empty,
            PriceInCents = cents,
            Price = BillingReference.CentsToAmount(cents),
            Interval = product.Interval ?? 0,
            IntervalUnit = product.IntervalUnit ?? string.Empty,
            FamilyHandle = product.ProductFamily?.Handle ?? string.Empty,
            RequiresPaymentMethod = product.RequireCreditCard ?? false
        };
    }

    private static ShopperSubscription MapSubscription(MaxioSubscription subscription)
    {
        var cents = subscription.ProductPriceInCents
            ?? subscription.Product?.PriceInCents
            ?? 0;

        return new ShopperSubscription
        {
            Id = subscription.Id ?? 0,
            State = subscription.State ?? string.Empty,
            ProductHandle = subscription.Product?.Handle ?? string.Empty,
            ProductName = subscription.Product?.Name ?? string.Empty,
            PriceInCents = cents,
            Price = BillingReference.CentsToAmount(cents),
            Currency = subscription.Currency,
            Interval = subscription.Product?.Interval,
            IntervalUnit = subscription.Product?.IntervalUnit,
            NextBillingAt = subscription.NextAssessmentAt ?? subscription.CurrentPeriodEndsAt,
            CurrentPeriodEndsAt = subscription.CurrentPeriodEndsAt,
            CreatedAt = subscription.CreatedAt,
            Reference = subscription.Reference
        };
    }

    private void EnsureConfigured()
    {
        if (!_options.IsConfigured)
        {
            throw new BillingNotConfiguredException();
        }
    }

    private async Task<T?> GetJsonAsync<T>(string relativePath, CancellationToken cancellationToken, bool allowNotFound = false)
    {
        using var response = await _httpClient.GetAsync(relativePath, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);

        if (allowNotFound && response.StatusCode == HttpStatusCode.NotFound)
        {
            return default;
        }

        EnsureSuccess(response, body, relativePath);
        if (string.IsNullOrWhiteSpace(body))
        {
            return default;
        }

        return JsonSerializer.Deserialize<T>(body, MaxioJson.Options);
    }

    private async Task<TResponse?> PostJsonAsync<TRequest, TResponse>(string relativePath, TRequest payload, CancellationToken cancellationToken)
    {
        using var response = await _httpClient.PostAsJsonAsync(relativePath, payload, MaxioJson.Options, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        EnsureSuccess(response, body, relativePath);
        if (string.IsNullOrWhiteSpace(body))
        {
            return default;
        }

        return JsonSerializer.Deserialize<TResponse>(body, MaxioJson.Options);
    }

    private void EnsureSuccess(HttpResponseMessage response, string body, string path)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var detail = TryReadMaxioErrors(body);
        var status = (int)response.StatusCode;
        _logger.LogWarning("Maxio {Method} {Path} failed with {StatusCode}: {Detail}",
            response.RequestMessage?.Method, path, status, detail);

        var message = string.IsNullOrWhiteSpace(detail)
            ? $"Maxio request failed ({status}) for {path}."
            : $"Maxio request failed ({status}): {detail}";

        throw new BillingGatewayException(message, status, body);
    }

    private static string? TryReadMaxioErrors(string body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return null;
        }

        try
        {
            var parsed = JsonSerializer.Deserialize<MaxioErrorResponse>(body, MaxioJson.Options);
            if (!string.IsNullOrWhiteSpace(parsed?.Errors))
            {
                return parsed.Errors;
            }
        }
        catch (JsonException)
        {
            // fall through to truncated raw body
        }

        const int maxLen = 500;
        var trimmed = body.Trim();
        return trimmed.Length <= maxLen ? trimmed : trimmed[..maxLen];
    }
}
