using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.Infrastructure.Billing;

/// <summary>
/// Thin HTTP client over the subset of the Maxio Advanced Billing REST API this integration needs.
/// Confirmed against the current Maxio Advanced Billing API contract and the sandbox site:
/// Basic auth (api key as username, literal "x" as password), JSON envelopes, .json route suffix.
/// </summary>
internal sealed class MaxioApiClient
{
    private const int PageSize = 200;
    private const string SubscriptionCollectionMethodRemittance = "remittance";

    private readonly HttpClient _httpClient;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public MaxioApiClient(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<SiteDto> GetSiteAsync(CancellationToken cancellationToken)
    {
        using var response = await SendAsync(HttpMethod.Get, "site.json", null, cancellationToken);
        return (await ReadEnvelopeAsync<SiteEnvelope>(response, cancellationToken)).Site;
    }

    public async Task<ProductFamilyDto?> FindProductFamilyByHandleAsync(string handle, CancellationToken cancellationToken)
    {
        var families = await GetListAsync<ProductFamilyEnvelope>(HttpMethod.Get, "product_families.json", null, cancellationToken);
        return families
            .Select(e => e.ProductFamily)
            .FirstOrDefault(f => f.Handle.Equals(handle, StringComparison.OrdinalIgnoreCase));
    }

    public async Task<IReadOnlyList<ProductDto>> ListProductsForFamilyAsync(int productFamilyId, CancellationToken cancellationToken)
    {
        var products = new List<ProductDto>();
        int page = 1;
        while (true)
        {
            var query = $"product_families/{productFamilyId}/products.json?page={page}&per_page={PageSize}&include_archived=false";
            using var response = await SendAsync(HttpMethod.Get, query, null, cancellationToken);
            var batch = await ReadListAsync<ProductEnvelope>(response, cancellationToken);
            products.AddRange(batch.Select(e => e.Product));
            if (batch.Count < PageSize)
            {
                break;
            }

            page++;
        }

        return products;
    }

    public async Task<CustomerDto?> FindCustomerByReferenceAsync(string reference, CancellationToken cancellationToken)
    {
        var query = $"customers/lookup.json?reference={Uri.EscapeDataString(reference)}";
        using var response = await SendAsync(HttpMethod.Get, query, null, cancellationToken);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }

        return (await ReadEnvelopeAsync<CustomerEnvelope>(response, cancellationToken)).Customer;
    }

    public async Task<CustomerDto> CreateCustomerAsync(string reference, string email, string firstName, string lastName, CancellationToken cancellationToken)
    {
        var payload = new CreateCustomerRequestDto
        {
            Customer = new CreateCustomerPayloadDto
            {
                Reference = reference,
                Email = email,
                FirstName = firstName,
                LastName = lastName
            }
        };

        using var response = await SendAsync(HttpMethod.Post, "customers.json", payload, cancellationToken);
        return (await ReadEnvelopeAsync<CustomerEnvelope>(response, cancellationToken)).Customer;
    }

    public async Task<SubscriptionDto> CreateSubscriptionAsync(int customerId, string productHandle, string reference, CancellationToken cancellationToken)
    {
        var payload = new CreateSubscriptionRequestDto
        {
            Subscription = new CreateSubscriptionPayloadDto
            {
                ProductHandle = productHandle,
                CustomerId = customerId,
                Reference = reference,
                // Collect through invoice ("remittance") so subscribing requires no card capture / 3-DS.
                // This matches the demo catalog ("payment method not required") and keeps the same build
                // working against any card-less catalog.
                PaymentCollectionMethod = SubscriptionCollectionMethodRemittance
            }
        };

        using var response = await SendAsync(HttpMethod.Post, "subscriptions.json", payload, cancellationToken);
        return (await ReadEnvelopeAsync<SubscriptionEnvelope>(response, cancellationToken)).Subscription;
    }

    public async Task<SubscriptionDto?> FindSubscriptionByReferenceAsync(string reference, CancellationToken cancellationToken)
    {
        var query = $"subscriptions/lookup.json?reference={Uri.EscapeDataString(reference)}";
        using var response = await SendAsync(HttpMethod.Get, query, null, cancellationToken);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }

        return (await ReadEnvelopeAsync<SubscriptionEnvelope>(response, cancellationToken)).Subscription;
    }

    public async Task<IReadOnlyList<SubscriptionDto>> ListSubscriptionsForCustomerAsync(int customerId, CancellationToken cancellationToken)
    {
        var query = $"customers/{customerId}/subscriptions.json";
        var subscriptions = await GetListAsync<SubscriptionEnvelope>(HttpMethod.Get, query, null, cancellationToken);
        return subscriptions.Select(e => e.Subscription).ToList();
    }

    private async Task<List<TEnvelope>> GetListAsync<TEnvelope>(HttpMethod method, string relativeUrl, object? payload, CancellationToken cancellationToken)
        where TEnvelope : class, new()
    {
        using var response = await SendAsync(method, relativeUrl, payload, cancellationToken);
        return await ReadListAsync<TEnvelope>(response, cancellationToken);
    }

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string relativeUrl, object? payload, CancellationToken cancellationToken)
    {
        var request = new HttpRequestMessage(method, relativeUrl);
        if (payload != null)
        {
            request.Content = JsonContent.Create(payload, options: JsonOptions);
        }

        try
        {
            return await _httpClient.SendAsync(request, cancellationToken);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new MaxioApiException(null, new[] { "The billing provider request timed out." });
        }
        catch (HttpRequestException ex)
        {
            throw new MaxioApiException(null, new[] { "Unable to reach the billing provider." }, ex.Message);
        }
    }

    private static async Task<TEnvelope> ReadEnvelopeAsync<TEnvelope>(HttpResponseMessage response, CancellationToken cancellationToken)
        where TEnvelope : new()
    {
        await EnsureSuccessAsync(response, cancellationToken);
        return await response.Content.ReadFromJsonAsync<TEnvelope>(JsonOptions, cancellationToken)
               ?? new TEnvelope();
    }

    private static async Task<List<TEnvelope>> ReadListAsync<TEnvelope>(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        await EnsureSuccessAsync(response, cancellationToken);
        return await response.Content.ReadFromJsonAsync<List<TEnvelope>>(JsonOptions, cancellationToken)
               ?? new List<TEnvelope>();
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        throw new MaxioApiException((int)response.StatusCode, ParseErrors(body), body);
    }

    internal static IReadOnlyList<string> ParseErrors(string? body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return Array.Empty<string>();
        }

        try
        {
            using var document = JsonDocument.Parse(body);
            if (document.RootElement.TryGetProperty("errors", out var errors) && errors.ValueKind == JsonValueKind.Array)
            {
                var messages = new List<string>();
                foreach (var item in errors.EnumerateArray())
                {
                    messages.Add(item.ValueKind == JsonValueKind.String
                        ? item.GetString()!
                        : item.GetRawText());
                }

                return messages;
            }
        }
        catch (JsonException)
        {
            // fall through to raw body
        }

        return new[] { body };
    }
}
