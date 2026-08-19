using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Billing;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Maxio.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

public class MaxioAdvancedBillingClient : ISubscriptionBillingGateway
{
    private const int MaxAttempts = 4;
    private readonly HttpClient _httpClient;
    private readonly ILogger<MaxioAdvancedBillingClient> _logger;

    public MaxioAdvancedBillingClient(HttpClient httpClient, ILogger<MaxioAdvancedBillingClient> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public async Task<IReadOnlyList<SubscriptionPlan>> ListPlansAsync(
        string productFamilyHandle,
        CancellationToken cancellationToken = default)
    {
        var familyKey = Uri.EscapeDataString($"handle:{productFamilyHandle}");
        var envelopes = await SendAsync<List<ProductEnvelope>>(
            HttpMethod.Get,
            $"product_families/{familyKey}/products.json?per_page=200",
            null,
            cancellationToken);

        return (envelopes ?? new List<ProductEnvelope>())
            .Where(envelope => envelope.Product is not null)
            .Select(envelope => ToPlan(envelope.Product!))
            .ToList();
    }

    public async Task<BillingCustomer?> FindCustomerByReferenceAsync(
        string reference,
        CancellationToken cancellationToken = default)
    {
        var path = $"customers/lookup.json?reference={Uri.EscapeDataString(reference)}";
        try
        {
            var envelope = await SendAsync<CustomerEnvelope>(HttpMethod.Get, path, null, cancellationToken);
            return envelope?.Customer is null ? null : ToCustomer(envelope.Customer);
        }
        catch (MaxioApiException ex) when (ex.StatusCode == (int)HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    public async Task<BillingCustomer> CreateCustomerAsync(
        ShopperIdentity shopper,
        string reference,
        CancellationToken cancellationToken = default)
    {
        var (firstName, lastName) = SplitName(shopper);
        var request = new CreateCustomerRequest
        {
            Customer = new CreateCustomerBody
            {
                FirstName = firstName,
                LastName = lastName,
                Email = shopper.Email,
                Organization = "eShopOnWeb",
                Reference = reference
            }
        };

        var envelope = await SendAsync<CustomerEnvelope>(HttpMethod.Post, "customers.json", request, cancellationToken);
        if (envelope?.Customer is null)
        {
            throw new MaxioApiException("Maxio created a customer but returned an empty payload.");
        }

        return ToCustomer(envelope.Customer);
    }

    public async Task<IReadOnlyList<CustomerSubscription>> ListCustomerSubscriptionsAsync(
        int customerId,
        CancellationToken cancellationToken = default)
    {
        var envelopes = await SendAsync<List<SubscriptionEnvelope>>(
            HttpMethod.Get,
            $"customers/{customerId}/subscriptions.json",
            null,
            cancellationToken);

        return (envelopes ?? new List<SubscriptionEnvelope>())
            .Where(envelope => envelope.Subscription is not null)
            .Select(envelope => ToSubscription(envelope.Subscription!))
            .ToList();
    }

    public async Task<CustomerSubscription?> FindSubscriptionByReferenceAsync(
        string reference,
        CancellationToken cancellationToken = default)
    {
        var path = $"subscriptions/lookup.json?reference={Uri.EscapeDataString(reference)}";
        try
        {
            var envelope = await SendAsync<SubscriptionEnvelope>(HttpMethod.Get, path, null, cancellationToken);
            return envelope?.Subscription is null ? null : ToSubscription(envelope.Subscription);
        }
        catch (MaxioApiException ex) when (ex.StatusCode == (int)HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    public async Task<CustomerSubscription> CreateSubscriptionAsync(
        int customerId,
        string productHandle,
        string uniquenessToken,
        string subscriptionReference,
        CancellationToken cancellationToken = default)
    {
        var request = new CreateSubscriptionRequest
        {
            UniquenessToken = uniquenessToken,
            Subscription = new CreateSubscriptionBody
            {
                ProductHandle = productHandle,
                CustomerId = customerId,
                Reference = subscriptionReference
            }
        };

        var envelope = await SendAsync<SubscriptionEnvelope>(HttpMethod.Post, "subscriptions.json", request, cancellationToken);
        if (envelope?.Subscription is null)
        {
            throw new MaxioApiException("Maxio created a subscription but returned an empty payload.");
        }

        return ToSubscription(envelope.Subscription);
    }

    internal static (string FirstName, string LastName) SplitName(ShopperIdentity shopper)
    {
        var source = !string.IsNullOrWhiteSpace(shopper.Email)
            ? shopper.Email
            : shopper.UserName;

        var local = source.Contains('@', StringComparison.Ordinal)
            ? source[..source.IndexOf('@')]
            : source;

        local = string.IsNullOrWhiteSpace(local) ? "Shopper" : local.Trim();
        return (local, "eShopOnWeb");
    }

    private async Task<T?> SendAsync<T>(
        HttpMethod method,
        string relativePath,
        object? body,
        CancellationToken cancellationToken)
    {
        Exception? lastException = null;

        for (var attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            using var request = new HttpRequestMessage(method, relativePath);
            if (body is not null)
            {
                request.Content = JsonContent.Create(body, options: MaxioJson.Options);
            }

            HttpResponseMessage response;
            try
            {
                response = await _httpClient.SendAsync(request, cancellationToken);
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
            {
                lastException = ex;
                _logger.LogWarning(ex, "Maxio HTTP {Method} {Path} failed on attempt {Attempt}.", method, relativePath, attempt);
                if (attempt == MaxAttempts)
                {
                    break;
                }

                await DelayForRetry(attempt, cancellationToken);
                continue;
            }

            using (response)
            {
                if ((int)response.StatusCode == 429 && attempt < MaxAttempts)
                {
                    _logger.LogWarning("Maxio rate-limited {Method} {Path}; retrying.", method, relativePath);
                    await DelayForRetry(attempt, cancellationToken);
                    continue;
                }

                var payload = await response.Content.ReadAsStringAsync(cancellationToken);
                if (response.IsSuccessStatusCode)
                {
                    if (string.IsNullOrWhiteSpace(payload))
                    {
                        return default;
                    }

                    return JsonSerializer.Deserialize<T>(payload, MaxioJson.Options);
                }

                throw CreateApiException(response.StatusCode, payload);
            }
        }

        throw new MaxioApiException("Maxio request failed after retries.", lastException ?? new HttpRequestException("Unknown HTTP failure."));
    }

    private static async Task DelayForRetry(int attempt, CancellationToken cancellationToken)
    {
        var delay = TimeSpan.FromSeconds(Math.Pow(2, attempt));
        await Task.Delay(delay, cancellationToken);
    }

    private static MaxioApiException CreateApiException(HttpStatusCode statusCode, string payload)
    {
        var message = $"Maxio request failed with {(int)statusCode} {statusCode}.";
        try
        {
            var errors = JsonSerializer.Deserialize<ErrorListResponse>(payload, MaxioJson.Options);
            if (errors?.Errors is { Count: > 0 })
            {
                message = $"{message} {string.Join(" ", errors.Errors)}";
            }
        }
        catch (JsonException)
        {
            // Body is not a structured error list; keep the status-based message.
        }

        return new MaxioApiException(message) { StatusCode = (int)statusCode };
    }

    private static BillingCustomer ToCustomer(CustomerPayload payload) =>
        new(payload.Id, payload.Reference, payload.Email ?? string.Empty);

    private static SubscriptionPlan ToPlan(ProductPayload payload) =>
        new(
            payload.Id,
            payload.Handle ?? string.Empty,
            payload.Name ?? string.Empty,
            payload.Description,
            CentsToAmount(payload.PriceInCents),
            payload.Interval,
            payload.IntervalUnit ?? "month",
            payload.ProductFamily?.Handle);

    private static CustomerSubscription ToSubscription(SubscriptionPayload payload) =>
        new(
            payload.Id,
            payload.State ?? string.Empty,
            payload.Product?.Handle,
            payload.Product?.Name,
            CentsToAmount(payload.ProductPriceInCents),
            payload.CurrentPeriodEndsAt ?? payload.NextAssessmentAt,
            payload.CreatedAt,
            payload.Reference);

    private static decimal CentsToAmount(long cents) => cents / 100m;

    internal static AuthenticationHeaderValue CreateBasicAuthHeader(string apiKey)
    {
        var token = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{apiKey}:X"));
        return new AuthenticationHeaderValue("Basic", token);
    }
}
