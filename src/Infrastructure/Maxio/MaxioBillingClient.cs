using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
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

public sealed class MaxioBillingClient : IMaxioBillingGateway
{
    private const int MaxAttempts = 4;
    private const int ListPageSize = 200;

    private readonly HttpClient _httpClient;
    private readonly MaxioOptions _options;
    private readonly ILogger<MaxioBillingClient> _logger;

    public MaxioBillingClient(
        HttpClient httpClient,
        IOptions<MaxioOptions> options,
        ILogger<MaxioBillingClient> logger)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<IReadOnlyList<SubscriptionPlan>> ListAvailablePlansAsync(CancellationToken cancellationToken)
    {
        EnsureConfigured();

        var familyHandle = _options.ProductFamilyHandle.Trim();
        var path = $"product_families/handle:{Uri.EscapeDataString(familyHandle)}/products.json?per_page={ListPageSize}&page=1";
        var envelopes = await GetJsonAsync<List<ProductEnvelope>>(path, cancellationToken) ?? new List<ProductEnvelope>();

        return envelopes
            .Select(e => e.Product)
            .Where(p => p is not null && p.ArchivedAt is null && !string.IsNullOrWhiteSpace(p.Handle))
            .Select(p => MapPlan(p!))
            .ToList();
    }

    public async Task<SubscriptionPlan?> GetPlanByHandleAsync(string productHandle, CancellationToken cancellationToken)
    {
        EnsureConfigured();

        if (string.IsNullOrWhiteSpace(productHandle))
        {
            return null;
        }

        var path = $"products/handle/{Uri.EscapeDataString(productHandle.Trim())}.json";
        var envelope = await GetJsonOrNotFoundAsync<ProductEnvelope>(path, cancellationToken);
        var product = envelope?.Product;
        if (product is null || product.ArchivedAt is not null)
        {
            return null;
        }

        var family = product.ProductFamily?.Handle;
        if (!string.Equals(family, _options.ProductFamilyHandle.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogInformation(
                "Ignoring Maxio product {Handle} because its family {Family} is not the configured family.",
                product.Handle, family);
            return null;
        }

        return MapPlan(product);
    }

    public async Task<BillingCustomer?> FindCustomerByReferenceAsync(string reference, CancellationToken cancellationToken)
    {
        EnsureConfigured();

        var path = $"customers/lookup.json?reference={Uri.EscapeDataString(reference)}";
        var envelope = await GetJsonOrNotFoundAsync<CustomerEnvelope>(path, cancellationToken);
        return envelope?.Customer is null ? null : MapCustomer(envelope.Customer);
    }

    public async Task<BillingCustomer> CreateCustomerAsync(
        NewBillingCustomer customer,
        string uniquenessToken,
        CancellationToken cancellationToken)
    {
        EnsureConfigured();

        var body = new CreateCustomerRequestBody
        {
            Customer = new MaxioCustomerDto
            {
                FirstName = customer.FirstName,
                LastName = customer.LastName,
                Email = customer.Email,
                Reference = customer.Reference
            },
            UniquenessToken = uniquenessToken
        };

        var envelope = await SendJsonAsync<CustomerEnvelope>(
            HttpMethod.Post,
            "customers.json",
            body,
            cancellationToken);

        if (envelope?.Customer is null)
        {
            throw new MaxioBillingException("Maxio created a customer but returned an empty payload.", HttpStatusCode.OK);
        }

        return MapCustomer(envelope.Customer);
    }

    public async Task<IReadOnlyList<ShopperSubscription>> ListCustomerSubscriptionsAsync(
        long customerId,
        CancellationToken cancellationToken)
    {
        EnsureConfigured();

        var path = $"customers/{customerId}/subscriptions.json?per_page={ListPageSize}&page=1";
        var envelopes = await GetJsonAsync<List<SubscriptionEnvelope>>(path, cancellationToken)
                       ?? new List<SubscriptionEnvelope>();

        return envelopes
            .Select(e => e.Subscription)
            .Where(s => s is not null)
            .Select(s => MapSubscription(s!))
            .ToList();
    }

    public async Task<ShopperSubscription> CreateSubscriptionAsync(
        NewBillingSubscription subscription,
        CancellationToken cancellationToken)
    {
        EnsureConfigured();

        var body = new CreateSubscriptionRequestBody
        {
            Subscription = new CreateSubscriptionPayload
            {
                ProductHandle = subscription.ProductHandle,
                CustomerId = subscription.CustomerId,
                Reference = subscription.Reference,
                PaymentCollectionMethod = subscription.PaymentCollectionMethod
            },
            UniquenessToken = subscription.UniquenessToken
        };

        var envelope = await SendJsonAsync<SubscriptionEnvelope>(
            HttpMethod.Post,
            "subscriptions.json",
            body,
            cancellationToken);

        if (envelope?.Subscription is null)
        {
            throw new MaxioBillingException("Maxio created a subscription but returned an empty payload.", HttpStatusCode.OK);
        }

        return MapSubscription(envelope.Subscription);
    }

    public async Task<ShopperSubscription?> FindSubscriptionByReferenceAsync(
        string reference,
        CancellationToken cancellationToken)
    {
        EnsureConfigured();

        var path = $"subscriptions/lookup.json?reference={Uri.EscapeDataString(reference)}";
        var envelope = await GetJsonOrNotFoundAsync<SubscriptionEnvelope>(path, cancellationToken);
        return envelope?.Subscription is null ? null : MapSubscription(envelope.Subscription);
    }

    private void EnsureConfigured()
    {
        if (!_options.IsConfigured)
        {
            throw new MaxioConfigurationException(
                "Maxio billing is not configured. Set Maxio:ApiKey, Maxio:Subdomain (or Maxio:BaseUrl), and Maxio:ProductFamilyHandle.");
        }
    }

    private async Task<T?> GetJsonAsync<T>(string relativePath, CancellationToken cancellationToken)
    {
        using var response = await SendWithRetryAsync(HttpMethod.Get, relativePath, contentJson: null, cancellationToken);
        await EnsureSuccessAsync(response);
        return await ReadJsonAsync<T>(response, cancellationToken);
    }

    private async Task<T?> GetJsonOrNotFoundAsync<T>(string relativePath, CancellationToken cancellationToken)
        where T : class
    {
        using var response = await SendWithRetryAsync(HttpMethod.Get, relativePath, contentJson: null, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        await EnsureSuccessAsync(response);
        return await ReadJsonAsync<T>(response, cancellationToken);
    }

    private async Task<T> SendJsonAsync<T>(HttpMethod method, string relativePath, object body, CancellationToken cancellationToken)
    {
        var json = JsonSerializer.Serialize(body, MaxioJson.SerializerOptions);
        using var response = await SendWithRetryAsync(method, relativePath, json, cancellationToken);
        await EnsureSuccessAsync(response);
        var parsed = await ReadJsonAsync<T>(response, cancellationToken);
        if (parsed is null)
        {
            throw new MaxioBillingException($"Maxio returned an empty {typeof(T).Name} payload.", response.StatusCode);
        }

        return parsed;
    }

    private async Task<HttpResponseMessage> SendWithRetryAsync(
        HttpMethod method,
        string relativePath,
        string? contentJson,
        CancellationToken cancellationToken)
    {
        HttpResponseMessage? response = null;
        for (var attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            response?.Dispose();
            using var request = new HttpRequestMessage(method, relativePath);
            if (contentJson is not null)
            {
                request.Content = new StringContent(contentJson, Encoding.UTF8, "application/json");
            }

            try
            {
                response = await _httpClient.SendAsync(request, cancellationToken);
            }
            catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested && attempt < MaxAttempts)
            {
                _logger.LogWarning("Maxio request {Method} {Path} timed out (attempt {Attempt}). Retrying.", method, relativePath, attempt);
                await DelayBackoffAsync(attempt, cancellationToken);
                continue;
            }
            catch (HttpRequestException ex) when (attempt < MaxAttempts)
            {
                _logger.LogWarning(ex, "Maxio request {Method} {Path} failed (attempt {Attempt}). Retrying.", method, relativePath, attempt);
                await DelayBackoffAsync(attempt, cancellationToken);
                continue;
            }

            if (response!.StatusCode == (HttpStatusCode)429 && attempt < MaxAttempts)
            {
                _logger.LogWarning("Maxio returned 429 for {Method} {Path} (attempt {Attempt}). Backing off.", method, relativePath, attempt);
                await DelayBackoffAsync(attempt, cancellationToken);
                continue;
            }

            if ((int)response.StatusCode >= 500 && attempt < MaxAttempts && method == HttpMethod.Get)
            {
                _logger.LogWarning("Maxio returned {Status} for GET {Path} (attempt {Attempt}). Retrying.", (int)response.StatusCode, relativePath, attempt);
                await DelayBackoffAsync(attempt, cancellationToken);
                continue;
            }

            return response;
        }

        return response!;
    }

    private static Task DelayBackoffAsync(int attempt, CancellationToken cancellationToken) =>
        Task.Delay(TimeSpan.FromSeconds(Math.Pow(2, attempt)), cancellationToken);

    private static async Task EnsureSuccessAsync(HttpResponseMessage response)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var body = await response.Content.ReadAsStringAsync();
        var message = TryFormatErrors(body) ?? $"Maxio request failed with {(int)response.StatusCode} {response.ReasonPhrase}.";
        throw new MaxioBillingException(message, response.StatusCode, body);
    }

    private static string? TryFormatErrors(string body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return null;
        }

        try
        {
            var parsed = JsonSerializer.Deserialize<MaxioErrorResponse>(body, MaxioJson.SerializerOptions);
            if (parsed?.Errors is { Count: > 0 })
            {
                return string.Join(" ", parsed.Errors);
            }
        }
        catch (JsonException)
        {
            // Fall back to the raw body below.
        }

        return body.Length > 500 ? body[..500] : body;
    }

    private static async Task<T?> ReadJsonAsync<T>(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.Content.Headers.ContentLength == 0)
        {
            return default;
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        return await JsonSerializer.DeserializeAsync<T>(stream, MaxioJson.SerializerOptions, cancellationToken);
    }

    private static SubscriptionPlan MapPlan(MaxioProductDto product) =>
        new(
            Handle: product.Handle ?? string.Empty,
            Name: product.Name ?? product.Handle ?? string.Empty,
            Description: product.Description,
            Price: ToDollars(product.PriceInCents),
            Interval: product.Interval ?? 1,
            IntervalUnit: product.IntervalUnit ?? "month",
            ProductFamilyHandle: product.ProductFamily?.Handle,
            RequireCreditCard: product.RequireCreditCard ?? false);

    private static BillingCustomer MapCustomer(MaxioCustomerDto customer) =>
        new(
            Id: customer.Id ?? 0,
            Reference: customer.Reference,
            Email: customer.Email ?? string.Empty);

    private static ShopperSubscription MapSubscription(MaxioSubscriptionDto subscription)
    {
        var cents = subscription.ProductPriceInCents ?? subscription.Product?.PriceInCents;
        var nextBilling = subscription.CurrentPeriodEndsAt ?? subscription.NextAssessmentAt;
        return new ShopperSubscription(
            Id: subscription.Id ?? 0,
            Reference: subscription.Reference,
            State: subscription.State ?? string.Empty,
            ProductHandle: subscription.Product?.Handle,
            ProductName: subscription.Product?.Name,
            Price: ToDollars(cents),
            NextBillingDate: nextBilling);
    }

    private static decimal ToDollars(long? cents) => (cents ?? 0) / 100m;
}
