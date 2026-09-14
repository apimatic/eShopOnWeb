using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.Infrastructure.Billing.Maxio.Contracts;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.Infrastructure.Billing.Maxio;

/// <summary>
/// Typed <see cref="HttpClient"/> over the Maxio Billing API. Authentication is HTTP Basic over TLS
/// with the API key as the user name and "X" as the password, per the Billing API authentication
/// guide. Provider failures are translated into the billing exceptions the API layer maps to HTTP
/// status codes.
/// </summary>
public class MaxioApiClient : IMaxioApiClient
{
    /// <summary>Maxio caps <c>per_page</c> at 200.</summary>
    private const int PageSize = 200;

    /// <summary>Safety valve so a misbehaving upstream cannot make us page forever.</summary>
    private const int MaxPages = 25;

    private readonly HttpClient _httpClient;
    private readonly ILogger<MaxioApiClient> _logger;

    public MaxioApiClient(HttpClient httpClient, ILogger<MaxioApiClient> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public async Task<IReadOnlyList<MaxioProduct>> ListProductsForFamilyAsync(string productFamilyHandle,
        CancellationToken cancellationToken = default)
    {
        // The product family is addressed by handle (`handle:` prefix) rather than id: handles are
        // stable, numeric ids are reassigned whenever the catalog is re-seeded.
        var family = Uri.EscapeDataString($"handle:{productFamilyHandle}");
        var products = new List<MaxioProduct>();

        for (var page = 1; page <= MaxPages; page++)
        {
            var path = $"product_families/{family}/products.json?per_page={PageSize}&page={page}";
            var envelopes = await SendAsync<List<ProductEnvelope>>(HttpMethod.Get, path, body: null,
                allowNotFound: true, cancellationToken: cancellationToken);

            if (envelopes is null)
            {
                throw new BillingProviderException(
                    $"Maxio product family '{productFamilyHandle}' was not found on this site. Check Maxio:ProductFamilyHandle.",
                    (int)HttpStatusCode.NotFound);
            }

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

        _logger.LogWarning("Stopped paging Maxio products for family {Family} after {MaxPages} pages.",
            productFamilyHandle, MaxPages);
        return products;
    }

    public async Task<MaxioCustomer?> FindCustomerByReferenceAsync(string reference,
        CancellationToken cancellationToken = default)
    {
        var path = $"customers/lookup.json?reference={Uri.EscapeDataString(reference)}";
        var envelope = await SendAsync<CustomerEnvelope>(HttpMethod.Get, path, body: null,
            allowNotFound: true, cancellationToken: cancellationToken);

        return envelope?.Customer;
    }

    public async Task<MaxioCustomer> CreateCustomerAsync(CreateCustomerRequest request,
        CancellationToken cancellationToken = default)
    {
        var envelope = await SendAsync<CustomerEnvelope>(HttpMethod.Post, "customers.json", request,
            allowNotFound: false, cancellationToken: cancellationToken,
            safeToRetry: !string.IsNullOrEmpty(request.UniquenessToken));

        return envelope?.Customer
               ?? throw new BillingProviderException("Maxio accepted the customer but returned no customer payload.");
    }

    public async Task<IReadOnlyList<MaxioSubscription>> ListCustomerSubscriptionsAsync(long customerId,
        CancellationToken cancellationToken = default)
    {
        var path = $"customers/{customerId.ToString(CultureInfo.InvariantCulture)}/subscriptions.json";
        var envelopes = await SendAsync<List<SubscriptionEnvelope>>(HttpMethod.Get, path, body: null,
            allowNotFound: true, cancellationToken: cancellationToken);

        if (envelopes is null)
        {
            return Array.Empty<MaxioSubscription>();
        }

        var subscriptions = new List<MaxioSubscription>(envelopes.Count);
        foreach (var envelope in envelopes)
        {
            if (envelope.Subscription is not null)
            {
                subscriptions.Add(envelope.Subscription);
            }
        }

        return subscriptions;
    }

    public async Task<MaxioSubscription> CreateSubscriptionAsync(CreateSubscriptionRequest request,
        CancellationToken cancellationToken = default)
    {
        var envelope = await SendAsync<SubscriptionEnvelope>(HttpMethod.Post, "subscriptions.json", request,
            allowNotFound: false, cancellationToken: cancellationToken,
            safeToRetry: !string.IsNullOrEmpty(request.UniquenessToken));

        return envelope?.Subscription
               ?? throw new BillingProviderException("Maxio accepted the subscription but returned no subscription payload.");
    }

    public async Task<MaxioSite?> ReadSiteAsync(CancellationToken cancellationToken = default)
    {
        var envelope = await SendAsync<SiteEnvelope>(HttpMethod.Get, "site.json", body: null,
            allowNotFound: true, cancellationToken: cancellationToken);

        return envelope?.Site;
    }

    /// <summary>
    /// Issues one Maxio call and turns the outcome into either a deserialized payload or a billing
    /// exception. <paramref name="allowNotFound"/> makes a 404 a null result rather than a failure,
    /// which is how Maxio signals "no such customer/resource" on the lookup endpoints.
    /// </summary>
    private async Task<TResponse?> SendAsync<TResponse>(HttpMethod method, string path, object? body,
        bool allowNotFound, CancellationToken cancellationToken, bool safeToRetry = false)
        where TResponse : class
    {
        using var request = new HttpRequestMessage(method, path);
        request.Options.Set(MaxioResilienceHandler.SafeToRetryOption, method == HttpMethod.Get || safeToRetry);

        if (body is not null)
        {
            request.Content = JsonContent.Create(body, body.GetType(), options: MaxioJson.Options);
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
        catch (TaskCanceledException ex)
        {
            throw new BillingProviderException(
                $"The Maxio request {method} {path} timed out after {_httpClient.Timeout.TotalSeconds:0}s.", ex);
        }
        catch (HttpRequestException ex)
        {
            throw new BillingProviderException($"Could not reach Maxio at {_httpClient.BaseAddress}: {ex.Message}", ex);
        }

        using (response)
        {
            if (response.StatusCode == HttpStatusCode.NotFound && allowNotFound)
            {
                return null;
            }

            if (!response.IsSuccessStatusCode)
            {
                throw await BuildFailureAsync(method, path, response, cancellationToken);
            }

            if (response.StatusCode == HttpStatusCode.NoContent)
            {
                return null;
            }

            try
            {
                return await response.Content.ReadFromJsonAsync<TResponse>(MaxioJson.Options, cancellationToken);
            }
            catch (JsonException ex)
            {
                throw new BillingProviderException(
                    $"Maxio returned a response for {method} {path} that could not be read: {ex.Message}", ex,
                    (int)response.StatusCode);
            }
        }
    }

    private static async Task<BillingException> BuildFailureAsync(HttpMethod method, string path,
        HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var status = (int)response.StatusCode;
        var errors = await ReadErrorsAsync(response, cancellationToken);
        var detail = errors.Count > 0 ? string.Join("; ", errors) : response.ReasonPhrase ?? "no detail";

        return response.StatusCode switch
        {
            HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => new BillingProviderException(
                $"Maxio rejected our credentials ({status}) for {method} {path}. Check Maxio:ApiKey and Maxio:Subdomain.",
                status),

            HttpStatusCode.Conflict => new BillingConflictException(
                $"Maxio reported a duplicate submission for {method} {path}: {detail}"),

            HttpStatusCode.UnprocessableEntity or HttpStatusCode.BadRequest => new BillingValidationException(
                $"Maxio rejected {method} {path}: {detail}", errors),

            HttpStatusCode.TooManyRequests => new BillingProviderException(
                $"Maxio is throttling this site; {method} {path} was refused after retries: {detail}", status),

            _ => new BillingProviderException($"Maxio returned {status} for {method} {path}: {detail}", status)
        };
    }

    /// <summary>
    /// Flattens the two documented error shapes - <c>{"errors":["..."]}</c> and
    /// <c>{"errors":{"field":"..."}}</c> - into a list of messages, falling back to the raw body.
    /// </summary>
    private static async Task<IReadOnlyList<string>> ReadErrorsAsync(HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        string payload;
        try
        {
            payload = await response.Content.ReadAsStringAsync(cancellationToken);
        }
        catch (Exception)
        {
            return Array.Empty<string>();
        }

        if (string.IsNullOrWhiteSpace(payload))
        {
            return Array.Empty<string>();
        }

        try
        {
            using var document = JsonDocument.Parse(payload);
            if (document.RootElement.ValueKind == JsonValueKind.Object
                && document.RootElement.TryGetProperty("errors", out var errors))
            {
                var messages = new List<string>();
                switch (errors.ValueKind)
                {
                    case JsonValueKind.Array:
                        foreach (var item in errors.EnumerateArray())
                        {
                            var value = item.ValueKind == JsonValueKind.String ? item.GetString() : item.ToString();
                            if (!string.IsNullOrWhiteSpace(value))
                            {
                                messages.Add(value!);
                            }
                        }

                        break;

                    case JsonValueKind.Object:
                        foreach (var property in errors.EnumerateObject())
                        {
                            var value = property.Value.ValueKind == JsonValueKind.String
                                ? property.Value.GetString()
                                : property.Value.ToString();
                            messages.Add($"{property.Name}: {value}");
                        }

                        break;

                    case JsonValueKind.String:
                        var single = errors.GetString();
                        if (!string.IsNullOrWhiteSpace(single))
                        {
                            messages.Add(single!);
                        }

                        break;
                }

                if (messages.Count > 0)
                {
                    return messages;
                }
            }
        }
        catch (JsonException)
        {
            // Not JSON - fall through to the truncated raw body below.
        }

        return new[] { Truncate(payload, 500) };
    }

    private static string Truncate(string value, int max)
    {
        var collapsed = new StringBuilder(value.Length);
        foreach (var character in value)
        {
            collapsed.Append(char.IsControl(character) ? ' ' : character);
        }

        var text = collapsed.ToString().Trim();
        return text.Length <= max ? text : text[..max] + "...";
    }
}
