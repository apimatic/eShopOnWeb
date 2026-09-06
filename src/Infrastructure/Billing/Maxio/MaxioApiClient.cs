using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.Infrastructure.Billing.Maxio.Contracts;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.Infrastructure.Billing.Maxio;

/// <summary>
/// Talks to the Maxio Advanced Billing REST API over a typed <see cref="HttpClient"/>.
/// <para>
/// Authentication, base address, timeout and retry are configured on the client in
/// <see cref="MaxioBillingServiceCollectionExtensions"/>; this type is only responsible for
/// shaping requests, reading envelopes and turning failures into the billing exceptions the rest
/// of the application understands.
/// </para>
/// </summary>
public class MaxioApiClient : IMaxioApiClient
{
    /// <summary>Maxio speaks snake_case, which the contracts declare explicitly per property.</summary>
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    private readonly HttpClient _httpClient;
    private readonly ILogger<MaxioApiClient> _logger;

    public MaxioApiClient(HttpClient httpClient, ILogger<MaxioApiClient> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public async Task<IReadOnlyList<MaxioProduct>> ListProductsForFamilyAsync(
        string productFamilyHandle,
        CancellationToken cancellationToken = default)
    {
        // Maxio accepts either the family's numeric id or its handle prefixed with "handle:" in
        // this path segment. Using the handle is what keeps the integration working across sites,
        // where ids differ but handles do not.
        var path = $"product_families/handle:{Uri.EscapeDataString(productFamilyHandle)}/products.json?per_page=200";

        var envelopes = await SendAsync<List<MaxioProductEnvelope>>(HttpMethod.Get, path, null, cancellationToken)
            ?? new List<MaxioProductEnvelope>();

        return envelopes
            .Select(envelope => envelope.Product)
            .Where(product => product is not null)
            .Select(product => product!)
            .ToList();
    }

    public async Task<MaxioCustomer?> FindCustomerByReferenceAsync(
        string reference,
        CancellationToken cancellationToken = default)
    {
        var path = $"customers/lookup.json?reference={Uri.EscapeDataString(reference)}";
        var envelope = await SendAsync<MaxioCustomerEnvelope>(
            HttpMethod.Get, path, null, cancellationToken, treatNotFoundAsNull: true);

        return envelope?.Customer;
    }

    public async Task<MaxioCustomer> CreateCustomerAsync(
        MaxioCreateCustomer customer,
        CancellationToken cancellationToken = default)
    {
        var request = new MaxioCreateCustomerRequest { Customer = customer };
        var envelope = await SendAsync<MaxioCustomerEnvelope>(
            HttpMethod.Post, "customers.json", request, cancellationToken);

        return envelope?.Customer
            ?? throw new BillingGatewayException("Maxio accepted the customer but returned no customer body.");
    }

    public async Task<IReadOnlyList<MaxioSubscription>> ListCustomerSubscriptionsAsync(
        long customerId,
        CancellationToken cancellationToken = default)
    {
        var path = $"customers/{customerId.ToString(CultureInfo.InvariantCulture)}/subscriptions.json";

        var envelopes = await SendAsync<List<MaxioSubscriptionEnvelope>>(HttpMethod.Get, path, null, cancellationToken)
            ?? new List<MaxioSubscriptionEnvelope>();

        return envelopes
            .Select(envelope => envelope.Subscription)
            .Where(subscription => subscription is not null)
            .Select(subscription => subscription!)
            .ToList();
    }

    public async Task<MaxioSubscription> CreateSubscriptionAsync(
        MaxioCreateSubscription subscription,
        CancellationToken cancellationToken = default)
    {
        var request = new MaxioCreateSubscriptionRequest { Subscription = subscription };
        var envelope = await SendAsync<MaxioSubscriptionEnvelope>(
            HttpMethod.Post, "subscriptions.json", request, cancellationToken);

        return envelope?.Subscription
            ?? throw new BillingGatewayException("Maxio accepted the subscription but returned no subscription body.");
    }

    public async Task<MaxioSubscription?> FindSubscriptionByReferenceAsync(
        string reference,
        CancellationToken cancellationToken = default)
    {
        var path = $"subscriptions/lookup.json?reference={Uri.EscapeDataString(reference)}";
        var envelope = await SendAsync<MaxioSubscriptionEnvelope>(
            HttpMethod.Get, path, null, cancellationToken, treatNotFoundAsNull: true);

        return envelope?.Subscription;
    }

    public async Task<MaxioSite> GetSiteAsync(CancellationToken cancellationToken = default)
    {
        var envelope = await SendAsync<MaxioSiteEnvelope>(HttpMethod.Get, "site.json", null, cancellationToken);

        return envelope?.Site
            ?? throw new BillingGatewayException("Maxio returned no site body.");
    }

    private async Task<TResponse?> SendAsync<TResponse>(
        HttpMethod method,
        string relativePath,
        object? body,
        CancellationToken cancellationToken,
        bool treatNotFoundAsNull = false)
        where TResponse : class
    {
        using var request = new HttpRequestMessage(method, relativePath);

        if (body is not null)
        {
            request.Content = JsonContent.Create(body, body.GetType(), options: SerializerOptions);
        }

        var stopwatch = Stopwatch.StartNew();
        HttpResponseMessage response;

        try
        {
            response = await _httpClient.SendAsync(request, cancellationToken);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // HttpClient reports its own timeout as a cancellation that the caller did not ask for.
            _logger.LogError("Maxio {Method} {Path} timed out after {Elapsed}ms.",
                method, relativePath, stopwatch.ElapsedMilliseconds);

            throw new BillingGatewayException(
                $"The billing system did not respond within {_httpClient.Timeout.TotalSeconds:0} seconds.");
        }
        catch (HttpRequestException exception)
        {
            _logger.LogError(exception, "Maxio {Method} {Path} failed to reach the billing system.",
                method, relativePath);

            throw new BillingGatewayException("The billing system could not be reached.", exception);
        }

        using (response)
        {
            var payload = await response.Content.ReadAsStringAsync(cancellationToken);

            _logger.LogInformation("Maxio {Method} {Path} responded {StatusCode} in {Elapsed}ms.",
                method, relativePath, (int)response.StatusCode, stopwatch.ElapsedMilliseconds);

            if (response.StatusCode == HttpStatusCode.NotFound && treatNotFoundAsNull)
            {
                return null;
            }

            if (!response.IsSuccessStatusCode)
            {
                throw Translate(response.StatusCode, payload, method, relativePath);
            }

            if (string.IsNullOrWhiteSpace(payload))
            {
                return null;
            }

            try
            {
                return JsonSerializer.Deserialize<TResponse>(payload, SerializerOptions);
            }
            catch (JsonException exception)
            {
                _logger.LogError(exception, "Maxio {Method} {Path} returned a body that could not be read.",
                    method, relativePath);

                throw new BillingGatewayException(
                    "The billing system returned a response this application could not read.",
                    exception,
                    (int)response.StatusCode);
            }
        }
    }

    private BillingException Translate(HttpStatusCode statusCode, string payload, HttpMethod method, string path)
    {
        var errors = ReadErrors(payload);

        // 422 is Maxio's "your request was rejected" and its messages are written for humans, so
        // they are the one thing worth passing back to the caller verbatim.
        if (statusCode == HttpStatusCode.UnprocessableEntity)
        {
            _logger.LogWarning("Maxio {Method} {Path} rejected the request: {Errors}",
                method, path, string.Join("; ", errors));

            return new BillingValidationException(errors);
        }

        _logger.LogError("Maxio {Method} {Path} failed with {StatusCode}: {Errors}",
            method, path, (int)statusCode, string.Join("; ", errors));

        var detail = errors.Count > 0
            ? string.Join("; ", errors)
            : Describe(statusCode);

        return new BillingGatewayException(
            $"The billing system returned {(int)statusCode}: {detail}",
            (int)statusCode);
    }

    private static string Describe(HttpStatusCode statusCode) => statusCode switch
    {
        HttpStatusCode.Unauthorized => "the API key was rejected",
        HttpStatusCode.Forbidden => "the API key is not allowed to perform this operation",
        HttpStatusCode.NotFound => "the resource does not exist on this site",
        (HttpStatusCode)429 => "the site's API rate limit was exceeded",
        _ => "no further detail was supplied"
    };

    /// <summary>
    /// Reads the error messages out of a failure body. Maxio uses a flat <c>{"errors": ["..."]}</c>
    /// for most resources but a per-field map (<c>{"errors": {"email": ["..."]}}</c>) for some
    /// customer validations, and returns HTML for a few infrastructure-level failures.
    /// </summary>
    private static IReadOnlyList<string> ReadErrors(string payload)
    {
        if (string.IsNullOrWhiteSpace(payload))
        {
            return Array.Empty<string>();
        }

        try
        {
            using var document = JsonDocument.Parse(payload);

            if (!document.RootElement.TryGetProperty("errors", out var errors))
            {
                return document.RootElement.TryGetProperty("error", out var single) &&
                       single.ValueKind == JsonValueKind.String
                    ? new[] { single.GetString()! }
                    : Array.Empty<string>();
            }

            switch (errors.ValueKind)
            {
                case JsonValueKind.String:
                    return new[] { errors.GetString()! };

                case JsonValueKind.Array:
                    return errors.EnumerateArray()
                        .Select(element => element.ToString())
                        .Where(message => !string.IsNullOrWhiteSpace(message))
                        .ToList();

                case JsonValueKind.Object:
                    return errors.EnumerateObject()
                        .SelectMany(property => property.Value.ValueKind == JsonValueKind.Array
                            ? property.Value.EnumerateArray().Select(element => $"{property.Name}: {element}")
                            : new[] { $"{property.Name}: {property.Value}" })
                        .ToList();

                default:
                    return Array.Empty<string>();
            }
        }
        catch (JsonException)
        {
            // Not JSON at all (an HTML error page, for instance) — nothing useful to relay.
            return Array.Empty<string>();
        }
    }
}
