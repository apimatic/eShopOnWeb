using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.Infrastructure.Maxio.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Typed HTTP client for the Maxio Advanced Billing REST API.
/// </summary>
/// <remarks>
/// Authentication is HTTP Basic with the API key as the username and the literal "x" as the
/// password, per Maxio's documented scheme. The credential is read per request from
/// <see cref="IOptionsMonitor{TOptions}"/> so a rotated secret takes effect without a restart,
/// and is never logged.
/// </remarks>
internal sealed class MaxioApiClient : IMaxioApiClient
{
    /// <summary>Maxio caps page size at 200 for these collections.</summary>
    private const int PageSize = 200;

    /// <summary>Guard against an unbounded paging loop if the API ever stops shrinking pages.</summary>
    private const int MaxPages = 50;

    private readonly HttpClient _httpClient;
    private readonly IOptionsMonitor<MaxioSettings> _settings;
    private readonly ILogger<MaxioApiClient> _logger;

    public MaxioApiClient(HttpClient httpClient, IOptionsMonitor<MaxioSettings> settings, ILogger<MaxioApiClient> logger)
    {
        _httpClient = httpClient;
        _settings = settings;
        _logger = logger;
    }

    public Task<MaxioSite?> GetSiteAsync(CancellationToken cancellationToken) =>
        GetOrNullAsync<MaxioSiteEnvelope, MaxioSite>("site.json", envelope => envelope.Site, cancellationToken);

    public Task<IReadOnlyList<MaxioProductFamily>> ListProductFamiliesAsync(CancellationToken cancellationToken) =>
        GetAllPagesAsync<MaxioProductFamilyEnvelope, MaxioProductFamily>(
            "product_families.json",
            envelope => envelope.ProductFamily,
            cancellationToken);

    public Task<IReadOnlyList<MaxioProduct>> ListProductsForFamilyAsync(long productFamilyId, CancellationToken cancellationToken) =>
        GetAllPagesAsync<MaxioProductEnvelope, MaxioProduct>(
            $"product_families/{productFamilyId}/products.json",
            envelope => envelope.Product,
            cancellationToken);

    public Task<MaxioCustomer?> FindCustomerByReferenceAsync(string reference, CancellationToken cancellationToken) =>
        GetOrNullAsync<MaxioCustomerEnvelope, MaxioCustomer>(
            $"customers/lookup.json?reference={Uri.EscapeDataString(reference)}",
            envelope => envelope.Customer,
            cancellationToken);

    public async Task<MaxioCustomer> CreateCustomerAsync(MaxioCreateCustomer customer, CancellationToken cancellationToken)
    {
        var envelope = await PostAsync<MaxioCreateCustomerRequest, MaxioCustomerEnvelope>(
            "customers.json",
            new MaxioCreateCustomerRequest { Customer = customer },
            cancellationToken);

        return envelope.Customer
               ?? throw new MaxioApiException(HttpStatusCode.OK, "POST", "customers.json",
                   new[] { "The API returned a success status with no customer payload." });
    }

    public async Task<IReadOnlyList<MaxioSubscription>> ListCustomerSubscriptionsAsync(long customerId, CancellationToken cancellationToken)
    {
        var envelopes = await SendAsync<List<MaxioSubscriptionEnvelope>>(
            HttpMethod.Get,
            $"customers/{customerId}/subscriptions.json",
            content: null,
            cancellationToken);

        return Unwrap(envelopes, envelope => envelope.Subscription);
    }

    public Task<MaxioSubscription?> FindSubscriptionByReferenceAsync(string reference, CancellationToken cancellationToken) =>
        GetOrNullAsync<MaxioSubscriptionEnvelope, MaxioSubscription>(
            $"subscriptions/lookup.json?reference={Uri.EscapeDataString(reference)}",
            envelope => envelope.Subscription,
            cancellationToken);

    public async Task<MaxioSubscription> CreateSubscriptionAsync(MaxioCreateSubscription subscription, CancellationToken cancellationToken)
    {
        var envelope = await PostAsync<MaxioCreateSubscriptionRequest, MaxioSubscriptionEnvelope>(
            "subscriptions.json",
            new MaxioCreateSubscriptionRequest { Subscription = subscription },
            cancellationToken);

        return envelope.Subscription
               ?? throw new MaxioApiException(HttpStatusCode.OK, "POST", "subscriptions.json",
                   new[] { "The API returned a success status with no subscription payload." });
    }

    private async Task<TResource?> GetOrNullAsync<TEnvelope, TResource>(
        string path,
        Func<TEnvelope, TResource?> select,
        CancellationToken cancellationToken)
        where TEnvelope : class
        where TResource : class
    {
        try
        {
            var envelope = await SendAsync<TEnvelope>(HttpMethod.Get, path, content: null, cancellationToken);
            return select(envelope);
        }
        catch (MaxioApiException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    private async Task<IReadOnlyList<TResource>> GetAllPagesAsync<TEnvelope, TResource>(
        string path,
        Func<TEnvelope, TResource?> select,
        CancellationToken cancellationToken)
        where TResource : class
    {
        var separator = path.Contains('?') ? '&' : '?';
        var results = new List<TResource>();

        for (var page = 1; page <= MaxPages; page++)
        {
            var pagePath = FormattableString.Invariant($"{path}{separator}page={page}&per_page={PageSize}");
            var envelopes = await SendAsync<List<TEnvelope>>(HttpMethod.Get, pagePath, content: null, cancellationToken);

            results.AddRange(Unwrap(envelopes, select));

            if (envelopes.Count < PageSize)
            {
                return results;
            }
        }

        _logger.LogWarning("Stopped paging {Path} after {MaxPages} pages; results may be truncated.", path, MaxPages);
        return results;
    }

    private static IReadOnlyList<TResource> Unwrap<TEnvelope, TResource>(
        IEnumerable<TEnvelope> envelopes,
        Func<TEnvelope, TResource?> select)
        where TResource : class
    {
        var results = new List<TResource>();

        foreach (var envelope in envelopes)
        {
            var resource = select(envelope);
            if (resource is not null)
            {
                results.Add(resource);
            }
        }

        return results;
    }

    private async Task<TResponse> PostAsync<TRequest, TResponse>(string path, TRequest body, CancellationToken cancellationToken)
    {
        using var content = JsonContent.Create(body, options: MaxioJson.Options);
        return await SendAsync<TResponse>(HttpMethod.Post, path, content, cancellationToken);
    }

    private async Task<TResponse> SendAsync<TResponse>(
        HttpMethod method,
        string path,
        HttpContent? content,
        CancellationToken cancellationToken)
    {
        if (_httpClient.BaseAddress is null)
        {
            throw new InvalidOperationException(
                $"The Maxio API base address is not configured. Set {MaxioSettings.ConfigurationSectionName}:{nameof(MaxioSettings.Subdomain)} " +
                $"or {MaxioSettings.ConfigurationSectionName}:{nameof(MaxioSettings.BaseUrl)}.");
        }

        using var request = new HttpRequestMessage(method, new Uri(_httpClient.BaseAddress, path))
        {
            Content = content,
            Headers = { Authorization = BuildAuthorizationHeader() }
        };
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        HttpResponseMessage response;
        try
        {
            response = await _httpClient.SendAsync(request, cancellationToken);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !cancellationToken.IsCancellationRequested)
        {
            throw new MaxioApiException(
                HttpStatusCode.ServiceUnavailable,
                method.Method,
                StripQuery(path),
                new[] { $"The Maxio API could not be reached: {ex.Message}" },
                ex);
        }

        using (response)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                throw new MaxioApiException(response.StatusCode, method.Method, StripQuery(path), MaxioErrorReader.Read(body));
            }

            _logger.LogDebug("Maxio {Method} {Path} -> {StatusCode}.", method.Method, StripQuery(path), (int)response.StatusCode);

            try
            {
                return JsonSerializer.Deserialize<TResponse>(body, MaxioJson.Options)
                       ?? throw new MaxioApiException(response.StatusCode, method.Method, StripQuery(path),
                           new[] { "The API returned an empty body where a payload was expected." });
            }
            catch (JsonException ex)
            {
                throw new MaxioApiException(response.StatusCode, method.Method, StripQuery(path),
                    new[] { $"The API returned a body that could not be parsed: {ex.Message}" }, ex);
            }
        }
    }

    private AuthenticationHeaderValue BuildAuthorizationHeader()
    {
        var apiKey = _settings.CurrentValue.ApiKey ?? string.Empty;

        // Maxio Advanced Billing: HTTP Basic, API key as the username, literal "x" as the password.
        var credential = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{apiKey}:x"));
        return new AuthenticationHeaderValue("Basic", credential);
    }

    /// <summary>
    /// Drops the query string before a path reaches an exception message or a log, so customer
    /// references and other request parameters are not echoed into diagnostics.
    /// </summary>
    private static string StripQuery(string path)
    {
        var index = path.IndexOf('?');
        return index < 0 ? path : path.Substring(0, index);
    }
}
