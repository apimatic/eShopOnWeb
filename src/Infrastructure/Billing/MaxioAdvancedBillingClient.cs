using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Billing;

internal sealed class MaxioAdvancedBillingClient : IMaxioAdvancedBillingClient
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<MaxioAdvancedBillingClient> _logger;

    public MaxioAdvancedBillingClient(
        HttpClient httpClient,
        IOptions<MaxioOptions> options,
        ILogger<MaxioAdvancedBillingClient> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
        options.Value.EnsureConfigured();
    }

    public async Task<IReadOnlyList<MaxioProduct>> ListProductsForFamilyAsync(string productFamilyHandle, CancellationToken cancellationToken)
    {
        var path = $"product_families/handle:{Uri.EscapeDataString(productFamilyHandle)}/products.json?per_page=200";
        var envelopes = await SendAsync<List<MaxioProductEnvelope>>(HttpMethod.Get, path, null, cancellationToken);
        return envelopes?
            .Select(e => e.Product)
            .Where(p => p is not null && string.IsNullOrEmpty(p.ArchivedAt))
            .Cast<MaxioProduct>()
            .ToList() ?? new List<MaxioProduct>();
    }

    public async Task<MaxioCustomer?> FindCustomerByReferenceAsync(string reference, CancellationToken cancellationToken)
    {
        var path = $"customers/lookup.json?reference={Uri.EscapeDataString(reference)}";
        try
        {
            var envelope = await SendAsync<MaxioCustomerEnvelope>(HttpMethod.Get, path, null, cancellationToken);
            return envelope?.Customer;
        }
        catch (MaxioApiException ex) when (ex.StatusCode == (int)HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    public async Task<MaxioCustomer> CreateCustomerAsync(MaxioCustomerPayload customer, string uniquenessToken, CancellationToken cancellationToken)
    {
        var body = new MaxioCreateCustomerRequest { Customer = customer, UniquenessToken = uniquenessToken };
        var envelope = await SendAsync<MaxioCustomerEnvelope>(
            HttpMethod.Post,
            "customers.json",
            body,
            cancellationToken);

        if (envelope?.Customer is null)
        {
            throw new MaxioApiException(500, "Maxio create-customer returned an empty payload.");
        }

        return envelope.Customer;
    }

    public async Task<IReadOnlyList<MaxioSubscription>> ListCustomerSubscriptionsAsync(int customerId, CancellationToken cancellationToken)
    {
        var path = $"customers/{customerId}/subscriptions.json";
        var envelopes = await SendAsync<List<MaxioSubscriptionEnvelope>>(HttpMethod.Get, path, null, cancellationToken);
        return envelopes?
            .Select(e => e.Subscription)
            .Where(s => s is not null)
            .Cast<MaxioSubscription>()
            .ToList() ?? new List<MaxioSubscription>();
    }

    public async Task<MaxioSubscription> CreateSubscriptionAsync(int customerId, string productHandle, string uniquenessToken, CancellationToken cancellationToken)
    {
        var body = new MaxioCreateSubscriptionRequest
        {
            Subscription = new MaxioCreateSubscriptionPayload
            {
                CustomerId = customerId,
                ProductHandle = productHandle
            },
            UniquenessToken = uniquenessToken
        };

        var envelope = await SendAsync<MaxioSubscriptionEnvelope>(
            HttpMethod.Post,
            "subscriptions.json",
            body,
            cancellationToken);

        if (envelope?.Subscription is null)
        {
            throw new MaxioApiException(500, "Maxio create-subscription returned an empty payload.");
        }

        return envelope.Subscription;
    }

    private async Task<T?> SendAsync<T>(
        HttpMethod method,
        string relativePath,
        object? body,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, relativePath);
        if (body is not null)
        {
            var json = JsonSerializer.Serialize(body, MaxioJson.SerializerOptions);
            request.Content = new StringContent(json, Encoding.UTF8, "application/json");
        }

        _logger.LogInformation("Maxio {Method} {Path}", method, relativePath);

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        var payload = await response.Content.ReadAsStringAsync(cancellationToken);

        if (response.StatusCode == HttpStatusCode.Conflict)
        {
            throw new MaxioDuplicateSubmissionException(payload);
        }

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            throw new MaxioApiException((int)response.StatusCode, "Maxio resource was not found.");
        }

        if (!response.IsSuccessStatusCode)
        {
            var message = TryFormatErrors(payload) ?? payload;
            _logger.LogWarning("Maxio {Method} {Path} failed with {Status}: {Message}", method, relativePath, (int)response.StatusCode, message);
            throw new MaxioApiException((int)response.StatusCode, string.IsNullOrWhiteSpace(message)
                ? $"Maxio request failed with status {(int)response.StatusCode}."
                : message);
        }

        if (string.IsNullOrWhiteSpace(payload))
        {
            return default;
        }

        return JsonSerializer.Deserialize<T>(payload, MaxioJson.SerializerOptions);
    }

    private static string? TryFormatErrors(string payload)
    {
        try
        {
            using var document = JsonDocument.Parse(payload);
            if (!document.RootElement.TryGetProperty("errors", out var errors))
            {
                return null;
            }

            if (errors.ValueKind == JsonValueKind.Array)
            {
                return string.Join("; ", errors.EnumerateArray().Select(e => e.ToString()));
            }

            return errors.ToString();
        }
        catch (JsonException)
        {
            return null;
        }
    }
}

internal sealed class MaxioDuplicateSubmissionException : Exception
{
    public MaxioDuplicateSubmissionException(string payload)
        : base("Maxio rejected a duplicate uniqueness_token submission.")
    {
        Payload = payload;
    }

    public string Payload { get; }
}
