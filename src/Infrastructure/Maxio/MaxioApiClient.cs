using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Thin, transport-level client for the Maxio Advanced Billing REST API. It knows about URLs,
/// payload shapes and provider error semantics; it holds no workflow logic.
/// </summary>
internal class MaxioApiClient
{
    /// <summary>Maxio clamps any larger value to 200.</summary>
    private const int PageSize = 200;

    private readonly HttpClient _httpClient;
    private readonly ILogger<MaxioApiClient> _logger;

    public MaxioApiClient(HttpClient httpClient, ILogger<MaxioApiClient> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    /// <summary>
    /// GET /product_families/{handle}/products.json - the products of one family, which this
    /// application publishes as its subscription plans.
    /// </summary>
    public async Task<IReadOnlyList<MaxioProduct>> ListProductsForFamilyAsync(string productFamilyHandle,
        CancellationToken cancellationToken)
    {
        var products = new List<MaxioProduct>();

        // Maxio caps a page at 200 records, so keep asking until a short page comes back.
        for (var page = 1; ; page++)
        {
            // The path segment accepts either a numeric id or a handle prefixed with "handle:".
            var path = $"product_families/handle:{Uri.EscapeDataString(productFamilyHandle)}" +
                       $"/products.json?per_page={PageSize}&page={page}";

            using var request = new HttpRequestMessage(HttpMethod.Get, path);
            using var response = await SendAsync(request, cancellationToken);

            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                throw new BillingConfigurationException(
                    $"Maxio product family '{productFamilyHandle}' does not exist on this site. Check Maxio:ProductFamilyHandle.");
            }

            await EnsureSuccessAsync(response, "list products for product family", cancellationToken);

            var envelopes = await ReadAsync<List<MaxioProductEnvelope>>(response, cancellationToken)
                            ?? new List<MaxioProductEnvelope>();

            foreach (var envelope in envelopes)
            {
                if (envelope.Product is not null)
                {
                    products.Add(envelope.Product);
                }
            }

            if (envelopes.Count < PageSize)
            {
                return products;
            }
        }
    }

    /// <summary>
    /// GET /customers/lookup.json?reference=... - returns null when no customer carries the reference.
    /// </summary>
    public async Task<MaxioCustomer?> FindCustomerByReferenceAsync(string reference, CancellationToken cancellationToken)
    {
        var path = $"customers/lookup.json?reference={Uri.EscapeDataString(reference)}";

        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        using var response = await SendAsync(request, cancellationToken);

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        await EnsureSuccessAsync(response, "look up customer by reference", cancellationToken);

        var envelope = await ReadAsync<MaxioCustomerEnvelope>(response, cancellationToken);
        return envelope?.Customer;
    }

    /// <summary>
    /// POST /customers.json. The provider enforces uniqueness of the customer reference, so a lost
    /// race surfaces as a 422 rather than a duplicate customer; callers re-read to resolve it.
    /// </summary>
    public async Task<MaxioCustomer> CreateCustomerAsync(MaxioCustomerAttributes attributes,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "customers.json")
        {
            Content = JsonContent.Create(new MaxioCreateCustomerRequest { Customer = attributes },
                options: MaxioJson.Options)
        };

        using var response = await SendAsync(request, cancellationToken);

        if (response.StatusCode == HttpStatusCode.UnprocessableEntity)
        {
            var errors = await ReadErrorsAsync(response, cancellationToken);
            throw new MaxioUnprocessableEntityException("Maxio rejected the customer details.", errors);
        }

        await EnsureSuccessAsync(response, "create customer", cancellationToken);

        var envelope = await ReadAsync<MaxioCustomerEnvelope>(response, cancellationToken);
        return envelope?.Customer
               ?? throw new BillingProviderException("Maxio returned an empty customer payload for a successful create.");
    }

    /// <summary>GET /customers/{id}/subscriptions.json.</summary>
    public async Task<IReadOnlyList<MaxioSubscription>> ListCustomerSubscriptionsAsync(long customerId,
        CancellationToken cancellationToken)
    {
        var path = $"customers/{customerId.ToString(CultureInfo.InvariantCulture)}/subscriptions.json";

        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        using var response = await SendAsync(request, cancellationToken);

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return Array.Empty<MaxioSubscription>();
        }

        await EnsureSuccessAsync(response, "list customer subscriptions", cancellationToken);

        var envelopes = await ReadAsync<List<MaxioSubscriptionEnvelope>>(response, cancellationToken);
        var subscriptions = new List<MaxioSubscription>();
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
    /// POST /subscriptions.json. The request carries a uniqueness token, so a replay within the
    /// provider's de-duplication window answers 409 instead of signing the customer up twice.
    /// </summary>
    public async Task<MaxioSubscription> CreateSubscriptionAsync(MaxioCreateSubscriptionRequest body,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "subscriptions.json")
        {
            Content = JsonContent.Create(body, options: MaxioJson.Options)
        };

        // Safe to replay only because the uniqueness token makes a duplicate submission detectable.
        request.Options.Set(MaxioTransientFaultHandler.RetrySafeOption, body.UniquenessToken is not null);

        using var response = await SendAsync(request, cancellationToken);

        if (response.StatusCode == HttpStatusCode.Conflict)
        {
            var errors = await ReadErrorsAsync(response, cancellationToken);
            throw new MaxioDuplicateSubmissionException(
                "Maxio rejected the signup as a duplicate submission.", errors);
        }

        if (response.StatusCode == HttpStatusCode.UnprocessableEntity)
        {
            var errors = await ReadErrorsAsync(response, cancellationToken);
            throw new MaxioUnprocessableEntityException("Maxio rejected the subscription request.", errors);
        }

        await EnsureSuccessAsync(response, "create subscription", cancellationToken);

        var envelope = await ReadAsync<MaxioSubscriptionEnvelope>(response, cancellationToken);
        return envelope?.Subscription
               ?? throw new BillingProviderException("Maxio returned an empty subscription payload for a successful create.");
    }

    private async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        try
        {
            return await _httpClient.SendAsync(request, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException)
        {
            throw new BillingProviderException(
                $"Could not reach Maxio at {_httpClient.BaseAddress}. {ex.Message}", innerException: ex);
        }
    }

    private async Task EnsureSuccessAsync(HttpResponseMessage response, string operation,
        CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var status = (int)response.StatusCode;
        var errors = await ReadErrorsAsync(response, cancellationToken);

        _logger.LogError("Maxio call to {Operation} failed with {StatusCode}. {Errors}",
            operation, status, string.Join("; ", errors));

        if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
        {
            throw new BillingConfigurationException(
                $"Maxio rejected the API credentials ({status}). Check Maxio:ApiKey and Maxio:Subdomain.");
        }

        throw new BillingProviderException($"Maxio failed to {operation} ({status}).", status, errors);
    }

    private static async Task<T?> ReadAsync<T>(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        try
        {
            return await response.Content.ReadFromJsonAsync<T>(MaxioJson.Options, cancellationToken);
        }
        catch (JsonException ex)
        {
            throw new BillingProviderException("Maxio returned a payload that could not be parsed.",
                (int)response.StatusCode, innerException: ex);
        }
    }

    /// <summary>
    /// Maxio reports failures either as an array of messages or as an object keyed by field; both
    /// shapes are flattened to a list of human-readable messages.
    /// </summary>
    private static async Task<IReadOnlyList<string>> ReadErrorsAsync(HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        var messages = new List<string>();

        string body;
        try
        {
            body = await response.Content.ReadAsStringAsync(cancellationToken);
        }
        catch (Exception)
        {
            return messages;
        }

        if (string.IsNullOrWhiteSpace(body))
        {
            return messages;
        }

        try
        {
            using var document = JsonDocument.Parse(body);
            if (document.RootElement.ValueKind == JsonValueKind.Object &&
                document.RootElement.TryGetProperty("errors", out var errors))
            {
                CollectMessages(errors, messages);
            }
        }
        catch (JsonException)
        {
            messages.Add(body.Length > 500 ? body[..500] : body);
        }

        return messages;
    }

    private static void CollectMessages(JsonElement element, ICollection<string> messages)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.String:
                var value = element.GetString();
                if (!string.IsNullOrWhiteSpace(value))
                {
                    messages.Add(value);
                }

                break;
            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                {
                    CollectMessages(item, messages);
                }

                break;
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                {
                    var nested = new List<string>();
                    CollectMessages(property.Value, nested);
                    foreach (var message in nested)
                    {
                        messages.Add($"{property.Name}: {message}");
                    }
                }

                break;
        }
    }
}
