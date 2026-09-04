using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

public sealed class MaxioBillingClient : IMaxioBillingClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly HttpClient _httpClient;
    private readonly MaxioOptions _options;
    private readonly ILogger<MaxioBillingClient> _logger;

    public MaxioBillingClient(HttpClient httpClient, IOptions<MaxioOptions> options, ILogger<MaxioBillingClient> logger)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _logger = logger;
        _options.Validate();

        var credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{_options.ApiKey}:X"));
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", credentials);
        _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    }

    public async Task<MaxioCustomer?> FindCustomerByReferenceAsync(string reference, CancellationToken cancellationToken)
    {
        using var response = await _httpClient.GetAsync($"customers/lookup.json?reference={Uri.EscapeDataString(reference)}", cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        var payload = await ReadAsync<MaxioCustomerResponse>(response, cancellationToken);
        return payload.Customer;
    }

    public async Task<MaxioCustomer> CreateCustomerAsync(string reference, string firstName, string lastName, string email, CancellationToken cancellationToken)
    {
        using var response = await _httpClient.PostAsJsonAsync(
            "customers.json",
            new CreateMaxioCustomerRequest
            {
                UniquenessToken = CreateUniquenessToken($"customer:{reference}"),
                Customer = new CreateMaxioCustomer
                {
                    Reference = reference,
                    FirstName = firstName,
                    LastName = lastName,
                    Email = email
                }
            },
            JsonOptions,
            cancellationToken);

        if (response.IsSuccessStatusCode)
        {
            return (await ReadAsync<MaxioCustomerResponse>(response, cancellationToken)).Customer;
        }

        // Customer references are unique in Billing API. A concurrent creator can
        // therefore safely be resolved by looking up the reference again.
        if (response.StatusCode is HttpStatusCode.UnprocessableEntity or HttpStatusCode.Conflict)
        {
            var existing = await FindCustomerByReferenceAsync(reference, cancellationToken);
            if (existing is not null)
            {
                return existing;
            }
        }

        throw await CreateExceptionAsync(response, cancellationToken);
    }

    public async Task<IReadOnlyList<MaxioProduct>> ListProductsAsync(string productFamilyHandle, CancellationToken cancellationToken)
    {
        using var response = await _httpClient.GetAsync(
            $"product_families/handle:{Uri.EscapeDataString(productFamilyHandle)}/products.json?per_page=200",
            cancellationToken);
        var items = await ReadAsync<List<MaxioProductResponse>>(response, cancellationToken);
        return items.Select(item => item.Product).Where(product => product.ArchivedAt is null).ToArray();
    }

    public async Task<MaxioSubscription> CreateSubscriptionAsync(int customerId, string productHandle, string reference, CancellationToken cancellationToken)
    {
        using var response = await _httpClient.PostAsJsonAsync(
            "subscriptions.json",
            new CreateMaxioSubscriptionRequest
            {
                UniquenessToken = CreateUniquenessToken($"subscription:{reference}"),
                Subscription = new CreateMaxioSubscription
                {
                    CustomerId = customerId,
                    ProductHandle = productHandle,
                    Reference = reference
                }
            },
            JsonOptions,
            cancellationToken);

        return (await ReadAsync<MaxioSubscriptionResponse>(response, cancellationToken)).Subscription;
    }

    public async Task<IReadOnlyList<MaxioSubscription>> ListCustomerSubscriptionsAsync(int customerId, CancellationToken cancellationToken)
    {
        using var response = await _httpClient.GetAsync($"customers/{customerId}/subscriptions.json", cancellationToken);
        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw CreateException(response.StatusCode, json);
        }

        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        if (root.ValueKind == JsonValueKind.Array)
        {
            return root.EnumerateArray().Select(ParseSubscription).ToArray();
        }

        if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("items", out var items))
        {
            return items.EnumerateArray().Select(ParseSubscription).ToArray();
        }

        return Array.Empty<MaxioSubscription>();
    }

    private static MaxioSubscription ParseSubscription(JsonElement value)
    {
        var subscription = value.TryGetProperty("subscription", out var wrapped) ? wrapped : value;
        return subscription.Deserialize<MaxioSubscription>(JsonOptions) ?? new MaxioSubscription();
    }

    private async Task<T> ReadAsync<T>(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw CreateException(response.StatusCode, json);
        }

        try
        {
            return JsonSerializer.Deserialize<T>(json, JsonOptions)
                ?? throw new MaxioApiException(response.StatusCode, "Maxio returned an empty response.");
        }
        catch (JsonException exception)
        {
            _logger.LogError(exception, "Maxio returned an invalid response for {StatusCode}.", response.StatusCode);
            throw new MaxioApiException(response.StatusCode, "Maxio returned an invalid response.");
        }
    }

    private static async Task<MaxioApiException> CreateExceptionAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        return CreateException(response.StatusCode, await response.Content.ReadAsStringAsync(cancellationToken));
    }

    private static MaxioApiException CreateException(HttpStatusCode statusCode, string body)
    {
        var detail = string.IsNullOrWhiteSpace(body) ? statusCode.ToString() : body.Trim();
        return new MaxioApiException(statusCode, $"Maxio request failed ({(int)statusCode}): {detail}");
    }

    private static string CreateUniquenessToken(string value)
    {
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    }
}
