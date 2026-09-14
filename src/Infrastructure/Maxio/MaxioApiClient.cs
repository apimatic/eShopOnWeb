using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using Microsoft.eShopWeb.Infrastructure.Maxio.Wire;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

internal class MaxioApiClient : IMaxioApiClient
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _httpClient;
    private readonly MaxioOptions _options;

    public MaxioApiClient(HttpClient httpClient, IOptions<MaxioOptions> options)
    {
        _httpClient = httpClient;
        _options = options.Value;
    }

    public async Task<MaxioCustomerWire?> FindCustomerByReferenceAsync(string reference, CancellationToken cancellationToken)
    {
        using var response = await _httpClient.GetAsync(
            $"/customers/lookup.json?reference={System.Uri.EscapeDataString(reference)}", cancellationToken);

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        await EnsureSuccessAsync(response, cancellationToken);
        var envelope = await response.Content.ReadFromJsonAsync<MaxioCustomerEnvelope>(SerializerOptions, cancellationToken);
        return envelope?.Customer;
    }

    public async Task<MaxioCustomerWire> CreateCustomerAsync(string reference, string email, CancellationToken cancellationToken)
    {
        var (firstName, lastName) = DeriveNameFromEmail(email);
        var body = new CreateMaxioCustomerEnvelope
        {
            Customer = new CreateMaxioCustomerWire
            {
                Reference = reference,
                Email = email,
                FirstName = firstName,
                LastName = lastName
            }
        };

        using var response = await _httpClient.PostAsJsonAsync("/customers.json", body, SerializerOptions, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        var envelope = await response.Content.ReadFromJsonAsync<MaxioCustomerEnvelope>(SerializerOptions, cancellationToken);
        return envelope!.Customer!;
    }

    public async Task<MaxioProductWire?> GetProductByHandleAsync(string productHandle, CancellationToken cancellationToken)
    {
        using var response = await _httpClient.GetAsync(
            $"/products/handle/{System.Uri.EscapeDataString(productHandle)}.json", cancellationToken);

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        await EnsureSuccessAsync(response, cancellationToken);
        var envelope = await response.Content.ReadFromJsonAsync<MaxioProductEnvelope>(SerializerOptions, cancellationToken);
        return envelope?.Product;
    }

    public async Task<IReadOnlyList<MaxioProductWire>> ListProductsForFamilyAsync(CancellationToken cancellationToken)
    {
        using var response = await _httpClient.GetAsync(
            $"/product_families/handle:{System.Uri.EscapeDataString(_options.ProductFamilyHandle)}/products.json", cancellationToken);

        await EnsureSuccessAsync(response, cancellationToken);
        var envelopes = await response.Content.ReadFromJsonAsync<List<MaxioProductEnvelope>>(SerializerOptions, cancellationToken);
        var products = new List<MaxioProductWire>();
        foreach (var envelope in envelopes ?? new List<MaxioProductEnvelope>())
        {
            if (envelope.Product is not null)
            {
                products.Add(envelope.Product);
            }
        }
        return products;
    }

    public async Task<MaxioSubscriptionWire> CreateSubscriptionAsync(string customerReference, string productHandle, CancellationToken cancellationToken)
    {
        var body = new CreateMaxioSubscriptionEnvelope
        {
            Subscription = new CreateMaxioSubscriptionWire
            {
                CustomerReference = customerReference,
                ProductHandle = productHandle
            }
        };

        using var response = await _httpClient.PostAsJsonAsync("/subscriptions.json", body, SerializerOptions, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        var envelope = await response.Content.ReadFromJsonAsync<MaxioSubscriptionEnvelope>(SerializerOptions, cancellationToken);
        return envelope!.Subscription!;
    }

    public async Task<IReadOnlyList<MaxioSubscriptionWire>> ListSubscriptionsForCustomerAsync(int maxioCustomerId, CancellationToken cancellationToken)
    {
        using var response = await _httpClient.GetAsync($"/subscriptions.json?customer_id={maxioCustomerId}", cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        var envelopes = await response.Content.ReadFromJsonAsync<List<MaxioSubscriptionEnvelope>>(SerializerOptions, cancellationToken);
        var subscriptions = new List<MaxioSubscriptionWire>();
        foreach (var envelope in envelopes ?? new List<MaxioSubscriptionEnvelope>())
        {
            if (envelope.Subscription is not null)
            {
                subscriptions.Add(envelope.Subscription);
            }
        }
        return subscriptions;
    }

    /// <summary>
    /// eShopOnWeb's ApplicationUser carries no first/last name, but Maxio requires both to
    /// create a customer. Derive a reasonable placeholder from the email address.
    /// </summary>
    private static (string FirstName, string LastName) DeriveNameFromEmail(string email)
    {
        var localPart = email.Split('@')[0];
        var segments = localPart.Split(new[] { '.', '_', '+', '-' }, System.StringSplitOptions.RemoveEmptyEntries);

        var firstName = segments.Length > 0 ? segments[0] : localPart;
        var lastName = segments.Length > 1 ? string.Join(" ", segments[1..]) : "eShopOnWeb Customer";

        return (firstName, lastName);
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        throw new MaxioApiException(response.StatusCode, body);
    }
}
