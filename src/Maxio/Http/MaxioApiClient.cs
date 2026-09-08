using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.Maxio.Configuration;

namespace Microsoft.eShopWeb.Maxio.Http;

/// <summary>
/// REST client for Maxio Advanced Billing. Uses HTTP Basic authentication with the API
/// key as the username and "x" as the password (the documented Advanced Billing scheme),
/// and targets the JSON endpoints on the site derived from <see cref="MaxioOptions"/>.
/// </summary>
public sealed class MaxioApiClient : IMaxioApiClient
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    private readonly HttpClient _http;

    public MaxioApiClient(HttpClient httpClient, MaxioOptions options)
    {
        _http = httpClient;
        var baseUrl = options.GetApiBaseUrl();
        if (_http.BaseAddress is null)
        {
            _http.BaseAddress = new Uri(baseUrl);
        }

        var credentials = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{options.ApiKey}:x"));
        _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", credentials);
        _http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    }

    public async Task<MaxioSite> GetSiteAsync(CancellationToken cancellationToken)
    {
        var envelope = await GetAsync<MaxioSiteEnvelope>("/site.json", cancellationToken).ConfigureAwait(false);
        return envelope.Site;
    }

    public async Task<IReadOnlyList<MaxioProductFamily>> ListProductFamiliesAsync(CancellationToken cancellationToken)
    {
        var envelopes = await GetListAsync<MaxioProductFamilyEnvelope>("/product_families.json", cancellationToken).ConfigureAwait(false);
        var families = new List<MaxioProductFamily>(envelopes.Count);
        foreach (var envelope in envelopes)
        {
            families.Add(envelope.ProductFamily);
        }

        return families;
    }

    public async Task<IReadOnlyList<MaxioProduct>> ListProductsByFamilyAsync(long productFamilyId, CancellationToken cancellationToken)
    {
        var envelopes = await GetListAsync<MaxioProductEnvelope>($"/product_families/{productFamilyId}/products.json", cancellationToken).ConfigureAwait(false);
        var products = new List<MaxioProduct>(envelopes.Count);
        foreach (var envelope in envelopes)
        {
            products.Add(envelope.Product);
        }

        return products;
    }

    public async Task<MaxioCustomer?> FindCustomerByReferenceAsync(string reference, CancellationToken cancellationToken)
    {
        var encodedReference = Uri.EscapeDataString(reference);
        var response = await SendAsync(HttpMethod.Get, $"/customers/lookup.json?reference={encodedReference}", null, cancellationToken).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        EnsureSuccess(response);
        var envelope = Deserialize<MaxioCustomerEnvelope>(response.Body);
        return envelope.Customer;
    }

    public async Task<MaxioCustomer> CreateCustomerAsync(MaxioNewCustomer customer, CancellationToken cancellationToken)
    {
        var payload = new MaxioCreateCustomerEnvelope { Customer = customer };
        var response = await SendAsync(HttpMethod.Post, "/customers.json", payload, cancellationToken).ConfigureAwait(false);
        EnsureSuccess(response);
        var envelope = Deserialize<MaxioCustomerEnvelope>(response.Body);
        return envelope.Customer;
    }

    public async Task<IReadOnlyList<MaxioSubscription>> ListCustomerSubscriptionsAsync(long customerId, CancellationToken cancellationToken)
    {
        var envelopes = await GetListAsync<MaxioSubscriptionEnvelope>($"/customers/{customerId}/subscriptions.json", cancellationToken).ConfigureAwait(false);
        var subscriptions = new List<MaxioSubscription>(envelopes.Count);
        foreach (var envelope in envelopes)
        {
            subscriptions.Add(envelope.Subscription);
        }

        return subscriptions;
    }

    public async Task<MaxioSubscription> CreateSubscriptionAsync(MaxioNewSubscription subscription, CancellationToken cancellationToken)
    {
        var payload = new MaxioCreateSubscriptionEnvelope { Subscription = subscription };
        var response = await SendAsync(HttpMethod.Post, "/subscriptions.json", payload, cancellationToken).ConfigureAwait(false);
        EnsureSuccess(response);
        var envelope = Deserialize<MaxioSubscriptionEnvelope>(response.Body);
        return envelope.Subscription;
    }

    private async Task<TEnvelope> GetAsync<TEnvelope>(string path, CancellationToken cancellationToken)
    {
        var response = await SendAsync(HttpMethod.Get, path, null, cancellationToken).ConfigureAwait(false);
        EnsureSuccess(response);
        return Deserialize<TEnvelope>(response.Body);
    }

    private async Task<List<TEnvelope>> GetListAsync<TEnvelope>(string path, CancellationToken cancellationToken)
    {
        var response = await SendAsync(HttpMethod.Get, path, null, cancellationToken).ConfigureAwait(false);
        EnsureSuccess(response);
        return Deserialize<List<TEnvelope>>(response.Body);
    }

    private async Task<MaxioResponse> SendAsync(HttpMethod method, string path, object? body, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, path);
        if (body is not null)
        {
            request.Content = JsonContent.Create(body, options: JsonOptions);
        }

        using var response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        var content = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        return new MaxioResponse(response.StatusCode, content);
    }

    private static void EnsureSuccess(MaxioResponse response)
    {
        int status = (int)response.StatusCode;
        if (status is >= 200 and < 300)
        {
            return;
        }

        throw new MaxioApiException(response.StatusCode, "Maxio Advanced Billing request failed.", ParseErrors(response.Body));
    }

    private static IReadOnlyList<string>? ParseErrors(string body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(body);
            if (document.RootElement.TryGetProperty("errors", out var errorsElement))
            {
                if (errorsElement.ValueKind == JsonValueKind.Array)
                {
                    var errors = new List<string>();
                    foreach (var item in errorsElement.EnumerateArray())
                    {
                        errors.Add(item.ValueKind == JsonValueKind.String ? item.GetString() ?? string.Empty : item.ToString());
                    }

                    return errors;
                }

                if (errorsElement.ValueKind == JsonValueKind.String)
                {
                    return new List<string> { errorsElement.GetString() ?? string.Empty };
                }
            }
        }
        catch (JsonException)
        {
            // Not JSON; fall through and surface the raw body.
        }

        return new List<string> { body };
    }

    private static T Deserialize<T>(string body)
    {
        return JsonSerializer.Deserialize<T>(body, JsonOptions)
            ?? throw new MaxioApiException(HttpStatusCode.BadGateway, "Maxio Advanced Billing returned an empty response body.");
    }

    private sealed class MaxioResponse
    {
        public MaxioResponse(HttpStatusCode statusCode, string body)
        {
            StatusCode = statusCode;
            Body = body;
        }

        public HttpStatusCode StatusCode { get; }

        public string Body { get; }
    }
}
