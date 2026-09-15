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

namespace Microsoft.eShopWeb.PublicApi.Maxio;

public sealed class MaxioBillingClient : IMaxioBillingClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly HttpClient _httpClient;
    private readonly MaxioOptions _options;

    public MaxioBillingClient(HttpClient httpClient, Microsoft.Extensions.Options.IOptions<MaxioOptions> options)
    {
        _httpClient = httpClient;
        _options = options.Value;
    }

    public async Task<IReadOnlyList<MaxioProduct>> ListProductsAsync(string productFamilyHandle, CancellationToken cancellationToken)
    {
        var response = await SendListAsync<MaxioProduct>(
            HttpMethod.Get,
            $"product_families/handle:{Uri.EscapeDataString(productFamilyHandle)}/products.json?per_page=200",
            null,
            "list subscription plans",
            cancellationToken);

        return response.Items
            .Where(item => item.Product is not null && !string.IsNullOrWhiteSpace(item.Product.Handle))
            .Select(item => item.Product!)
            .ToArray();
    }

    public async Task<MaxioCustomer?> FindCustomerByReferenceAsync(string reference, CancellationToken cancellationToken)
    {
        var path = $"customers/lookup.json?reference={Uri.EscapeDataString(reference)}";
        return await SendNullableAsync<MaxioCustomerResponse>(HttpMethod.Get, path, "find the Maxio customer", cancellationToken)
            is { Customer: not null } result ? result.Customer : null;
    }

    public async Task<MaxioCustomer> CreateCustomerAsync(string reference, string firstName, string lastName, string email, CancellationToken cancellationToken)
    {
        var payload = new
        {
            uniqueness_token = UniquenessToken(reference),
            customer = new
            {
                first_name = firstName,
                last_name = lastName,
                email,
                reference
            }
        };

        var response = await SendAsync<MaxioCustomerResponse>(HttpMethod.Post, "customers.json", payload, "create the Maxio customer", cancellationToken);
        return response.Customer ?? throw new MaxioApiException(502, "read the created Maxio customer");
    }

    public async Task<MaxioSubscription?> FindSubscriptionByReferenceAsync(string reference, CancellationToken cancellationToken)
    {
        var path = $"subscriptions/lookup.json?reference={Uri.EscapeDataString(reference)}";
        return await SendNullableAsync<MaxioSubscriptionResponse>(HttpMethod.Get, path, "find the Maxio subscription", cancellationToken)
            is { Subscription: not null } result ? result.Subscription : null;
    }

    public async Task<MaxioSubscription> CreateSubscriptionAsync(string productHandle, string customerReference, string reference, CancellationToken cancellationToken)
    {
        var siteResponse = await SendAsync<MaxioSiteResponse>(HttpMethod.Get, "site.json", null, "read the Maxio site settings", cancellationToken);
        var paymentCollectionMethod = siteResponse.Site?.RelationshipInvoicingEnabled == true ? "remittance" : "invoice";
        var payload = new
        {
            uniqueness_token = UniquenessToken(reference),
            subscription = new
            {
                product_handle = productHandle,
                customer_reference = customerReference,
                reference,
                payment_collection_method = paymentCollectionMethod
            }
        };

        var response = await SendAsync<MaxioSubscriptionResponse>(HttpMethod.Post, "subscriptions.json", payload, "create the Maxio subscription", cancellationToken);
        return response.Subscription ?? throw new MaxioApiException(502, "read the created Maxio subscription");
    }

    public async Task<IReadOnlyList<MaxioSubscription>> ListCustomerSubscriptionsAsync(int customerId, CancellationToken cancellationToken)
    {
        var response = await SendListAsync<MaxioSubscription>(
            HttpMethod.Get,
            $"customers/{customerId}/subscriptions.json?per_page=200",
            null,
            "list Maxio customer subscriptions",
            cancellationToken);

        return response.Items
            .Where(item => item.Subscription is not null)
            .Select(item => item.Subscription!)
            .ToArray();
    }

    private async Task<MaxioItemsResponse<T>> SendListAsync<T>(HttpMethod method, string path, object? payload, string operation, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, path);
        if (payload is not null)
        {
            request.Content = JsonContent.Create(payload, options: JsonOptions);
        }

        using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new MaxioApiException((int)response.StatusCode, operation);
        }

        using var document = JsonDocument.Parse(await response.Content.ReadAsStreamAsync(cancellationToken));
        if (document.RootElement.ValueKind == JsonValueKind.Array)
        {
            return new MaxioItemsResponse<T>
            {
                Items = JsonSerializer.Deserialize<List<MaxioItem<T>>>(document.RootElement.GetRawText(), JsonOptions) ?? new()
            };
        }

        return JsonSerializer.Deserialize<MaxioItemsResponse<T>>(document.RootElement.GetRawText(), JsonOptions)
            ?? throw new MaxioApiException((int)HttpStatusCode.BadGateway, operation);
    }

    private async Task<T> SendAsync<T>(HttpMethod method, string path, object? payload, string operation, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, path);
        if (payload is not null)
        {
            request.Content = JsonContent.Create(payload, options: JsonOptions);
        }

        using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new MaxioApiException((int)response.StatusCode, operation);
        }

        var result = await response.Content.ReadFromJsonAsync<T>(JsonOptions, cancellationToken);
        return result ?? throw new MaxioApiException((int)HttpStatusCode.BadGateway, operation);
    }

    private async Task<T?> SendNullableAsync<T>(HttpMethod method, string path, string operation, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, path);
        using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return default;
        }

        if (!response.IsSuccessStatusCode)
        {
            throw new MaxioApiException((int)response.StatusCode, operation);
        }

        return await response.Content.ReadFromJsonAsync<T>(JsonOptions, cancellationToken);
    }

    public static void Configure(HttpClient client, MaxioOptions options)
    {
        options.Validate();
        client.BaseAddress = options.GetBaseAddress();
        client.Timeout = TimeSpan.FromSeconds(30);
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        var credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{options.ApiKey}:X"));
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", credentials);
    }

    private static string UniquenessToken(string reference) =>
        Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(reference)));
}
