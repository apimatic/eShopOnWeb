using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <inheritdoc cref="IMaxioApiClient"/>
public class MaxioApiClient : IMaxioApiClient
{
    /// <summary>Maximum page size the API accepts; anything larger is clamped by the server.</summary>
    private const int PageSize = 200;

    /// <summary>
    /// Safety valve so a server that never shrinks a page cannot spin this client forever.
    /// At <see cref="PageSize"/> records per page this still covers 10,000 records.
    /// </summary>
    private const int MaxPages = 50;

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly HttpClient _httpClient;
    private readonly ILogger<MaxioApiClient> _logger;

    public MaxioApiClient(HttpClient httpClient, ILogger<MaxioApiClient> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public Task<IReadOnlyList<MaxioProduct>> ListProductsForFamilyAsync(string productFamilyHandle, CancellationToken cancellationToken = default)
    {
        // The family may be addressed by id or, as here, by handle. Numeric ids are reassigned when a
        // site is re-seeded; handles are not.
        var path = "product_families/handle:" + Uri.EscapeDataString(productFamilyHandle) + "/products.json";
        return ListPagedAsync<MaxioProductEnvelope, MaxioProduct>(path, envelope => envelope.Product, cancellationToken);
    }

    public async Task<MaxioCustomer?> FindCustomerByReferenceAsync(string reference, CancellationToken cancellationToken = default)
    {
        var path = "customers/lookup.json?reference=" + Uri.EscapeDataString(reference);
        var envelope = await SendAsync<MaxioCustomerEnvelope>(HttpMethod.Get, path, body: null, treatNotFoundAsMissing: true, cancellationToken);
        return envelope?.Customer;
    }

    public async Task<MaxioCustomer> CreateCustomerAsync(MaxioCustomerAttributes customer, CancellationToken cancellationToken = default)
    {
        var request = new MaxioCreateCustomerRequest { Customer = customer };
        var envelope = await SendAsync<MaxioCustomerEnvelope>(HttpMethod.Post, "customers.json", request, treatNotFoundAsMissing: false, cancellationToken);

        return envelope?.Customer ?? throw new MaxioApiException(HttpStatusCode.OK, "POST", "customers.json",
            new[] { "The API reported success but returned no customer." });
    }

    public async Task<MaxioSubscription> CreateSubscriptionAsync(MaxioCreateSubscriptionRequest request, CancellationToken cancellationToken = default)
    {
        var envelope = await SendAsync<MaxioSubscriptionEnvelope>(HttpMethod.Post, "subscriptions.json", request, treatNotFoundAsMissing: false, cancellationToken);

        return envelope?.Subscription ?? throw new MaxioApiException(HttpStatusCode.OK, "POST", "subscriptions.json",
            new[] { "The API reported success but returned no subscription." });
    }

    public async Task<MaxioSite> ReadSiteAsync(CancellationToken cancellationToken = default)
    {
        var envelope = await SendAsync<MaxioSiteEnvelope>(HttpMethod.Get, "site.json", body: null, treatNotFoundAsMissing: false, cancellationToken);

        return envelope?.Site ?? throw new MaxioApiException(HttpStatusCode.OK, "GET", "site.json",
            new[] { "The API reported success but returned no site." });
    }

    public Task<IReadOnlyList<MaxioSubscription>> ListCustomerSubscriptionsAsync(long customerId, CancellationToken cancellationToken = default)
    {
        var path = "customers/" + customerId.ToString(CultureInfo.InvariantCulture) + "/subscriptions.json";
        return ListPagedAsync<MaxioSubscriptionEnvelope, MaxioSubscription>(path, envelope => envelope.Subscription, cancellationToken);
    }

    /// <summary>
    /// Walks every page of a list endpoint. List responses are arrays of single-property envelopes.
    /// </summary>
    private async Task<IReadOnlyList<TItem>> ListPagedAsync<TEnvelope, TItem>(
        string path, Func<TEnvelope, TItem?> select, CancellationToken cancellationToken)
        where TItem : class
    {
        var separator = path.Contains('?', StringComparison.Ordinal) ? "&" : "?";
        var results = new List<TItem>();

        for (var page = 1; page <= MaxPages; page++)
        {
            var pagePath = path + separator
                + "page=" + page.ToString(CultureInfo.InvariantCulture)
                + "&per_page=" + PageSize.ToString(CultureInfo.InvariantCulture);

            var envelopes = await SendAsync<List<TEnvelope>>(HttpMethod.Get, pagePath, body: null, treatNotFoundAsMissing: false, cancellationToken);

            if (envelopes is null || envelopes.Count == 0)
            {
                break;
            }

            foreach (var envelope in envelopes)
            {
                var item = select(envelope);
                if (item is not null)
                {
                    results.Add(item);
                }
            }

            if (envelopes.Count < PageSize)
            {
                break;
            }
        }

        return results;
    }

    private async Task<T?> SendAsync<T>(HttpMethod method, string path, object? body, bool treatNotFoundAsMissing, CancellationToken cancellationToken)
        where T : class
    {
        using var request = new HttpRequestMessage(method, path);
        if (body is not null)
        {
            // The API rejects form encoded payloads, so the content type has to be application/json.
            request.Content = JsonContent.Create(body, body.GetType(), options: SerializerOptions);
        }

        var stopwatch = Stopwatch.StartNew();
        using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseContentRead, cancellationToken);
        stopwatch.Stop();

        _logger.LogInformation("Maxio {Method} {Path} responded {StatusCode} in {ElapsedMilliseconds} ms",
            method.Method, path, (int)response.StatusCode, stopwatch.ElapsedMilliseconds);

        if (response.StatusCode == HttpStatusCode.NotFound && treatNotFoundAsMissing)
        {
            return null;
        }

        var content = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw BuildException(response.StatusCode, method.Method, path, content);
        }

        if (string.IsNullOrWhiteSpace(content))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<T>(content, SerializerOptions);
        }
        catch (JsonException ex)
        {
            throw new MaxioApiException(response.StatusCode, method.Method, path,
                new[] { "The API returned a response that could not be parsed: " + ex.Message });
        }
    }

    private static MaxioApiException BuildException(HttpStatusCode statusCode, string method, string path, string content)
    {
        var errors = ParseErrors(content);

        return statusCode switch
        {
            HttpStatusCode.Conflict => new MaxioDuplicateSubmissionException(method, path, errors),
            HttpStatusCode.UnprocessableEntity => new MaxioValidationException(method, path, errors),
            _ => new MaxioApiException(statusCode, method, path, errors)
        };
    }

    /// <summary>
    /// Errors arrive as <c>{"errors": ["...", "..."]}</c> and, for some endpoints, as an object keyed by
    /// field name. Parsing stays defensive: an error body is exactly where an intermediary is most
    /// likely to hand back something that is not JSON at all.
    /// </summary>
    private static IReadOnlyList<string> ParseErrors(string content)
    {
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(content))
        {
            return errors;
        }

        try
        {
            using var document = JsonDocument.Parse(content);
            if (!document.RootElement.TryGetProperty("errors", out var errorsElement))
            {
                return errors;
            }

            switch (errorsElement.ValueKind)
            {
                case JsonValueKind.Array:
                    foreach (var element in errorsElement.EnumerateArray())
                    {
                        var value = element.ValueKind == JsonValueKind.String ? element.GetString() : element.ToString();
                        if (!string.IsNullOrWhiteSpace(value))
                        {
                            errors.Add(value!);
                        }
                    }

                    break;

                case JsonValueKind.Object:
                    foreach (var property in errorsElement.EnumerateObject())
                    {
                        var value = property.Value.ValueKind == JsonValueKind.String ? property.Value.GetString() : property.Value.ToString();
                        errors.Add(property.Name + ": " + value);
                    }

                    break;

                case JsonValueKind.String:
                    var single = errorsElement.GetString();
                    if (!string.IsNullOrWhiteSpace(single))
                    {
                        errors.Add(single!);
                    }

                    break;
            }
        }
        catch (JsonException)
        {
            // Not JSON. Fall through with an empty error list rather than masking the status code.
        }

        return errors;
    }
}
