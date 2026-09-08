using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

/// <summary>
/// Thin client over the Maxio Advanced Billing REST API. All Maxio interaction flows through here
/// (see https://{subdomain}.chargify.com). Authentication is HTTP Basic with the API key and password "X".
/// </summary>
public interface IMaxioBillingClient
{
    Task<MaxioSite> GetSiteAsync(CancellationToken cancellationToken);

    /// <summary>Returns the customer matching the unique reference, or null when none exists.</summary>
    Task<MaxioCustomer?> FindCustomerByReferenceAsync(string reference, CancellationToken cancellationToken);

    Task<MaxioCustomer> CreateCustomerAsync(MaxioCustomerDraft draft, string uniquenessToken, CancellationToken cancellationToken);

    Task<MaxioSubscription> CreateSubscriptionAsync(MaxioSubscriptionDraft draft, string uniquenessToken, CancellationToken cancellationToken);

    Task<IReadOnlyList<MaxioSubscription>> ListCustomerSubscriptionsAsync(long customerId, CancellationToken cancellationToken);

    Task<IReadOnlyList<MaxioProduct>> ListProductsByFamilyAsync(string productFamilyHandle, CancellationToken cancellationToken);
}

public class MaxioBillingClient : IMaxioBillingClient
{
    private const string BasicAuthPassword = "X";
    private const int ProductsPerPage = 200;
    private const int RequestTimeoutSeconds = 30;

    private readonly HttpClient _httpClient;
    private readonly MaxioOptions _options;

    public MaxioBillingClient(HttpClient httpClient, IOptions<MaxioOptions> options)
    {
        _httpClient = httpClient;
        _options = options.Value;

        if (_options.IsConfigured)
        {
            _httpClient.BaseAddress = new Uri(_options.ResolveBaseUrl());
            var token = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{_options.RequireApiKey()}:{BasicAuthPassword}"));
            _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", token);
            _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            _httpClient.Timeout = TimeSpan.FromSeconds(RequestTimeoutSeconds);
        }
    }

    public async Task<MaxioSite> GetSiteAsync(CancellationToken cancellationToken)
    {
        using var response = await SendAsync(HttpMethod.Get, "site.json", null, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        var envelope = await ReadJsonAsync<MaxioSiteEnvelope>(response, cancellationToken);
        return envelope.Site;
    }

    public async Task<MaxioCustomer?> FindCustomerByReferenceAsync(string reference, CancellationToken cancellationToken)
    {
        var path = $"customers/lookup.json?reference={Uri.EscapeDataString(reference)}";
        using var response = await SendAsync(HttpMethod.Get, path, null, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        await EnsureSuccessAsync(response, cancellationToken);
        var envelope = await ReadJsonAsync<MaxioCustomerEnvelope>(response, cancellationToken);
        return envelope.Customer;
    }

    public async Task<MaxioCustomer> CreateCustomerAsync(MaxioCustomerDraft draft, string uniquenessToken, CancellationToken cancellationToken)
    {
        var body = JsonSerializer.SerializeToUtf8Bytes(new CreateCustomerEnvelope
        {
            Customer = draft,
            UniquenessToken = uniquenessToken
        }, MaxioJson.Options);

        using var content = new ByteArrayContent(body);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        using var response = await SendAsync(HttpMethod.Post, "customers.json", content, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        var envelope = await ReadJsonAsync<MaxioCustomerEnvelope>(response, cancellationToken);
        return envelope.Customer;
    }

    public async Task<MaxioSubscription> CreateSubscriptionAsync(MaxioSubscriptionDraft draft, string uniquenessToken, CancellationToken cancellationToken)
    {
        var body = JsonSerializer.SerializeToUtf8Bytes(new CreateSubscriptionEnvelope
        {
            Subscription = draft,
            UniquenessToken = uniquenessToken
        }, MaxioJson.Options);

        using var content = new ByteArrayContent(body);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        using var response = await SendAsync(HttpMethod.Post, "subscriptions.json", content, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        var envelope = await ReadJsonAsync<MaxioSubscriptionEnvelope>(response, cancellationToken);
        return envelope.Subscription;
    }

    public async Task<IReadOnlyList<MaxioSubscription>> ListCustomerSubscriptionsAsync(long customerId, CancellationToken cancellationToken)
    {
        var path = $"customers/{customerId}/subscriptions.json";
        using var response = await SendAsync(HttpMethod.Get, path, null, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);

        var items = await ReadJsonAsync<List<MaxioSubscriptionItem>>(response, cancellationToken);
        return items.Select(item => item.Subscription).ToList();
    }

    public async Task<IReadOnlyList<MaxioProduct>> ListProductsByFamilyAsync(string productFamilyHandle, CancellationToken cancellationToken)
    {
        var products = new List<MaxioProduct>();
        var page = 1;

        while (true)
        {
            var path = $"product_families/handle:{Uri.EscapeDataString(productFamilyHandle)}/products.json?per_page={ProductsPerPage}&page={page}";
            using var response = await SendAsync(HttpMethod.Get, path, null, cancellationToken);
            await EnsureSuccessAsync(response, cancellationToken);

            var items = await ReadJsonAsync<List<MaxioProductItem>>(response, cancellationToken);
            foreach (var item in items)
            {
                products.Add(item.Product);
            }
            if (items.Count < ProductsPerPage)
            {
                break;
            }

            page++;
        }

        return products;
    }

    private void EnsureConfigured()
    {
        if (!_options.IsConfigured)
        {
            throw new MaxioConfigurationException(
                "Maxio Advanced Billing is not configured. Set 'Maxio:ApiKey' (MAXIO_API_KEY) and either " +
                "'Maxio:Subdomain' (MAXIO_SITE_SUBDOMAIN) or 'Maxio:BaseUrl' before calling a subscription endpoint.");
        }
    }

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, HttpContent? content, CancellationToken cancellationToken)
    {
        EnsureConfigured();

        using var request = new HttpRequestMessage(method, path)
        {
            Content = content
        };

        try
        {
            return await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        }
        catch (HttpRequestException ex)
        {
            throw new MaxioApiException($"Unable to reach the Maxio Advanced Billing API: {ex.Message}");
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new MaxioApiException("The Maxio Advanced Billing API request timed out.");
        }
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        string body;
        try
        {
            body = await response.Content.ReadAsStringAsync(cancellationToken);
        }
        catch
        {
            body = string.Empty;
        }

        var errors = MaxioErrorParser.Parse(body);
        var detail = errors.Count > 0
            ? string.Join(" ", errors)
            : (string.IsNullOrWhiteSpace(body) ? response.ReasonPhrase ?? "Unknown error" : body.Trim());

        throw new MaxioApiException(
            $"Maxio Advanced Billing API returned {(int)response.StatusCode} ({response.ReasonPhrase}). {detail}",
            (int)response.StatusCode,
            errors);
    }

    private static async Task<T> ReadJsonAsync<T>(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        return (await JsonSerializer.DeserializeAsync<T>(stream, MaxioJson.Options, cancellationToken))!;
    }
}

internal static class MaxioErrorParser
{
    public static IReadOnlyList<string> Parse(string body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return Array.Empty<string>();
        }

        try
        {
            using var document = JsonDocument.Parse(body);
            var root = document.RootElement;
            var messages = new List<string>();

            void Collect(JsonElement element)
            {
                switch (element.ValueKind)
                {
                    case JsonValueKind.String:
                        if (!string.IsNullOrWhiteSpace(element.GetString()))
                        {
                            messages.Add(element.GetString()!);
                        }

                        break;
                    case JsonValueKind.Array:
                        foreach (var child in element.EnumerateArray())
                        {
                            Collect(child);
                        }

                        break;
                    case JsonValueKind.Object:
                        foreach (var property in element.EnumerateObject())
                        {
                            Collect(property.Value);
                        }

                        break;
                }
            }

            Collect(root);
            return messages;
        }
        catch (JsonException)
        {
            return new List<string> { body.Trim() };
        }
    }
}
