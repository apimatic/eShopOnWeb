using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.Infrastructure.Billing.Maxio.Model;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.Infrastructure.Billing.Maxio;

/// <summary>
/// HTTP binding for the Advanced Billing endpoints this integration uses.
///
/// Endpoint paths, envelope keys and field names were taken from the official Maxio .NET SDK
/// (github.com/maxio-com/ab-dotnet-sdk) and confirmed against a live sandbox site. Authentication
/// is HTTP Basic with the site API key as the username and the literal "x" as the password; the
/// header is attached by the DI registration so no credential ever passes through this class.
/// </summary>
internal sealed class MaxioApiClient : IMaxioApiClient
{
    /// <summary>Advanced Billing caps page size at 200 for list endpoints.</summary>
    private const int MaxPageSize = 200;

    /// <summary>Guards against an unbounded loop if a list endpoint ever stops honouring paging.</summary>
    private const int MaxPages = 50;

    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        // Every property is mapped explicitly with [JsonPropertyName]; nothing is inferred.
        PropertyNameCaseInsensitive = false,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly HttpClient _httpClient;
    private readonly ILogger<MaxioApiClient> _logger;

    public MaxioApiClient(HttpClient httpClient, ILogger<MaxioApiClient> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public async Task<MaxioSite> GetSiteAsync(CancellationToken cancellationToken)
    {
        var envelope = await GetAsync<MaxioSiteEnvelope>("site.json", cancellationToken).ConfigureAwait(false);
        return envelope?.Site ?? throw Malformed("GET", "site.json", "site");
    }

    public async Task<MaxioProductFamily?> FindProductFamilyByHandleAsync(string handle, CancellationToken cancellationToken)
    {
        // /product_families/{handle}.json is not supported - the path segment is the numeric id
        // only (verified: it answers 404 for a handle). So list and match on the handle, which is
        // the identifier that stays stable when a catalog is re-seeded.
        var families = await GetPagedAsync<MaxioProductFamilyEnvelope>(
            "product_families.json",
            query: null,
            cancellationToken).ConfigureAwait(false);

        return families
            .Select(e => e.ProductFamily)
            .FirstOrDefault(f => f is not null
                && string.Equals(f.Handle, handle, StringComparison.OrdinalIgnoreCase)
                && f.ArchivedAt is null);
    }

    public async Task<IReadOnlyList<MaxioProduct>> ListProductsForFamilyAsync(int productFamilyId, CancellationToken cancellationToken)
    {
        var products = await GetPagedAsync<MaxioProductEnvelope>(
            $"product_families/{productFamilyId}/products.json",
            query: "include_archived=false",
            cancellationToken).ConfigureAwait(false);

        return products
            .Select(e => e.Product)
            .Where(p => p is not null && p.ArchivedAt is null)
            .Select(p => p!)
            .ToList();
    }

    public async Task<MaxioCustomer?> FindCustomerByReferenceAsync(string reference, CancellationToken cancellationToken)
    {
        // The lookup endpoint answers 404 - not an empty body - when the reference is unknown.
        var envelope = await GetOrNullAsync<MaxioCustomerEnvelope>(
            $"customers/lookup.json?reference={Uri.EscapeDataString(reference)}",
            cancellationToken).ConfigureAwait(false);

        return envelope?.Customer;
    }

    public async Task<MaxioCustomer> CreateCustomerAsync(MaxioCreateCustomer customer, CancellationToken cancellationToken)
    {
        var envelope = await PostAsync<MaxioCreateCustomerRequest, MaxioCustomerEnvelope>(
            "customers.json",
            new MaxioCreateCustomerRequest { Customer = customer },
            cancellationToken).ConfigureAwait(false);

        return envelope?.Customer ?? throw Malformed("POST", "customers.json", "customer");
    }

    public async Task<IReadOnlyList<MaxioSubscription>> ListCustomerSubscriptionsAsync(int customerId, CancellationToken cancellationToken)
    {
        var envelopes = await GetAsync<List<MaxioSubscriptionEnvelope>>(
            $"customers/{customerId}/subscriptions.json",
            cancellationToken).ConfigureAwait(false);

        return envelopes?
            .Select(e => e.Subscription)
            .Where(s => s is not null)
            .Select(s => s!)
            .ToList() ?? new List<MaxioSubscription>();
    }

    public async Task<MaxioSubscription?> FindSubscriptionByReferenceAsync(string reference, CancellationToken cancellationToken)
    {
        var envelope = await GetOrNullAsync<MaxioSubscriptionEnvelope>(
            $"subscriptions/lookup.json?reference={Uri.EscapeDataString(reference)}",
            cancellationToken).ConfigureAwait(false);

        return envelope?.Subscription;
    }

    public async Task<MaxioSubscription> CreateSubscriptionAsync(MaxioCreateSubscription subscription, CancellationToken cancellationToken)
    {
        var envelope = await PostAsync<MaxioCreateSubscriptionRequest, MaxioSubscriptionEnvelope>(
            "subscriptions.json",
            new MaxioCreateSubscriptionRequest { Subscription = subscription },
            cancellationToken).ConfigureAwait(false);

        return envelope?.Subscription ?? throw Malformed("POST", "subscriptions.json", "subscription");
    }

    private async Task<T?> GetAsync<T>(string path, CancellationToken cancellationToken)
    {
        using var response = await SendAsync(System.Net.Http.HttpMethod.Get, path, content: null, cancellationToken).ConfigureAwait(false);
        await EnsureSuccessAsync(response, "GET", path, cancellationToken).ConfigureAwait(false);
        return await ReadAsync<T>(response, "GET", path, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>GET that treats 404 as "no such resource" rather than a failure.</summary>
    private async Task<T?> GetOrNullAsync<T>(string path, CancellationToken cancellationToken)
    {
        using var response = await SendAsync(System.Net.Http.HttpMethod.Get, path, content: null, cancellationToken).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return default;
        }

        await EnsureSuccessAsync(response, "GET", path, cancellationToken).ConfigureAwait(false);
        return await ReadAsync<T>(response, "GET", path, cancellationToken).ConfigureAwait(false);
    }

    private async Task<TResponse?> PostAsync<TRequest, TResponse>(string path, TRequest body, CancellationToken cancellationToken)
    {
        using var content = JsonContent.Create(body, options: SerializerOptions);
        using var response = await SendAsync(System.Net.Http.HttpMethod.Post, path, content, cancellationToken).ConfigureAwait(false);
        await EnsureSuccessAsync(response, "POST", path, cancellationToken).ConfigureAwait(false);
        return await ReadAsync<TResponse>(response, "POST", path, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Walks every page of a list endpoint and concatenates the results.</summary>
    private async Task<List<T>> GetPagedAsync<T>(string path, string? query, CancellationToken cancellationToken)
    {
        var results = new List<T>();

        for (var page = 1; page <= MaxPages; page++)
        {
            var separator = path.Contains('?') ? "&" : "?";
            var pagedPath = $"{path}{separator}page={page}&per_page={MaxPageSize}";
            if (!string.IsNullOrEmpty(query))
            {
                pagedPath += "&" + query;
            }

            var batch = await GetAsync<List<T>>(pagedPath, cancellationToken).ConfigureAwait(false);
            if (batch is null || batch.Count == 0)
            {
                break;
            }

            results.AddRange(batch);

            if (batch.Count < MaxPageSize)
            {
                break;
            }
        }

        return results;
    }

    private async Task<HttpResponseMessage> SendAsync(
        System.Net.Http.HttpMethod method,
        string path,
        HttpContent? content,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, path) { Content = content };

        try
        {
            return await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException)
        {
            // No status code: the call never produced a response, so it is transient by definition.
            throw new MaxioApiException(
                $"Could not reach the Maxio Advanced Billing API ({method} {path}): {ex.Message}",
                method.Method,
                path,
                statusCode: null,
                innerException: ex);
        }
    }

    private async Task<T?> ReadAsync<T>(HttpResponseMessage response, string method, string path, CancellationToken cancellationToken)
    {
        try
        {
            return await response.Content.ReadFromJsonAsync<T>(SerializerOptions, cancellationToken).ConfigureAwait(false);
        }
        catch (JsonException ex)
        {
            throw new MaxioApiException(
                $"The Maxio Advanced Billing API returned a body that could not be parsed ({method} {path}).",
                method,
                path,
                response.StatusCode,
                innerException: ex);
        }
    }

    private async Task EnsureSuccessAsync(HttpResponseMessage response, string method, string path, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var errors = await ReadErrorsAsync(response, cancellationToken).ConfigureAwait(false);
        var detail = errors.Count > 0 ? string.Join("; ", errors) : response.ReasonPhrase ?? "no detail supplied";

        _logger.LogWarning(
            "Maxio Advanced Billing rejected {Method} {Path} with {StatusCode}: {Detail}",
            method,
            Redact(path),
            (int)response.StatusCode,
            detail);

        throw new MaxioApiException(
            $"Maxio Advanced Billing returned {(int)response.StatusCode} for {method} {Redact(path)}: {detail}",
            method,
            Redact(path),
            response.StatusCode,
            errors);
    }

    /// <summary>
    /// Advanced Billing reports validation problems either as {"errors": ["..."]} or as
    /// {"errors": {"field": ["..."]}}. Both are flattened to a list of human readable messages.
    /// </summary>
    private static async Task<IReadOnlyList<string>> ReadErrorsAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var messages = new List<string>();

        string body;
        try
        {
            body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
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
            if (document.RootElement.ValueKind != JsonValueKind.Object
                || !document.RootElement.TryGetProperty("errors", out var errors))
            {
                return messages;
            }

            switch (errors.ValueKind)
            {
                case JsonValueKind.String:
                    Add(messages, errors.GetString());
                    break;

                case JsonValueKind.Array:
                    foreach (var item in errors.EnumerateArray())
                    {
                        Add(messages, item.ValueKind == JsonValueKind.String ? item.GetString() : item.ToString());
                    }

                    break;

                case JsonValueKind.Object:
                    foreach (var field in errors.EnumerateObject())
                    {
                        if (field.Value.ValueKind == JsonValueKind.Array)
                        {
                            foreach (var item in field.Value.EnumerateArray())
                            {
                                Add(messages, $"{field.Name}: {item}");
                            }
                        }
                        else
                        {
                            Add(messages, $"{field.Name}: {field.Value}");
                        }
                    }

                    break;
            }
        }
        catch (JsonException)
        {
            // A non-JSON error body (an HTML gateway page, say) carries nothing worth surfacing.
        }

        return messages;

        static void Add(List<string> target, string? message)
        {
            if (!string.IsNullOrWhiteSpace(message))
            {
                target.Add(message.Trim());
            }
        }
    }

    /// <summary>
    /// Strips query values before a path reaches a log or an error message. Lookup paths carry a
    /// customer reference, which is derived from the shopper's account identity.
    /// </summary>
    private static string Redact(string path)
    {
        var separator = path.IndexOf('?');
        return separator < 0 ? path : path[..separator];
    }

    private static MaxioApiException Malformed(string method, string path, string expectedKey) =>
        new($"Maxio Advanced Billing returned a success response for {method} {path} without the expected '{expectedKey}' object.",
            method,
            path);
}
