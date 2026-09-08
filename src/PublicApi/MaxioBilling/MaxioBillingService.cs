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
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.MaxioBilling;

public class MaxioBillingService : IMaxioBillingService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly HttpClient _httpClient;
    private readonly MaxioBillingOptions _options;
    private readonly ILogger<MaxioBillingService> _logger;

    public MaxioBillingService(HttpClient httpClient, IOptions<MaxioBillingOptions> options, ILogger<MaxioBillingService> logger)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<IReadOnlyList<MaxioProduct>> ListProductsInFamilyAsync(string productFamilyHandle, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(productFamilyHandle))
        {
            throw new ArgumentException("A product family handle is required.", nameof(productFamilyHandle));
        }

        const int perPage = 200;
        var products = new List<MaxioProduct>();

        for (int page = 1; ; page++)
        {
            string familySegment = Uri.EscapeDataString($"handle:{productFamilyHandle}");
            string url = $"/product_families/{familySegment}/products.json?page={page}&per_page={perPage}";
            string body = await SendAsync(HttpMethod.Get, url, requestBody: null, cancellationToken).ConfigureAwait(false);

            var pageItems = JsonSerializer.Deserialize<List<MaxioProductEnvelope>>(body, JsonOptions) ?? new List<MaxioProductEnvelope>();
            var productsOnPage = pageItems
                .Where(envelope => envelope.Product is not null)
                .Select(envelope => envelope.Product!)
                .ToList();

            products.AddRange(productsOnPage);

            if (productsOnPage.Count < perPage)
            {
                break;
            }
        }

        return products;
    }

    public async Task<MaxioProduct?> FindProductByHandleAsync(string handle, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(handle))
        {
            throw new ArgumentException("A product handle is required.", nameof(handle));
        }

        string url = $"/products/handle/{Uri.EscapeDataString(handle)}.json";

        try
        {
            string body = await SendAsync(HttpMethod.Get, url, requestBody: null, cancellationToken).ConfigureAwait(false);
            var envelope = JsonSerializer.Deserialize<MaxioProductEnvelope>(body, JsonOptions);
            return envelope?.Product;
        }
        catch (MaxioApiException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            _logger.LogInformation("Maxio product with handle '{Handle}' was not found.", handle);
            return null;
        }
    }

    public async Task<MaxioCustomer?> FindCustomerByReferenceAsync(string reference, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(reference))
        {
            throw new ArgumentException("A customer reference is required.", nameof(reference));
        }

        string url = $"/customers/lookup.json?reference={Uri.EscapeDataString(reference)}";

        try
        {
            string body = await SendAsync(HttpMethod.Get, url, requestBody: null, cancellationToken).ConfigureAwait(false);
            var envelope = JsonSerializer.Deserialize<MaxioCustomerEnvelope>(body, JsonOptions);
            return envelope?.Customer;
        }
        catch (MaxioApiException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    public async Task<MaxioCustomer> EnsureCustomerAsync(string reference, string email, string firstName, string lastName, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(reference))
        {
            throw new ArgumentException("A customer reference is required.", nameof(reference));
        }

        var existing = await FindCustomerByReferenceAsync(reference, cancellationToken).ConfigureAwait(false);
        if (existing is not null)
        {
            return existing;
        }

        var payload = new
        {
            customer = new
            {
                first_name = firstName ?? string.Empty,
                last_name = lastName ?? string.Empty,
                email = email ?? string.Empty,
                reference
            }
        };

        try
        {
            string body = await SendAsync(HttpMethod.Post, "/customers.json", payload, cancellationToken).ConfigureAwait(false);
            var envelope = JsonSerializer.Deserialize<MaxioCustomerEnvelope>(body, JsonOptions);
            return envelope?.Customer ?? throw new MaxioApiException(HttpStatusCode.InternalServerError, body, "/customers.json");
        }
        catch (MaxioApiException ex) when (ex.StatusCode == HttpStatusCode.UnprocessableEntity)
        {
            var createdByAnotherRequest = await FindCustomerByReferenceAsync(reference, cancellationToken).ConfigureAwait(false);
            if (createdByAnotherRequest is not null)
            {
                _logger.LogWarning(
                    "Maxio customer lookup after a conflicting create returned an existing customer for reference '{Reference}'. Treating as an idempotent success.",
                    reference);
                return createdByAnotherRequest;
            }

            throw;
        }
    }

    public async Task<IReadOnlyList<MaxioSubscription>> ListCustomerSubscriptionsAsync(long customerId, CancellationToken cancellationToken)
    {
        string url = $"/customers/{customerId}/subscriptions.json";
        string body = await SendAsync(HttpMethod.Get, url, requestBody: null, cancellationToken).ConfigureAwait(false);

        var envelopes = JsonSerializer.Deserialize<List<MaxioSubscriptionEnvelope>>(body, JsonOptions) ?? new List<MaxioSubscriptionEnvelope>();
        return envelopes
            .Where(envelope => envelope.Subscription is not null)
            .Select(envelope => envelope.Subscription!)
            .ToList();
    }

    public async Task<MaxioSubscription> CreateSubscriptionAsync(long customerId, string productHandle, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(productHandle))
        {
            throw new ArgumentException("A product handle is required.", nameof(productHandle));
        }

        // The catalog is seeded with products that do not require a payment method
        // (require_credit_card = false). Billing the subscription via remittance
        // (invoice-based collection) is what allows signup to succeed without
        // capturing a card or running 3-D Secure.
        var payload = new
        {
            subscription = new
            {
                product_handle = productHandle,
                customer_id = customerId,
                payment_collection_method = "remittance"
            }
        };

        string body = await SendAsync(HttpMethod.Post, "/subscriptions.json", payload, cancellationToken).ConfigureAwait(false);
        var envelope = JsonSerializer.Deserialize<MaxioSubscriptionEnvelope>(body, JsonOptions);
        return envelope?.Subscription ?? throw new MaxioApiException(HttpStatusCode.InternalServerError, body, "/subscriptions.json");
    }

    private async Task<string> SendAsync(HttpMethod method, string relativeUrl, object? requestBody, CancellationToken cancellationToken)
    {
        if (!_options.IsConfigured)
        {
            throw new InvalidOperationException(
                "Maxio billing is not configured. Set the Maxio:ApiKey and Maxio:Subdomain (or Maxio:BaseUrl) settings before calling the billing API.");
        }

        using var request = new HttpRequestMessage(method, _options.ResolveBaseUrl() + relativeUrl);

        var credentials = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{_options.ApiKey}:x"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", credentials);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        if (requestBody is not null)
        {
            request.Content = JsonContent.Create(requestBody, options: JsonOptions);
        }

        _logger.LogInformation("Sending {Method} {Url} to Maxio.", method, request.RequestUri);

        using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        string responseBody = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            throw new MaxioApiException(response.StatusCode, responseBody, relativeUrl);
        }

        return responseBody;
    }
}
