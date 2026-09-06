using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.Infrastructure.Maxio.Contracts;
using Microsoft.eShopWeb.Infrastructure.Maxio.Http;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Typed HTTP client for the Maxio Billing API. Authentication, retries and the concurrency cap are
/// configured on the underlying <see cref="HttpClient"/> pipeline; this class only maps requests and
/// responses, and turns failures into billing exceptions.
/// </summary>
public class MaxioApiClient : IMaxioApiClient
{
    /// <summary>Maxio caps list endpoints at 200 records per page.</summary>
    private const int MaxPageSize = 200;

    private readonly HttpClient _httpClient;
    private readonly ILogger<MaxioApiClient> _logger;

    public MaxioApiClient(HttpClient httpClient, ILogger<MaxioApiClient> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public async Task<IReadOnlyList<MaxioProduct>> ListProductsForFamilyAsync(
        string productFamilyHandle, CancellationToken cancellationToken = default)
    {
        // The product family path segment takes either a numeric id or a handle prefixed with "handle:".
        var path = $"product_families/handle:{Uri.EscapeDataString(productFamilyHandle)}/products.json" +
                   $"?per_page={MaxPageSize}";

        var envelopes = await SendAsync<List<MaxioProductEnvelope>>(
            HttpMethod.Get, path, null, $"list plans in product family {productFamilyHandle}", cancellationToken);

        return Unwrap(envelopes, e => e.Product);
    }

    public async Task<MaxioSite?> GetSiteAsync(CancellationToken cancellationToken = default)
    {
        var envelope = await SendAsync<MaxioSiteEnvelope>(
            HttpMethod.Get, "site.json", null, "read site settings", cancellationToken);

        return envelope?.Site;
    }

    public async Task<MaxioCustomer?> FindCustomerByReferenceAsync(
        string reference, CancellationToken cancellationToken = default)
    {
        var path = $"customers/lookup.json?reference={Uri.EscapeDataString(reference)}";

        var envelope = await SendAsync<MaxioCustomerEnvelope>(
            HttpMethod.Get, path, null, $"look up customer by reference {reference}",
            cancellationToken, treatNotFoundAsNull: true);

        return envelope?.Customer;
    }

    public async Task<MaxioCustomer> CreateCustomerAsync(
        MaxioCustomerAttributes customer, string uniquenessToken, CancellationToken cancellationToken = default)
    {
        var body = new MaxioCreateCustomerRequest { Customer = customer, UniquenessToken = uniquenessToken };

        var envelope = await SendAsync<MaxioCustomerEnvelope>(
            HttpMethod.Post, "customers.json", body, $"create customer {customer.Reference}", cancellationToken);

        return envelope?.Customer
            ?? throw new BillingGatewayException(
                "Maxio accepted the customer but returned no customer in the response.");
    }

    public async Task<IReadOnlyList<MaxioSubscription>> ListCustomerSubscriptionsAsync(
        long customerId, CancellationToken cancellationToken = default)
    {
        var envelopes = await SendAsync<List<MaxioSubscriptionEnvelope>>(
            HttpMethod.Get, $"customers/{customerId}/subscriptions.json", null,
            $"list subscriptions for customer {customerId}", cancellationToken, treatNotFoundAsNull: true);

        return Unwrap(envelopes, e => e.Subscription);
    }

    public async Task<MaxioSubscription> CreateSubscriptionAsync(
        MaxioSubscriptionAttributes subscription, string uniquenessToken, CancellationToken cancellationToken = default)
    {
        var body = new MaxioCreateSubscriptionRequest { Subscription = subscription, UniquenessToken = uniquenessToken };

        var envelope = await SendAsync<MaxioSubscriptionEnvelope>(
            HttpMethod.Post, "subscriptions.json", body,
            $"create subscription on plan {subscription.ProductHandle}", cancellationToken);

        return envelope?.Subscription
            ?? throw new BillingGatewayException(
                "Maxio accepted the subscription but returned no subscription in the response.");
    }

    private static IReadOnlyList<TItem> Unwrap<TEnvelope, TItem>(
        List<TEnvelope>? envelopes, Func<TEnvelope, TItem?> selector)
        where TItem : class
    {
        if (envelopes is null)
        {
            return Array.Empty<TItem>();
        }

        return envelopes.Select(selector).Where(item => item is not null).Select(item => item!).ToList();
    }

    private async Task<TResponse?> SendAsync<TResponse>(
        HttpMethod method,
        string path,
        object? content,
        string description,
        CancellationToken cancellationToken,
        bool treatNotFoundAsNull = false)
    {
        using var request = new HttpRequestMessage(method, path);

        if (content is not null)
        {
            request.Content = JsonContent.Create(content, content.GetType(), options: MaxioJson.Options);
        }

        HttpResponseMessage response;
        try
        {
            response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseContentRead, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException ex)
        {
            // The time budget for the whole call, retries included, ran out.
            throw new BillingUnavailableException(
                $"The Maxio Billing API did not respond in time while trying to {description}.", ex);
        }
        catch (HttpRequestException ex)
        {
            throw new BillingUnavailableException(
                $"The Maxio Billing API could not be reached while trying to {description}.", ex);
        }

        using (response)
        {
            if (treatNotFoundAsNull && response.StatusCode == HttpStatusCode.NotFound)
            {
                return default;
            }

            var payload = await response.Content.ReadAsStringAsync(cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                var errors = ParseErrors(payload);

                _logger.LogWarning(
                    "Maxio request to {Description} failed with HTTP {StatusCode}: {Errors}",
                    description, (int)response.StatusCode, string.Join("; ", errors));

                throw new MaxioApiException(response.StatusCode, errors, description);
            }

            if (string.IsNullOrWhiteSpace(payload))
            {
                return default;
            }

            try
            {
                return JsonSerializer.Deserialize<TResponse>(payload, MaxioJson.Options);
            }
            catch (JsonException ex)
            {
                throw new BillingGatewayException(
                    $"The Maxio Billing API returned a response that could not be read while trying to {description}.", ex);
            }
        }
    }

    /// <summary>
    /// Maxio reports failures as an "errors" array of strings and, for some resources, as an
    /// "errors" object keyed by field. Anything else is surfaced as a truncated raw body.
    /// </summary>
    private static IReadOnlyList<string> ParseErrors(string payload)
    {
        if (string.IsNullOrWhiteSpace(payload))
        {
            return Array.Empty<string>();
        }

        try
        {
            using var document = JsonDocument.Parse(payload);

            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return new[] { Truncate(payload) };
            }

            if (document.RootElement.TryGetProperty("errors", out var errors))
            {
                switch (errors.ValueKind)
                {
                    case JsonValueKind.Array:
                        return errors.EnumerateArray().Select(Describe).Where(e => e.Length > 0).ToArray();
                    case JsonValueKind.Object:
                        return errors.EnumerateObject().Select(p => $"{p.Name}: {Describe(p.Value)}").ToArray();
                    case JsonValueKind.String:
                        return new[] { Describe(errors) };
                }
            }

            if (document.RootElement.TryGetProperty("error", out var singleError))
            {
                return new[] { Describe(singleError) };
            }
        }
        catch (JsonException)
        {
            // Not JSON - fall through to the raw body.
        }

        return new[] { Truncate(payload) };
    }

    private static string Describe(JsonElement element) =>
        element.ValueKind == JsonValueKind.String ? element.GetString() ?? string.Empty : element.ToString();

    private static string Truncate(string value, int maxLength = 500) =>
        value.Length <= maxLength ? value : value[..maxLength] + "...";
}
