using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

/// <summary>
/// HTTP client for the Maxio Advanced Billing JSON API.
///
/// Authentication is HTTP Basic with the site API key. The base address is
/// <c>https://{subdomain}.chargify.com</c> unless overridden by <c>Maxio:BaseUrl</c>.
/// All request and response contracts below were verified against Maxio's published API
/// documentation and the live sandbox site.
/// </summary>
public class MaxioClient : IMaxioClient
{
    private const int MaxPageSize = 200;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly HttpClient _httpClient;
    private readonly MaxioSettings _settings;

    public MaxioClient(HttpClient httpClient, IOptions<MaxioSettings> settings)
    {
        _httpClient = httpClient;
        _settings = settings.Value;
        _settings.Validate();
    }

    public async Task<IReadOnlyList<MaxioProduct>> ListProductsAsync(CancellationToken cancellationToken)
    {
        using var response = await SendAsync(
            HttpMethod.Get,
            $"product_families/handle:{Uri.EscapeDataString(_settings.ProductFamilyHandle!)}/products.json?per_page={MaxPageSize}",
            null,
            null,
            cancellationToken);

        var envelopes = await DeserializeAsync<List<MaxioProductEnvelope>>(response, cancellationToken);
        return envelopes?
            .Select(e => e.Product)
            .Where(p => p is not null)
            .Cast<MaxioProduct>()
            .ToList() ?? new List<MaxioProduct>();
    }

    public async Task<MaxioCustomer?> FindCustomerByReferenceAsync(string reference, CancellationToken cancellationToken)
    {
        using var response = await SendAsync(
            HttpMethod.Get,
            $"customers/lookup.json?reference={Uri.EscapeDataString(reference)}",
            null,
            HttpStatusCode.NotFound,
            cancellationToken);

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        var envelope = await DeserializeAsync<MaxioCustomerEnvelope>(response, cancellationToken);
        return envelope?.Customer;
    }

    public async Task<MaxioCustomer> CreateCustomerAsync(MaxioCustomer customer, CancellationToken cancellationToken)
    {
        using var response = await SendAsync(
            HttpMethod.Post,
            "customers.json",
            new MaxioCreateCustomerRequest(customer),
            null,
            cancellationToken);

        var envelope = await DeserializeAsync<MaxioCustomerEnvelope>(response, cancellationToken);
        return envelope?.Customer ?? throw new MaxioApiException(
            (int)response.StatusCode,
            "create customer",
            new[] { "The Maxio API returned an empty customer payload." });
    }

    public async Task<IReadOnlyList<MaxioSubscription>> ListCustomerSubscriptionsAsync(long customerId, CancellationToken cancellationToken)
    {
        using var response = await SendAsync(
            HttpMethod.Get,
            $"customers/{customerId}/subscriptions.json?per_page={MaxPageSize}",
            null,
            null,
            cancellationToken);

        var envelopes = await DeserializeAsync<List<MaxioSubscriptionEnvelope>>(response, cancellationToken);
        return envelopes?
            .Select(e => e.Subscription)
            .Where(s => s is not null)
            .Cast<MaxioSubscription>()
            .ToList() ?? new List<MaxioSubscription>();
    }

    public async Task<MaxioSubscription> CreateSubscriptionAsync(MaxioCreateSubscriptionRequest request, CancellationToken cancellationToken)
    {
        using var response = await SendAsync(
            HttpMethod.Post,
            "subscriptions.json",
            request,
            null,
            cancellationToken);

        var envelope = await DeserializeAsync<MaxioSubscriptionEnvelope>(response, cancellationToken);
        return envelope?.Subscription ?? throw new MaxioApiException(
            (int)response.StatusCode,
            "create subscription",
            new[] { "The Maxio API returned an empty subscription payload." });
    }

    private async Task<HttpResponseMessage> SendAsync(
        HttpMethod method,
        string relativePath,
        object? body,
        HttpStatusCode? acceptedFailureStatus,
        CancellationToken cancellationToken)
    {
        var requestSummary = $"{method} {relativePath}";
        var payload = body is null ? null : JsonSerializer.Serialize(body, JsonOptions);

        using var request = new HttpRequestMessage(method, relativePath);
        if (payload is not null)
        {
            request.Content = new StringContent(payload, Encoding.UTF8, "application/json");
        }

        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.Authorization = new AuthenticationHeaderValue(
            "Basic",
            Convert.ToBase64String(Encoding.ASCII.GetBytes($"{_settings.ApiKey}:x")));
        request.Headers.UserAgent.Add(new ProductInfoHeaderValue("eShopOnWeb-PublicApi", "1.0"));

        HttpResponseMessage response;
        try
        {
            response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new MaxioApiException(0, requestSummary, new[] { "The Maxio API did not respond before the timeout expired." });
        }
        catch (HttpRequestException ex)
        {
            throw new MaxioApiException(0, requestSummary, new[] { $"The Maxio API could not be reached: {ex.Message}" });
        }

        if (response.IsSuccessStatusCode || response.StatusCode == acceptedFailureStatus)
        {
            return response;
        }

        var errors = await TryReadErrorsAsync(response, cancellationToken);
        response.Dispose();
        throw new MaxioApiException((int)response.StatusCode, requestSummary, errors);
    }

    private static async Task<T?> DeserializeAsync<T>(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        return await JsonSerializer.DeserializeAsync<T>(stream, JsonOptions, cancellationToken);
    }

    private static async Task<IReadOnlyList<string>> TryReadErrorsAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        string content;
        try
        {
            content = await response.Content.ReadAsStringAsync(cancellationToken);
        }
        catch
        {
            return new[] { $"HTTP {(int)response.StatusCode} {response.ReasonPhrase}" };
        }

        if (string.IsNullOrWhiteSpace(content))
        {
            return new[] { $"HTTP {(int)response.StatusCode} {response.ReasonPhrase}" };
        }

        try
        {
            using var document = JsonDocument.Parse(content);
            if (document.RootElement.ValueKind == JsonValueKind.Object &&
                document.RootElement.TryGetProperty("errors", out var errors))
            {
                if (errors.ValueKind == JsonValueKind.Array)
                {
                    var messages = errors.EnumerateArray()
                        .Where(e => e.ValueKind == JsonValueKind.String)
                        .Select(e => e.GetString())
                        .Where(m => !string.IsNullOrWhiteSpace(m))
                        .Cast<string>()
                        .ToList();
                    if (messages.Any())
                    {
                        return messages;
                    }
                }
                else if (errors.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(errors.GetString()))
                {
                    return new[] { errors.GetString()! };
                }
            }
        }
        catch (JsonException)
        {
            // Not JSON; fall back to the raw body below.
        }

        return new[] { content.Trim() };
    }
}
