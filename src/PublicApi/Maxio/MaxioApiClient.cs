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

public sealed class MaxioApiClient : IMaxioApiClient
{
    public const string RemittanceCollectionMethod = "remittance";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly HttpClient _http;
    private readonly MaxioOptions _options;
    private readonly string _productFamilyPath;

    public MaxioApiClient(HttpClient http, IOptions<MaxioOptions> options)
    {
        _http = http;
        _options = options.Value;
        _productFamilyPath = $"product_families/handle:{_options.ProductFamilyHandle}";
    }

    public async Task<IReadOnlyList<MaxioProduct>> ListPlansAsync(CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"{_productFamilyPath}/products.json?per_page=200&include_archived=false");
        var body = await SendAsync(request, cancellationToken);
        var response = JsonSerializer.Deserialize<List<MaxioProductResponse>>(body, JsonOptions) ?? new List<MaxioProductResponse>();
        return response
            .Select(item => item.Product)
            .Where(product => product is not null)
            .Cast<MaxioProduct>()
            .ToList();
    }

    public async Task<MaxioCustomer?> FindCustomerByReferenceAsync(string reference, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"customers/lookup.json?reference={Uri.EscapeDataString(reference)}");
        var response = await SendAsync(request, cancellationToken, notFoundIsNull: true);
        if (response is null)
        {
            return null;
        }

        var parsed = JsonSerializer.Deserialize<MaxioCustomerResponse>(response, JsonOptions);
        return parsed?.Customer;
    }

    public async Task<MaxioCustomer> CreateCustomerAsync(MaxioCustomerInput customer, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "customers.json")
        {
            Content = new StringContent(JsonSerializer.Serialize(new MaxioCreateCustomerRequest { Customer = customer }, JsonOptions), Encoding.UTF8, "application/json")
        };
        var body = await SendAsync(request, cancellationToken);
        var parsed = JsonSerializer.Deserialize<MaxioCustomerResponse>(body, JsonOptions);
        return parsed?.Customer ?? throw new MaxioApiException(HttpStatusCode.OK, "Maxio returned an empty customer payload.");
    }

    public async Task<IReadOnlyList<MaxioSubscription>> ListCustomerSubscriptionsAsync(long customerId, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"customers/{customerId}/subscriptions.json?per_page=200");
        var body = await SendAsync(request, cancellationToken);
        var response = JsonSerializer.Deserialize<List<MaxioSubscriptionResponse>>(body, JsonOptions) ?? new List<MaxioSubscriptionResponse>();
        return response
            .Select(item => item.Subscription)
            .Where(subscription => subscription is not null)
            .Cast<MaxioSubscription>()
            .ToList();
    }

    public async Task<MaxioSubscription> CreateSubscriptionAsync(MaxioSubscriptionInput subscription, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "subscriptions.json")
        {
            Content = new StringContent(JsonSerializer.Serialize(new MaxioCreateSubscriptionRequest { Subscription = subscription }, JsonOptions), Encoding.UTF8, "application/json")
        };
        var body = await SendAsync(request, cancellationToken);
        var parsed = JsonSerializer.Deserialize<MaxioSubscriptionResponse>(body, JsonOptions);
        return parsed?.Subscription ?? throw new MaxioApiException(HttpStatusCode.Created, "Maxio returned an empty subscription payload.");
    }

    private async Task<string?> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken, bool notFoundIsNull = false)
    {
        ApplyCredentials(request);
        using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);

        if (notFoundIsNull && response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new MaxioApiException(response.StatusCode, ExtractMessage(response.StatusCode, body));
        }

        return body;
    }

    private void ApplyCredentials(HttpRequestMessage request)
    {
        var credentials = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{_options.ApiKey}:x"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", credentials);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    }

    private static string ExtractMessage(HttpStatusCode statusCode, string body)
    {
        var message = $"Maxio request failed with HTTP {(int)statusCode}.";
        if (string.IsNullOrWhiteSpace(body))
        {
            return message;
        }

        try
        {
            using var document = JsonDocument.Parse(body);
            if (document.RootElement.ValueKind == JsonValueKind.Object &&
                document.RootElement.TryGetProperty("errors", out var errors))
            {
                var details = ReadErrorDetails(errors);
                if (details.Count > 0)
                {
                    return $"{message} {string.Join(" ", details)}";
                }
            }
        }
        catch (JsonException)
        {
        }

        return $"{message} {body}";
    }

    private static List<string> ReadErrorDetails(JsonElement errors)
    {
        var details = new List<string>();
        if (errors.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in errors.EnumerateArray())
            {
                var text = item.ValueKind == JsonValueKind.String ? item.GetString() : item.GetRawText();
                if (!string.IsNullOrWhiteSpace(text))
                {
                    details.Add(text);
                }
            }
        }
        else if (errors.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in errors.EnumerateObject())
            {
                var text = property.Value.ValueKind == JsonValueKind.String ? property.Value.GetString() : property.Value.GetRawText();
                if (!string.IsNullOrWhiteSpace(text))
                {
                    details.Add($"{property.Name}: {text}");
                }
            }
        }

        return details;
    }
}
