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

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// HTTP client for the Maxio Advanced Billing JSON API.
/// Authentication is HTTP Basic with the API key as username and the literal "x" as password,
/// as required by the Advanced Billing API (verified against a live sandbox).
/// </summary>
public class MaxioClient : IMaxioClient
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly HttpClient _httpClient;

    public MaxioClient(HttpClient httpClient, Microsoft.Extensions.Options.IOptions<MaxioOptions> options)
    {
        var maxioOptions = options.Value;
        var missing = maxioOptions.GetMissingRequiredKeys();
        if (missing.Count > 0)
        {
            throw new InvalidOperationException(
                $"Maxio configuration is missing required keys: {string.Join(", ", missing)}. " +
                "Provide them via user-secrets or environment variables (MAXIO_API_KEY, MAXIO_SITE_SUBDOMAIN, MAXIO_DEFAULT_PRODUCT_FAMILY).");
        }

        _httpClient = httpClient;
        _httpClient.BaseAddress = new Uri(maxioOptions.GetBaseUrl() + "/");
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Basic",
            Convert.ToBase64String(Encoding.ASCII.GetBytes($"{maxioOptions.ApiKey}:x")));
        _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    }

    public async Task<MaxioProductFamily?> FindProductFamilyByHandleAsync(string handle, CancellationToken cancellationToken)
    {
        // product_families are read by listing all and matching on the stable handle;
        // the API has no direct by-handle read for families.
        var families = await GetListAsync<MaxioProductFamilyEnvelope, MaxioProductFamily>(
            "product_families.json",
            e => e.ProductFamily!,
            cancellationToken);
        return families.Select(f => f).FirstOrDefault(f => f.Handle == handle);
    }

    public Task<IReadOnlyList<MaxioProduct>> ListProductsInFamilyAsync(int productFamilyId, CancellationToken cancellationToken)
    {
        return GetListAsync<MaxioProductEnvelope, MaxioProduct>(
            $"product_families/{productFamilyId}/products.json",
            e => e.Product!, cancellationToken);
    }

    public async Task<MaxioProduct?> FindProductByHandleAsync(string handle, CancellationToken cancellationToken)
    {
        var envelope = await GetAsync<MaxioProductEnvelope>($"products/handle/{Uri.EscapeDataString(handle)}.json", treatNotFoundAsNull: true, cancellationToken);
        return envelope?.Product;
    }

    public async Task<MaxioCustomer?> FindCustomerByReferenceAsync(string reference, CancellationToken cancellationToken)
    {
        var envelope = await GetAsync<MaxioCustomerEnvelope>(
            $"customers/lookup.json?reference={Uri.EscapeDataString(reference)}",
            treatNotFoundAsNull: true,
            cancellationToken);
        return envelope?.Customer;
    }

    public async Task<MaxioCustomer> CreateCustomerAsync(MaxioCreateCustomerRequest request, CancellationToken cancellationToken)
    {
        var envelope = await SendAsync<MaxioCustomerEnvelope>(
            HttpMethod.Post,
            "customers.json",
            new MaxioCreateCustomerEnvelope { Customer = request },
            "creating the Maxio customer",
            cancellationToken);
        return envelope.Customer!;
    }

    public Task<IReadOnlyList<MaxioSubscription>> ListCustomerSubscriptionsAsync(int customerId, CancellationToken cancellationToken)
    {
        return GetListAsync<MaxioSubscriptionEnvelope, MaxioSubscription>(
            $"customers/{customerId}/subscriptions.json",
            e => e.Subscription!,
            cancellationToken);
    }

    public async Task<MaxioSubscription> CreateSubscriptionAsync(MaxioCreateSubscriptionRequest request, CancellationToken cancellationToken)
    {
        var envelope = await SendAsync<MaxioSubscriptionEnvelope>(
            HttpMethod.Post,
            "subscriptions.json",
            new MaxioCreateSubscriptionEnvelope { Subscription = request },
            "creating the Maxio subscription",
            cancellationToken);
        return envelope.Subscription!;
    }

    private async Task<IReadOnlyList<TModel>> GetListAsync<TEnvelope, TModel>(
        string relativeUrl,
        Func<TEnvelope, TModel> unwrap,
        CancellationToken cancellationToken)
        where TEnvelope : class
    {
        using var response = await _httpClient.GetAsync(relativeUrl, cancellationToken);
        var content = await response.Content.ReadAsStringAsync(cancellationToken);
        await EnsureSuccessAsync(response, content, cancellationToken);

        var envelopes = JsonSerializer.Deserialize<List<TEnvelope>>(content, SerializerOptions) ?? new List<TEnvelope>();
        return envelopes.Select(unwrap).ToList();
    }

    private async Task<TEnvelope?> GetAsync<TEnvelope>(string relativeUrl, bool treatNotFoundAsNull, CancellationToken cancellationToken)
        where TEnvelope : class
    {
        using var response = await _httpClient.GetAsync(relativeUrl, cancellationToken);
        var content = await response.Content.ReadAsStringAsync(cancellationToken);

        if (treatNotFoundAsNull && response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        await EnsureSuccessAsync(response, content, cancellationToken);
        return JsonSerializer.Deserialize<TEnvelope>(content, SerializerOptions);
    }

    private async Task<TEnvelope> SendAsync<TEnvelope>(
        HttpMethod method,
        string relativeUrl,
        object payload,
        string context,
        CancellationToken cancellationToken)
        where TEnvelope : class
    {
        var json = JsonSerializer.Serialize(payload, payload.GetType(), SerializerOptions);
        using var requestMessage = new HttpRequestMessage(method, relativeUrl)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
        using var response = await _httpClient.SendAsync(requestMessage, cancellationToken);
        var content = await response.Content.ReadAsStringAsync(cancellationToken);
        await EnsureSuccessAsync(response, content, cancellationToken);

        return JsonSerializer.Deserialize<TEnvelope>(content, SerializerOptions)
            ?? throw new MaxioApiException((int)response.StatusCode, new[] { "Maxio returned an empty payload." }, context);
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, string content, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var errors = new List<string>();
        try
        {
            var errorEnvelope = JsonSerializer.Deserialize<MaxioErrorEnvelope>(content, SerializerOptions);
            if (errorEnvelope?.Errors is { Count: > 0 })
            {
                errors.AddRange(errorEnvelope.Errors);
            }
        }
        catch (JsonException)
        {
            // non-JSON error body; fall through with empty errors list
        }

        if (errors.Count == 0 && !string.IsNullOrWhiteSpace(content))
        {
            errors.Add(content.Length > 512 ? content[..512] : content);
        }

        await Task.CompletedTask;
        throw new MaxioApiException((int)response.StatusCode, errors);
    }

    #region Request envelopes

    private sealed class MaxioCreateCustomerEnvelope
    {
        public MaxioCreateCustomerRequest? Customer { get; set; }
    }

    private sealed class MaxioCreateSubscriptionEnvelope
    {
        public MaxioCreateSubscriptionRequest? Subscription { get; set; }
    }

    #endregion
}
