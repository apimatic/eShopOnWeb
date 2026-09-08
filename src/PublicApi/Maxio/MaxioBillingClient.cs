using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

public sealed class MaxioBillingClient : IMaxioBillingClient
{
    private const int ProductsPerPage = 200;

    private static readonly JsonSerializerOptions ReadOptions = new(JsonSerializerDefaults.Web);
    private static readonly JsonSerializerOptions WriteOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly HttpClient _httpClient;
    private readonly MaxioOptions _options;
    private readonly ILogger<MaxioBillingClient> _logger;

    public MaxioBillingClient(HttpClient httpClient, IOptions<MaxioOptions> options, ILogger<MaxioBillingClient> logger)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<SiteData> GetSiteAsync(CancellationToken cancellationToken = default)
    {
        using var response = await SendAsync(HttpMethod.Get, "site.json", null, cancellationToken);
        return (await ReadAsync<SiteEnvelope>(response, cancellationToken)).Site
            ?? throw new MaxioApiException(null, "The Maxio site endpoint returned an unexpected payload.");
    }

    public async Task<IReadOnlyList<MaxioProduct>> ListProductsAsync(CancellationToken cancellationToken = default)
    {
        EnsureConfigured();
        if (string.IsNullOrWhiteSpace(_options.ProductFamilyHandle))
        {
            throw new InvalidOperationException(
                "Maxio integration is not configured to browse plans. Set Maxio:ProductFamilyHandle (from MAXIO_DEFAULT_PRODUCT_FAMILY).");
        }

        var products = new List<MaxioProduct>();
        var page = 1;
        while (true)
        {
            var path = $"product_families/handle:{Uri.EscapeDataString(_options.ProductFamilyHandle)}/products.json?per_page={ProductsPerPage}&page={page}";
            using var response = await SendAsync(HttpMethod.Get, path, null, cancellationToken);
            var envelopes = await ReadAsync<ProductEnvelope[]>(response, cancellationToken);

            foreach (var envelope in envelopes)
            {
                if (envelope.Product is not null)
                {
                    products.Add(envelope.Product);
                }
            }

            if (envelopes.Length < ProductsPerPage)
            {
                break;
            }

            page++;
        }

        return products;
    }

    public async Task<MaxioCustomer?> FindCustomerByReferenceAsync(string reference, CancellationToken cancellationToken = default)
    {
        try
        {
            var path = $"customers/lookup.json?reference={Uri.EscapeDataString(reference)}";
            using var response = await SendAsync(HttpMethod.Get, path, null, cancellationToken);
            return (await ReadAsync<CustomerEnvelope>(response, cancellationToken)).Customer;
        }
        catch (MaxioApiException ex) when (ex.StatusCode == 404)
        {
            return null;
        }
    }

    public async Task<MaxioCustomer> CreateCustomerAsync(MaxioCustomerWrite customer, string? uniquenessToken, CancellationToken cancellationToken = default)
    {
        var payload = new CreateCustomerPayload
        {
            Customer = customer,
            UniquenessToken = uniquenessToken
        };

        using var response = await SendAsync(HttpMethod.Post, "customers.json", payload, cancellationToken);
        return (await ReadAsync<CustomerEnvelope>(response, cancellationToken)).Customer
            ?? throw new MaxioApiException(null, "The Maxio customer endpoint returned an unexpected payload.");
    }

    public async Task<MaxioSubscription?> FindSubscriptionByReferenceAsync(string reference, CancellationToken cancellationToken = default)
    {
        try
        {
            var path = $"subscriptions/lookup.json?reference={Uri.EscapeDataString(reference)}";
            using var response = await SendAsync(HttpMethod.Get, path, null, cancellationToken);
            return (await ReadAsync<SubscriptionEnvelope>(response, cancellationToken)).Subscription;
        }
        catch (MaxioApiException ex) when (ex.StatusCode == 404)
        {
            return null;
        }
    }

    public async Task<IReadOnlyList<MaxioSubscription>> ListSubscriptionsForCustomerAsync(long customerId, CancellationToken cancellationToken = default)
    {
        var path = $"customers/{customerId}/subscriptions.json";
        using var response = await SendAsync(HttpMethod.Get, path, null, cancellationToken);
        var envelopes = await ReadAsync<SubscriptionEnvelope[]>(response, cancellationToken);

        var subscriptions = new List<MaxioSubscription>(envelopes.Length);
        foreach (var envelope in envelopes)
        {
            if (envelope.Subscription is not null)
            {
                subscriptions.Add(envelope.Subscription);
            }
        }

        return subscriptions;
    }

    public async Task<MaxioSubscription> CreateSubscriptionAsync(MaxioSubscriptionWrite subscription, string? uniquenessToken, CancellationToken cancellationToken = default)
    {
        var payload = new CreateSubscriptionPayload
        {
            Subscription = subscription,
            UniquenessToken = uniquenessToken
        };

        using var response = await SendAsync(HttpMethod.Post, "subscriptions.json", payload, cancellationToken);
        return (await ReadAsync<SubscriptionEnvelope>(response, cancellationToken)).Subscription
            ?? throw new MaxioApiException(null, "The Maxio subscription endpoint returned an unexpected payload.");
    }

    private void EnsureConfigured()
    {
        if (!_options.IsConfigured)
        {
            throw new InvalidOperationException(
                "Maxio integration is not configured. Set Maxio:ApiKey (from MAXIO_API_KEY) and Maxio:Subdomain (from MAXIO_SITE_SUBDOMAIN).");
        }
    }

    private HttpRequestMessage BuildRequest(HttpMethod method, string pathAndQuery, object? body)
    {
        var request = new HttpRequestMessage(method, new Uri(_options.GetBaseAddress(), pathAndQuery));
        var credentials = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{_options.ApiKey}:X"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", credentials);

        if (body is not null)
        {
            request.Content = JsonContent.Create(body, options: WriteOptions);
        }

        return request;
    }

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string pathAndQuery, object? body, CancellationToken cancellationToken)
    {
        EnsureConfigured();

        using var request = BuildRequest(method, pathAndQuery, body);
        HttpResponseMessage response;
        try
        {
            response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        }
        catch (HttpRequestException ex)
        {
            throw new MaxioApiException(null, $"Unable to reach the Maxio billing API at {_options.GetBaseAddress()}. {ex.Message}");
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new MaxioApiException(null, $"The Maxio billing API request to {_options.GetBaseAddress()} timed out.");
        }

        if (response.IsSuccessStatusCode)
        {
            return response;
        }

        using (response)
        {
            var (message, errors) = await ReadErrorAsync(response, cancellationToken);
            throw new MaxioApiException((int)response.StatusCode, message, errors);
        }
    }

    private static async Task<(string Message, IReadOnlyList<string> Errors)> ReadErrorAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        string body;
        try
        {
            body = await response.Content.ReadAsStringAsync(cancellationToken);
        }
        catch
        {
            body = string.Empty;
        }

        var errors = new List<string>();
        if (!string.IsNullOrWhiteSpace(body))
        {
            try
            {
                using var document = JsonDocument.Parse(body);
                if (document.RootElement.ValueKind == JsonValueKind.Object &&
                    document.RootElement.TryGetProperty("errors", out var errorsElement))
                {
                    CollectErrors(errorsElement, errors);
                }
            }
            catch (JsonException)
            {
            }
        }

        var message = errors.Count > 0
            ? string.Join(" ", errors)
            : $"The Maxio billing API returned HTTP {(int)response.StatusCode}.";

        return (message, errors);
    }

    private static void CollectErrors(JsonElement errorsElement, List<string> errors)
    {
        switch (errorsElement.ValueKind)
        {
            case JsonValueKind.Array:
                foreach (var item in errorsElement.EnumerateArray())
                {
                    if (item.ValueKind == JsonValueKind.String)
                    {
                        errors.Add(item.GetString()!);
                    }
                    else
                    {
                        errors.Add(item.GetRawText());
                    }
                }

                break;
            case JsonValueKind.String:
                errors.Add(errorsElement.GetString()!);
                break;
            case JsonValueKind.Object:
                errors.Add(errorsElement.GetRawText());
                break;
        }
    }

    private static async Task<T> ReadAsync<T>(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        using (response)
        {
            var result = await response.Content.ReadFromJsonAsync<T>(ReadOptions, cancellationToken);
            return result ?? throw new MaxioApiException(null, "The Maxio billing API returned an empty response.");
        }
    }
}
