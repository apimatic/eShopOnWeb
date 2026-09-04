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
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

public sealed class MaxioBillingClient : IMaxioBillingClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly HttpClient _httpClient;
    private readonly IOptions<MaxioOptions> _options;
    private readonly SemaphoreSlim _concurrencyGate = new(4, 4);

    public MaxioBillingClient(HttpClient httpClient, IOptions<MaxioOptions> options)
    {
        _httpClient = httpClient;
        _options = options;
        _httpClient.Timeout = TimeSpan.FromSeconds(30);
    }

    public async Task<IReadOnlyList<MaxioProduct>> ListPlansAsync(CancellationToken cancellationToken)
    {
        var family = Uri.EscapeDataString($"handle:{_options.Value.ProductFamilyHandle}");
        var response = await SendAsync(HttpMethod.Get,
            $"product_families/{family}/products.json?per_page=200&include_archived=false",
            null,
            cancellationToken);

        var products = await response!.Content.ReadFromJsonAsync<List<MaxioProductEnvelope>>(JsonOptions, cancellationToken)
            ?? new List<MaxioProductEnvelope>();

        return products
            .Where(x => x.Product is not null && x.Product.ArchivedAt is null && !string.IsNullOrWhiteSpace(x.Product.Handle))
            .Select(x => x.Product!)
            .ToArray();
    }

    public async Task<MaxioCustomer?> FindCustomerByReferenceAsync(string reference, CancellationToken cancellationToken)
    {
        using var response = await SendAsync(HttpMethod.Get,
            $"customers/lookup.json?reference={Uri.EscapeDataString(reference)}",
            null,
            cancellationToken,
            treatNotFoundAsNull: true);

        if (response is null)
        {
            return null;
        }

        var envelope = await response.Content.ReadFromJsonAsync<MaxioCustomerEnvelope>(JsonOptions, cancellationToken);
        return envelope?.Customer;
    }

    public async Task<MaxioCustomer> CreateCustomerAsync(
        string reference,
        string firstName,
        string lastName,
        string email,
        string uniquenessToken,
        CancellationToken cancellationToken)
    {
        var body = new
        {
            customer = new
            {
                first_name = firstName,
                last_name = lastName,
                email,
                reference
            },
            uniqueness_token = uniquenessToken
        };

        using var response = await SendAsync(HttpMethod.Post, "customers.json", body, cancellationToken);
        var envelope = await response!.Content.ReadFromJsonAsync<MaxioCustomerEnvelope>(JsonOptions, cancellationToken)
            ?? throw new MaxioApiException((int)response.StatusCode, "Maxio returned an empty customer response.");
        return envelope.Customer;
    }

    public async Task<MaxioSubscription?> FindSubscriptionByReferenceAsync(string reference, CancellationToken cancellationToken)
    {
        using var response = await SendAsync(HttpMethod.Get,
            $"subscriptions/lookup.json?reference={Uri.EscapeDataString(reference)}",
            null,
            cancellationToken,
            treatNotFoundAsNull: true);

        if (response is null)
        {
            return null;
        }

        var envelope = await response.Content.ReadFromJsonAsync<MaxioSubscriptionEnvelope>(JsonOptions, cancellationToken);
        return envelope?.Subscription;
    }

    public async Task<MaxioSubscription> CreateSubscriptionAsync(
        string reference,
        string productHandle,
        int customerId,
        string uniquenessToken,
        CancellationToken cancellationToken)
    {
        var body = new
        {
            subscription = new
            {
                product_handle = productHandle,
                customer_id = customerId,
                reference,
                payment_collection_method = "remittance"
            },
            uniqueness_token = uniquenessToken
        };

        using var response = await SendAsync(HttpMethod.Post, "subscriptions.json", body, cancellationToken);
        var envelope = await response!.Content.ReadFromJsonAsync<MaxioSubscriptionEnvelope>(JsonOptions, cancellationToken)
            ?? throw new MaxioApiException((int)response.StatusCode, "Maxio returned an empty subscription response.");
        return envelope.Subscription;
    }

    public async Task<IReadOnlyList<MaxioSubscription>> ListCustomerSubscriptionsAsync(
        int customerId,
        CancellationToken cancellationToken)
    {
        using var response = await SendAsync(HttpMethod.Get,
            $"customers/{customerId}/subscriptions.json",
            null,
            cancellationToken);

        var envelopes = await response!.Content.ReadFromJsonAsync<List<MaxioSubscriptionEnvelope>>(JsonOptions, cancellationToken)
            ?? new List<MaxioSubscriptionEnvelope>();
        return envelopes.Select(x => x.Subscription).ToArray();
    }

    private async Task<HttpResponseMessage?> SendAsync(
        HttpMethod method,
        string path,
        object? body,
        CancellationToken cancellationToken,
        bool treatNotFoundAsNull = false)
    {
        if (!_options.Value.IsConfigured)
        {
            throw new MaxioApiException(StatusCodes.Status503ServiceUnavailable, "Maxio billing is not configured.");
        }

        using var request = new HttpRequestMessage(method, new Uri(_options.Value.GetApiBaseUri(), path));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        var credential = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{_options.Value.ApiKey}:X"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", credential);
        if (body is not null)
        {
            request.Content = JsonContent.Create(body, options: JsonOptions);
        }

        await _concurrencyGate.WaitAsync(cancellationToken);
        try
        {
            var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                return response;
            }

            if (treatNotFoundAsNull && response.StatusCode == HttpStatusCode.NotFound)
            {
                response.Dispose();
                return null;
            }

            var statusCode = (int)response.StatusCode;
            var message = await GetSafeErrorMessageAsync(response, cancellationToken);
            response.Dispose();
            throw new MaxioApiException(statusCode, message);
        }
        finally
        {
            _concurrencyGate.Release();
        }
    }

    private static async Task<string> GetSafeErrorMessageAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.StatusCode == HttpStatusCode.TooManyRequests)
        {
            return "Maxio is rate limiting requests. Please retry shortly.";
        }

        try
        {
            var error = await response.Content.ReadFromJsonAsync<MaxioApiErrorResponse>(JsonOptions, cancellationToken);
            if (error?.Errors is JsonElement element)
            {
                return element.ValueKind == JsonValueKind.Array
                    ? string.Join(" ", element.EnumerateArray().Select(x => x.GetString()).Where(x => !string.IsNullOrWhiteSpace(x)))
                    : element.ToString();
            }
        }
        catch (JsonException)
        {
            // Keep provider response details out of the public response when it is not JSON.
        }

        return $"Maxio request failed with status {(int)response.StatusCode}.";
    }
}
