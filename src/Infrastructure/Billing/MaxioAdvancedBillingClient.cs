using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.Infrastructure.Billing;

public class MaxioAdvancedBillingClient
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<MaxioAdvancedBillingClient> _logger;

    public MaxioAdvancedBillingClient(HttpClient httpClient, ILogger<MaxioAdvancedBillingClient> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    internal static void ConfigureClient(HttpClient httpClient, MaxioOptions options)
    {
        string baseUrl;
        try
        {
            baseUrl = MaxioBaseUrl.Resolve(options);
        }
        catch (BillingUnavailableException)
        {
            baseUrl = "https://invalid.invalid";
        }

        httpClient.BaseAddress = new Uri(baseUrl.TrimEnd('/') + "/");
        httpClient.Timeout = TimeSpan.FromSeconds(30);
        httpClient.DefaultRequestHeaders.Accept.Clear();
        httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        if (string.IsNullOrWhiteSpace(options.ApiKey))
        {
            return;
        }

        var token = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{options.ApiKey}:x"));
        httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", token);
    }

    public async Task<IReadOnlyList<MaxioProduct>> ListProductsForFamilyAsync(
        string productFamilyHandle,
        CancellationToken cancellationToken)
    {
        var familyId = $"handle:{productFamilyHandle}";
        var path = $"product_families/{Uri.EscapeDataString(familyId)}/products.json?per_page=200&include_archived=false";
        var envelopes = await GetAsync<List<MaxioProductEnvelope>>(path, cancellationToken);
        var products = new List<MaxioProduct>();
        if (envelopes is null)
        {
            return products;
        }

        foreach (var envelope in envelopes)
        {
            if (envelope.Product is not null)
            {
                products.Add(envelope.Product);
            }
        }

        return products;
    }

    public async Task<MaxioCustomer?> FindCustomerByReferenceAsync(string reference, CancellationToken cancellationToken)
    {
        var path = $"customers/lookup.json?reference={Uri.EscapeDataString(reference)}";
        return (await GetAsync<MaxioCustomerEnvelope>(path, cancellationToken, allowNotFound: true))?.Customer;
    }

    public async Task<MaxioCustomer> CreateCustomerAsync(MaxioCustomer customer, CancellationToken cancellationToken)
    {
        var created = await PostAsync<MaxioCreateCustomerRequest, MaxioCustomerEnvelope>(
            "customers.json",
            new MaxioCreateCustomerRequest { Customer = customer },
            cancellationToken);
        if (created?.Customer is null)
        {
            throw new BillingUnavailableException("Maxio did not return a customer after create.");
        }

        return created.Customer;
    }

    public async Task<IReadOnlyList<MaxioSubscription>> ListCustomerSubscriptionsAsync(
        long customerId,
        CancellationToken cancellationToken)
    {
        var envelopes = await GetAsync<List<MaxioSubscriptionEnvelope>>(
            $"customers/{customerId}/subscriptions.json",
            cancellationToken);
        var subscriptions = new List<MaxioSubscription>();
        if (envelopes is null)
        {
            return subscriptions;
        }

        foreach (var envelope in envelopes)
        {
            if (envelope.Subscription is not null)
            {
                subscriptions.Add(envelope.Subscription);
            }
        }

        return subscriptions;
    }

    public async Task<MaxioSubscription?> FindSubscriptionByReferenceAsync(
        string reference,
        CancellationToken cancellationToken)
    {
        var path = $"subscriptions/lookup.json?reference={Uri.EscapeDataString(reference)}";
        return (await GetAsync<MaxioSubscriptionEnvelope>(path, cancellationToken, allowNotFound: true))?.Subscription;
    }

    public async Task<MaxioSubscription> CreateSubscriptionAsync(
        MaxioCreateSubscription subscription,
        CancellationToken cancellationToken)
    {
        var created = await PostAsync<MaxioCreateSubscriptionRequest, MaxioSubscriptionEnvelope>(
            "subscriptions.json",
            new MaxioCreateSubscriptionRequest { Subscription = subscription },
            cancellationToken);
        if (created?.Subscription is null)
        {
            throw new BillingUnavailableException("Maxio did not return a subscription after create.");
        }

        return created.Subscription;
    }

    private async Task<T?> GetAsync<T>(string relativePath, CancellationToken cancellationToken, bool allowNotFound = false)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, relativePath);
        return await SendAsync<T>(request, cancellationToken, allowNotFound);
    }

    private async Task<TResponse?> PostAsync<TRequest, TResponse>(
        string relativePath,
        TRequest body,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, relativePath)
        {
            Content = new StringContent(
                JsonSerializer.Serialize(body, MaxioJson.SerializerOptions),
                Encoding.UTF8,
                "application/json")
        };
        return await SendAsync<TResponse>(request, cancellationToken);
    }

    private async Task<T?> SendAsync<T>(
        HttpRequestMessage request,
        CancellationToken cancellationToken,
        bool allowNotFound = false)
    {
        HttpResponseMessage response;
        try
        {
            response = await _httpClient.SendAsync(request, cancellationToken);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            _logger.LogWarning(ex, "Maxio request to {Path} failed.", request.RequestUri?.PathAndQuery);
            throw new BillingUnavailableException("Unable to reach Maxio Advanced Billing.");
        }

        using (response)
        {
            var payload = await response.Content.ReadAsStringAsync(cancellationToken);

            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                if (allowNotFound)
                {
                    return default;
                }

                throw new BillingNotFoundException("The requested Maxio resource was not found.");
            }

            if (response.StatusCode == HttpStatusCode.Unauthorized ||
                response.StatusCode == HttpStatusCode.Forbidden)
            {
                _logger.LogWarning("Maxio rejected the request with {StatusCode}.", (int)response.StatusCode);
                throw new BillingUnavailableException("Maxio Advanced Billing rejected the API credentials.");
            }

            if (response.StatusCode == HttpStatusCode.UnprocessableEntity)
            {
                throw new BillingValidationException(ParseErrorMessage(payload));
            }

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning(
                    "Maxio returned {StatusCode} for {Path}.",
                    (int)response.StatusCode,
                    request.RequestUri?.PathAndQuery);
                throw new BillingUnavailableException(
                    $"Maxio Advanced Billing returned {(int)response.StatusCode}.");
            }

            if (string.IsNullOrWhiteSpace(payload))
            {
                return default;
            }

            try
            {
                return JsonSerializer.Deserialize<T>(payload, MaxioJson.SerializerOptions);
            }
            catch (JsonException ex)
            {
                _logger.LogWarning(ex, "Failed to deserialize Maxio response for {Path}.", request.RequestUri?.PathAndQuery);
                throw new BillingUnavailableException("Maxio Advanced Billing returned an unexpected response.");
            }
        }
    }

    internal static string ParseErrorMessage(string payload)
    {
        if (string.IsNullOrWhiteSpace(payload))
        {
            return "The billing request was rejected.";
        }

        try
        {
            using var document = JsonDocument.Parse(payload);
            var root = document.RootElement;
            if (root.TryGetProperty("errors", out var errors))
            {
                var messages = FlattenErrors(errors);
                if (messages.Count > 0)
                {
                    return string.Join(" ", messages);
                }
            }

            if (root.TryGetProperty("error", out var error) && error.ValueKind == JsonValueKind.String)
            {
                var message = error.GetString();
                if (!string.IsNullOrWhiteSpace(message))
                {
                    return message;
                }
            }
        }
        catch (JsonException)
        {
            // Fall through to a generic message rather than leaking a raw payload.
        }

        return "The billing request was rejected.";
    }

    private static List<string> FlattenErrors(JsonElement errors)
    {
        var messages = new List<string>();
        switch (errors.ValueKind)
        {
            case JsonValueKind.Array:
                foreach (var item in errors.EnumerateArray())
                {
                    if (item.ValueKind == JsonValueKind.String)
                    {
                        var value = item.GetString();
                        if (!string.IsNullOrWhiteSpace(value))
                        {
                            messages.Add(value);
                        }
                    }
                }
                break;
            case JsonValueKind.Object:
                foreach (var property in errors.EnumerateObject())
                {
                    if (property.Value.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var item in property.Value.EnumerateArray())
                        {
                            if (item.ValueKind == JsonValueKind.String)
                            {
                                messages.Add($"{property.Name} {item.GetString()}".Trim());
                            }
                        }
                    }
                    else if (property.Value.ValueKind == JsonValueKind.String)
                    {
                        messages.Add($"{property.Name} {property.Value.GetString()}".Trim());
                    }
                }
                break;
            case JsonValueKind.String:
                var single = errors.GetString();
                if (!string.IsNullOrWhiteSpace(single))
                {
                    messages.Add(single);
                }
                break;
        }

        return messages;
    }
}
