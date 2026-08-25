using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

/// <summary>
/// Plain-HTTP client for the Maxio Advanced Billing API.
/// Base address and Basic-auth credentials are configured on the typed HttpClient
/// in Program.cs from <see cref="MaxioSettings"/>.
/// </summary>
public class MaxioClient : IMaxioClient
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _httpClient;

    public MaxioClient(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<MaxioProductFamily?> GetProductFamilyByHandleAsync(string handle, CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient.GetAsync(
            $"product_families/lookup.json?handle={Uri.EscapeDataString(handle)}", cancellationToken);

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        var wrapper = await ReadAsync<MaxioProductFamilyWrapper>(response, cancellationToken);
        return wrapper?.ProductFamily;
    }

    public async Task<IReadOnlyList<MaxioProduct>> GetProductsByFamilyAsync(long productFamilyId, CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient.GetAsync($"product_families/{productFamilyId}/products.json", cancellationToken);
        var wrappers = await ReadAsync<List<MaxioProductWrapper>>(response, cancellationToken);
        return (wrappers ?? new List<MaxioProductWrapper>())
            .Select(w => w.Product)
            .Where(p => p != null)
            .Cast<MaxioProduct>()
            .ToList();
    }

    public async Task<MaxioCustomer?> FindCustomerByReferenceAsync(string reference, CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient.GetAsync(
            $"customers/lookup.json?reference={Uri.EscapeDataString(reference)}", cancellationToken);

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        var wrapper = await ReadAsync<MaxioCustomerWrapper>(response, cancellationToken);
        return wrapper?.Customer;
    }

    public async Task<MaxioCustomer> GetOrCreateCustomerAsync(string reference, string email, string firstName, string lastName, CancellationToken cancellationToken = default)
    {
        var existing = await FindCustomerByReferenceAsync(reference, cancellationToken);
        if (existing != null)
        {
            return existing;
        }

        var request = new MaxioCreateCustomerRequest
        {
            Customer = new MaxioCustomerAttributes
            {
                FirstName = firstName,
                LastName = lastName,
                Email = email,
                Reference = reference
            }
        };

        using var response = await _httpClient.PostAsJsonAsync("customers.json", request, SerializerOptions, cancellationToken);

        if (response.StatusCode == HttpStatusCode.UnprocessableEntity)
        {
            // The reference is unique per site: a 422 here means another request created
            // the customer first. Re-read and return that customer instead of failing.
            var winner = await FindCustomerByReferenceAsync(reference, cancellationToken);
            if (winner != null)
            {
                return winner;
            }
        }

        var wrapper = await ReadAsync<MaxioCustomerWrapper>(response, cancellationToken);
        return wrapper?.Customer
            ?? throw new MaxioApiException(response.StatusCode, new[] { "Maxio returned an empty customer payload." }, string.Empty);
    }

    public async Task<IReadOnlyList<MaxioSubscription>> GetCustomerSubscriptionsAsync(long customerId, CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient.GetAsync($"customers/{customerId}/subscriptions.json", cancellationToken);
        var wrappers = await ReadAsync<List<MaxioSubscriptionWrapper>>(response, cancellationToken);
        return (wrappers ?? new List<MaxioSubscriptionWrapper>())
            .Select(w => w.Subscription)
            .Where(s => s != null)
            .Cast<MaxioSubscription>()
            .ToList();
    }

    public async Task<MaxioSubscription> CreateSubscriptionAsync(long customerId, string productHandle, string? reference, CancellationToken cancellationToken = default)
    {
        var request = new MaxioCreateSubscriptionRequest
        {
            Subscription = new MaxioSubscriptionAttributes
            {
                ProductHandle = productHandle,
                CustomerId = customerId,
                PaymentCollectionMethod = "remittance",
                Reference = reference
            }
        };

        using var response = await _httpClient.PostAsJsonAsync("subscriptions.json", request, SerializerOptions, cancellationToken);
        var wrapper = await ReadAsync<MaxioSubscriptionWrapper>(response, cancellationToken);
        return wrapper?.Subscription
            ?? throw new MaxioApiException(response.StatusCode, new[] { "Maxio returned an empty subscription payload." }, string.Empty);
    }

    private static async Task<T?> ReadAsync<T>(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return await response.Content.ReadFromJsonAsync<T>(SerializerOptions, cancellationToken);
        }

        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        IReadOnlyList<string> errors;
        try
        {
            var parsed = JsonSerializer.Deserialize<MaxioErrorResponse>(body, SerializerOptions);
            errors = parsed?.Errors?.Count > 0
                ? parsed.Errors
                : new List<string> { body };
        }
        catch (JsonException)
        {
            errors = new List<string> { body };
        }

        throw new MaxioApiException(response.StatusCode, errors, body);
    }
}
