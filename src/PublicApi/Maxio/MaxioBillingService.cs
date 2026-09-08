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
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

/// <summary>
/// HTTP client for Maxio Advanced Billing. Every request is built against the Maxio
/// OpenAPI specification (maxio-spec/openapi.yaml): HTTP Basic auth with the API key as
/// the username, JSON bodies, snake_case payloads, .json suffixed paths and the
/// { site }.{ region }.chargify.com base URL templating from x-server-configuration.
/// </summary>
public sealed class MaxioBillingService : IMaxioBillingService
{
    public const string HttpClientName = "Maxio";

    private const string PaymentCollectionMethodRemittance = "remittance";
    private const string CustomerReferencePrefix = "eshop-user:";

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IOptions<MaxioOptions> _options;

    public MaxioBillingService(IHttpClientFactory httpClientFactory, IOptions<MaxioOptions> options)
    {
        _httpClientFactory = httpClientFactory;
        _options = options;
    }

    public async Task<IReadOnlyList<MaxioProduct>> ListPlansAsync(CancellationToken cancellationToken = default)
    {
        var options = GetConfiguredOptions(requireProductFamily: true);

        var familyHandle = options.ProductFamilyHandle!;
        // Only handles made of URL-safe characters can be interpolated into the path.
        EnsureSafeHandle(familyHandle, nameof(options.ProductFamilyHandle));

        // GET /product_families/{product_family_id}/products.json where the path segment
        // accepts the family handle with a "handle:" prefix.
        string url = $"product_families/handle:{familyHandle}/products.json";
        var products = await GetAsync<List<MaxioProductResponse>>(url, cancellationToken);

        return products
            .Where(p => p.Product.ArchivedAt is null)
            .Where(p => !p.Product.RequireCreditCard) // subscription flow captures no card
            .Select(p => p.Product)
            .ToList();
    }

    public async Task<IReadOnlyList<MaxioSubscription>> ListSubscriptionsAsync(string identity, CancellationToken cancellationToken = default)
    {
        GetConfiguredOptions(requireProductFamily: false);

        var customer = await FindCustomerByReferenceAsync(ToCustomerReference(identity), cancellationToken);
        if (customer is null)
        {
            return Array.Empty<MaxioSubscription>();
        }

        // GET /customers/{customer_id}/subscriptions.json (tag: Customers)
        string url = $"customers/{customer.Id}/subscriptions.json";
        var subscriptions = await GetAsync<List<MaxioSubscriptionResponse>>(url, cancellationToken);
        return subscriptions.Select(s => s.Subscription).ToList();
    }

    public async Task<MaxioCustomer> GetOrCreateCustomerAsync(string identity, CancellationToken cancellationToken = default)
    {
        GetConfiguredOptions(requireProductFamily: false);

        string reference = ToCustomerReference(identity);
        var existing = await FindCustomerByReferenceAsync(reference, cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        var (firstName, lastName) = DeriveCustomerName(identity);
        var request = new CreateMaxioCustomerRequest
        {
            Customer = new MaxioCustomerInput
            {
                FirstName = firstName,
                LastName = lastName,
                Email = identity,
                Reference = reference
            }
        };

        try
        {
            // POST /customers.json
            var response = await PostAsync<MaxioCustomerResponse>("customers.json", request, cancellationToken);
            return response.Customer;
        }
        catch (MaxioApiException ex) when (IsDuplicateReferenceError(ex))
        {
            // Lost a concurrent create race; the reference is unique so someone else
            // created this customer first. Return it instead of failing.
            var winner = await FindCustomerByReferenceAsync(reference, cancellationToken);
            if (winner is not null)
            {
                return winner;
            }

            throw;
        }
    }

    public async Task<MaxioSubscribeResult> SubscribeAsync(string identity, string productHandle, CancellationToken cancellationToken = default)
    {
        GetConfiguredOptions(requireProductFamily: false);

        if (string.IsNullOrWhiteSpace(productHandle))
        {
            throw new MaxioValidationException("A product handle is required to subscribe.");
        }

        // Only plans from the configured catalog can be subscribed to through this API.
        var plans = await ListPlansAsync(cancellationToken);
        if (plans.All(p => !string.Equals(p.Handle, productHandle, StringComparison.OrdinalIgnoreCase)))
        {
            throw new MaxioValidationException(
                $"The plan '{productHandle}' is not available for subscription through this site.");
        }

        var customer = await GetOrCreateCustomerAsync(identity, cancellationToken);

        // Replay guard: the user may already have a subscription to this plan (created by
        // this API or an earlier integration). Return it instead of creating a duplicate.
        var existingSubscriptions = await ListCustomerSubscriptionsAsync(customer.Id, cancellationToken);
        var existing = existingSubscriptions.FirstOrDefault(s =>
            string.Equals(s.Product?.Handle, productHandle, StringComparison.OrdinalIgnoreCase));
        if (existing is not null)
        {
            return new MaxioSubscribeResult { Subscription = existing, WasCreated = false };
        }

        string subscriptionReference = ToSubscriptionReference(identity, productHandle);
        var createRequest = new CreateMaxioSubscriptionRequest
        {
            Subscription = new CreateMaxioSubscriptionInput
            {
                ProductHandle = productHandle,
                CustomerReference = customer.Reference,
                PaymentCollectionMethod = PaymentCollectionMethodRemittance,
                Reference = subscriptionReference
            }
        };

        try
        {
            // POST /subscriptions.json — the reference is a site-unique idempotency key
            // (Maxio rejects a second subscription with the same reference via 422).
            var created = await PostAsync<MaxioSubscriptionResponse>("subscriptions.json", createRequest, cancellationToken);
            return new MaxioSubscribeResult { Subscription = created.Subscription, WasCreated = true };
        }
        catch (MaxioApiException ex) when (IsDuplicateReferenceError(ex))
        {
            // Two concurrent subscribe requests raced; the winner already created the
            // subscription so find and return it.
            var winner = await FindSubscriptionByReferenceAsync(subscriptionReference, cancellationToken);
            if (winner is not null)
            {
                return new MaxioSubscribeResult { Subscription = winner, WasCreated = false };
            }

            throw;
        }
    }

    private async Task<IReadOnlyList<MaxioSubscription>> ListCustomerSubscriptionsAsync(int customerId, CancellationToken cancellationToken)
    {
        string url = $"customers/{customerId}/subscriptions.json";
        var subscriptions = await GetAsync<List<MaxioSubscriptionResponse>>(url, cancellationToken);
        return subscriptions.Select(s => s.Subscription).ToList();
    }

    private async Task<MaxioCustomer?> FindCustomerByReferenceAsync(string reference, CancellationToken cancellationToken)
    {
        // GET /customers/lookup.json?reference=... — exact single match by reference.
        string url = $"customers/lookup.json?reference={Uri.EscapeDataString(reference)}";
        var response = await GetOrDefaultAsync<MaxioCustomerResponse>(url, cancellationToken);
        return response?.Customer;
    }

    private async Task<MaxioSubscription?> FindSubscriptionByReferenceAsync(string reference, CancellationToken cancellationToken)
    {
        // GET /subscriptions/lookup.json?reference=... — find a subscription by reference.
        string url = $"subscriptions/lookup.json?reference={Uri.EscapeDataString(reference)}";
        var response = await GetOrDefaultAsync<MaxioSubscriptionResponse>(url, cancellationToken);
        return response?.Subscription;
    }

    private MaxioOptions GetConfiguredOptions(bool requireProductFamily)
    {
        var options = _options.Value;
        string? baseUrl = MaxioBaseUrlResolver.TryResolve(options);

        if (string.IsNullOrWhiteSpace(options.ApiKey))
        {
            throw new MaxioConfigurationException(
                $"The Maxio integration is not configured. Set {MaxioConfigurationExtensions.ApiKeyEnvironmentVariable}.");
        }

        if (baseUrl is null)
        {
            throw new MaxioConfigurationException(
                $"The Maxio integration is not configured. Set {MaxioConfigurationExtensions.SubdomainEnvironmentVariable} " +
                $"or provide a {MaxioOptions.SectionName}:{nameof(MaxioOptions.BaseUrl)} value.");
        }

        if (requireProductFamily && string.IsNullOrWhiteSpace(options.ProductFamilyHandle))
        {
            throw new MaxioConfigurationException(
                $"The Maxio product family handle is not configured. Set {MaxioConfigurationExtensions.ProductFamilyEnvironmentVariable}.");
        }

        return options;
    }

    private async Task<T> GetAsync<T>(string relativeUrl, CancellationToken cancellationToken)
    {
        using var client = CreateHttpClient();
        using var response = await client.GetAsync(relativeUrl, cancellationToken).ConfigureAwait(false);
        string body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            throw CreateApiException(response.StatusCode, body);
        }

        return Deserialize<T>(body);
    }

    private async Task<T?> GetOrDefaultAsync<T>(string relativeUrl, CancellationToken cancellationToken)
        where T : class
    {
        using var client = CreateHttpClient();
        using var response = await client.GetAsync(relativeUrl, cancellationToken).ConfigureAwait(false);

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        string body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            throw CreateApiException(response.StatusCode, body);
        }

        return Deserialize<T>(body);
    }

    private async Task<T> PostAsync<T>(string relativeUrl, object body, CancellationToken cancellationToken)
    {
        using var client = CreateHttpClient();
        using var content = JsonContent.Create(body, options: MaxioJson.Options);
        using var response = await client.PostAsync(relativeUrl, content, cancellationToken).ConfigureAwait(false);
        string responseBody = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            throw CreateApiException(response.StatusCode, responseBody);
        }

        return Deserialize<T>(responseBody);
    }

    private HttpClient CreateHttpClient()
    {
        var options = GetConfiguredOptions(requireProductFamily: false);
        var baseUrl = MaxioBaseUrlResolver.TryResolve(options)!;

        var client = _httpClientFactory.CreateClient(HttpClientName);
        client.BaseAddress = new Uri(baseUrl.TrimEnd('/') + "/", UriKind.Absolute);

        // Maxio security scheme: HTTP Basic, username = API key, password = "x".
        string credentials = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{options.ApiKey}:x"));
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", credentials);
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        client.DefaultRequestHeaders.UserAgent.ParseAdd("eShopOnWeb");

        return client;
    }

    private static T Deserialize<T>(string json) =>
        JsonSerializer.Deserialize<T>(json, MaxioJson.Options)
        ?? throw new MaxioApiException(0, "Maxio returned an empty response body.");

    private static MaxioApiException CreateApiException(HttpStatusCode statusCode, string body)
    {
        var errors = ParseErrorMessages(body);
        string message = errors.Count > 0
            ? string.Join(" ", errors)
            : $"Maxio API request failed with status {(int)statusCode}.";

        return new MaxioApiException((int)statusCode, message, errors);
    }

    private static IReadOnlyList<string> ParseErrorMessages(string body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return Array.Empty<string>();
        }

        try
        {
            using var document = JsonDocument.Parse(body);
            if (document.RootElement.ValueKind == JsonValueKind.Object &&
                document.RootElement.TryGetProperty("errors", out var errors))
            {
                return errors.ValueKind switch
                {
                    JsonValueKind.Array => errors
                        .EnumerateArray()
                        .Where(e => e.ValueKind == JsonValueKind.String)
                        .Select(e => e.GetString()!)
                        .Where(m => !string.IsNullOrWhiteSpace(m))
                        .ToList(),
                    JsonValueKind.Object => errors
                        .EnumerateObject()
                        .SelectMany(p => p.Value.ValueKind == JsonValueKind.Array
                            ? p.Value.EnumerateArray().Select(e => e.GetString() ?? string.Empty)
                            : new[] { p.Value.GetString() ?? string.Empty })
                        .Where(m => !string.IsNullOrWhiteSpace(m))
                        .ToList(),
                    JsonValueKind.String => new[] { errors.GetString() ?? string.Empty },
                    _ => Array.Empty<string>()
                };
            }

            if (document.RootElement.ValueKind == JsonValueKind.Object &&
                document.RootElement.TryGetProperty("error", out var error) &&
                error.ValueKind == JsonValueKind.String)
            {
                return new[] { error.GetString() ?? string.Empty };
            }
        }
        catch (JsonException)
        {
            // Not JSON (some 4xx bodies are plain strings) - fall through to raw text.
        }

        string text = body.Trim();
        int maxLength = Math.Min(text.Length, 500);
        return new[] { text.Substring(0, maxLength) };
    }

    private static bool IsDuplicateReferenceError(MaxioApiException exception) =>
        exception.StatusCode == (int)HttpStatusCode.UnprocessableEntity &&
        exception.Errors.Any(e =>
            e.Contains("Reference", StringComparison.OrdinalIgnoreCase) &&
            (e.Contains("unique", StringComparison.OrdinalIgnoreCase) ||
             e.Contains("taken", StringComparison.OrdinalIgnoreCase)));

    private static string ToCustomerReference(string identity) => CustomerReferencePrefix + identity;

    private static string ToSubscriptionReference(string identity, string productHandle) =>
        $"eshop-sub:{identity}:{productHandle}";

    private static void EnsureSafeHandle(string handle, string settingName)
    {
        if (!System.Text.RegularExpressions.Regex.IsMatch(handle, "^[A-Za-z0-9_][A-Za-z0-9._-]*$"))
        {
            throw new MaxioConfigurationException(
                $"The Maxio {settingName} value '{handle}' contains characters that are not valid for an API handle.");
        }
    }

    private static (string FirstName, string LastName) DeriveCustomerName(string identity)
    {
        // The eShopOnWeb identity model stores the username (email) only, so the Maxio
        // customer display name is derived deterministically from the email address:
        // first name = local part, last name = registrable domain part.
        int atIndex = identity.IndexOf('@');
        if (atIndex <= 0)
        {
            return (identity, "User");
        }

        string localPart = identity.Substring(0, atIndex);
        string domain = identity.Substring(atIndex + 1);

        if (string.IsNullOrWhiteSpace(localPart))
        {
            return (identity, "User");
        }

        int lastDot = domain.LastIndexOf('.');
        string lastName = lastDot > 0 ? domain.Substring(0, lastDot) : domain;
        if (string.IsNullOrWhiteSpace(lastName))
        {
            lastName = "User";
        }

        return (localPart, lastName);
    }
}
