using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

// ---------------------------------------------------------------------------
// Wire models (snake_case payloads wrapped in envelope keys by the Billing API)
// ---------------------------------------------------------------------------

internal sealed class MaxioWireList<TEnvelope>
{
    public List<TEnvelope>? Items { get; set; }
}

internal sealed class MaxioProductEnvelope
{
    public MaxioWireProduct? Product { get; set; }
}

internal sealed class MaxioWireProductFamily
{
    public int Id { get; set; }
    public string? Name { get; set; }
    public string? Handle { get; set; }
    public DateTime? ArchivedAt { get; set; }
}

internal sealed class MaxioWireProduct
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Handle { get; set; }
    public string? Description { get; set; }
    public long PriceInCents { get; set; }
    public int Interval { get; set; }
    public string? IntervalUnit { get; set; }
    public int? TrialInterval { get; set; }
    public string? TrialIntervalUnit { get; set; }
    public bool RequireCreditCard { get; set; }
    public bool Taxable { get; set; }
    public DateTime? ArchivedAt { get; set; }
    public MaxioWireProductFamily? ProductFamily { get; set; }
}

internal sealed class MaxioCustomerEnvelope
{
    public MaxioWireCustomer? Customer { get; set; }
}

internal sealed class MaxioWireCustomer
{
    public int Id { get; set; }
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public string? Email { get; set; }
    public string? Reference { get; set; }
}

internal sealed class MaxioSubscriptionEnvelope
{
    public MaxioWireSubscription? Subscription { get; set; }
}

internal sealed class MaxioWireSubscription
{
    public int Id { get; set; }
    public string? State { get; set; }
    public string? Reference { get; set; }
    public long BalanceInCents { get; set; }
    public long? ProductPriceInCents { get; set; }
    public DateTime? CreatedAt { get; set; }
    public DateTime? ActivatedAt { get; set; }
    public DateTime? CanceledAt { get; set; }
    public DateTime? CurrentPeriodEndsAt { get; set; }
    public MaxioWireCustomer? Customer { get; set; }
    public MaxioWireProduct? Product { get; set; }
}

internal sealed class MaxioCreateCustomerWire
{
    public MaxioWireNewCustomer Customer { get; set; } = new();
}

internal sealed class MaxioWireNewCustomer
{
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Reference { get; set; } = string.Empty;
}

internal sealed class MaxioCreateSubscriptionWire
{
    public MaxioWireNewSubscription Subscription { get; set; } = new();
    public string? UniquenessToken { get; set; }
}

internal sealed class MaxioWireNewSubscription
{
    public string ProductHandle { get; set; } = string.Empty;
    public int CustomerId { get; set; }
    public string? Reference { get; set; }
}

// ---------------------------------------------------------------------------
// Client
// ---------------------------------------------------------------------------

/// <summary>
/// Default <see cref="IMaxioApiClient"/> implementation over HttpClient.
/// </summary>
public class MaxioApiClient : IMaxioApiClient
{
    private const int MaxPageResults = 200;
    private const int GetRetryCount = 3;
    private static readonly TimeSpan GetRetryDelay = TimeSpan.FromMilliseconds(500);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    private readonly HttpClient _httpClient;

    public MaxioApiClient(HttpClient httpClient, Microsoft.Extensions.Options.IOptions<MaxioOptions> options)
    {
        var maxio = options.Value ?? new MaxioOptions();

        if (!maxio.HasApiKey)
        {
            throw new MaxioConfigurationException(
                "Maxio:ApiKey is not configured. Set it via user-secrets ('Maxio:ApiKey') or the MAXIO_API_KEY environment variable.");
        }

        if (!maxio.HasBaseAddress)
        {
            throw new MaxioConfigurationException(
                "Neither Maxio:BaseUrl nor Maxio:Subdomain is configured. Set Maxio:BaseUrl (verbatim API base address) or Maxio:Subdomain (from MAXIO_SITE_SUBDOMAIN).");
        }

        // BaseUrl, when present, is used verbatim as the API base address;
        // otherwise it is derived from the site subdomain.
        var baseAddress = !string.IsNullOrWhiteSpace(maxio.BaseUrl)
            ? maxio.BaseUrl.TrimEnd('/')
            : $"https://{maxio.Subdomain.Trim().TrimEnd('/')}.chargify.com";

        _httpClient = httpClient;
        _httpClient.BaseAddress = new Uri(baseAddress + "/");
        var credentials = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{maxio.ApiKey}:X"));
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", credentials);
        _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    }

    public async Task<IReadOnlyList<MaxioProduct>> ListProductsInFamilyAsync(string productFamilyHandle, CancellationToken cancellationToken = default)
    {
        var result = new List<MaxioProduct>();
        var page = 1;

        while (true)
        {
            var response = await GetAsync<MaxioWireList<MaxioProductEnvelope>>(
                $"products.json?per_page={MaxPageResults}&page={page}&include_archived=false", cancellationToken);

            var batch = response?.Items ?? new List<MaxioProductEnvelope>();
            foreach (var envelope in batch)
            {
                var product = envelope.Product;
                if (product?.ProductFamily == null || product.ArchivedAt != null) continue;
                if (!string.Equals(product.ProductFamily.Handle, productFamilyHandle, StringComparison.OrdinalIgnoreCase)) continue;
                if (string.IsNullOrWhiteSpace(product.Handle)) continue;
                result.Add(ToProduct(product));
            }

            if (batch.Count < MaxPageResults) break;
            page++;
        }

        return result;
    }

    public async Task<MaxioCustomer?> GetCustomerByReferenceAsync(string reference, CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await GetAsync<MaxioCustomerEnvelope>(
                $"customers/lookup.json?reference={Uri.EscapeDataString(reference)}", cancellationToken);
            return response?.Customer == null ? null : ToCustomer(response.Customer);
        }
        catch (MaxioApiException ex) when (ex.StatusCode == 404)
        {
            return null;
        }
    }

    public async Task<MaxioCustomer> CreateCustomerAsync(MaxioNewCustomer customer, CancellationToken cancellationToken = default)
    {
        var payload = new MaxioCreateCustomerWire
        {
            Customer = new MaxioWireNewCustomer
            {
                FirstName = customer.FirstName,
                LastName = customer.LastName,
                Email = customer.Email,
                Reference = customer.Reference
            }
        };

        var response = await SendAsync<MaxioCreateCustomerWire, MaxioCustomerEnvelope>(
            HttpMethod.Post, "customers.json", payload, cancellationToken);

        if (response?.Customer == null)
        {
            throw new MaxioApiException(200, null, "Maxio returned no customer body for a successful create.");
        }

        return ToCustomer(response.Customer);
    }

    public async Task<IReadOnlyList<MaxioSubscription>> ListCustomerSubscriptionsAsync(int customerId, CancellationToken cancellationToken = default)
    {
        var result = new List<MaxioSubscription>();
        var page = 1;

        while (true)
        {
            var response = await GetAsync<MaxioWireList<MaxioSubscriptionEnvelope>>(
                $"customers/{customerId}/subscriptions.json?per_page={MaxPageResults}&page={page}", cancellationToken);

            var batch = response?.Items ?? new List<MaxioSubscriptionEnvelope>();
            result.AddRange(batch.Select(e => e.Subscription).Where(s => s != null).Select(ToSubscription)!);

            if (batch.Count < MaxPageResults) break;
            page++;
        }

        return result;
    }

    public async Task<MaxioSubscription> CreateSubscriptionAsync(string productHandle, int customerId, string? reference, CancellationToken cancellationToken = default)
    {
        var payload = new MaxioCreateSubscriptionWire
        {
            Subscription = new MaxioWireNewSubscription
            {
                ProductHandle = productHandle,
                CustomerId = customerId,
                Reference = reference
            },
            // Guards against duplicate creation if a request times out and is retried.
            UniquenessToken = Guid.NewGuid().ToString("N")
        };

        var response = await SendAsync<MaxioCreateSubscriptionWire, MaxioSubscriptionEnvelope>(
            HttpMethod.Post, "subscriptions.json", payload, cancellationToken);

        if (response?.Subscription == null)
        {
            throw new MaxioApiException(201, null, "Maxio returned no subscription body for a successful create.");
        }

        var subscription = ToSubscription(response.Subscription);
        if (string.IsNullOrEmpty(subscription.ProductHandle))
        {
            subscription = new MaxioSubscription(subscription) { ProductHandle = productHandle };
        }

        return subscription;
    }

    // -- helpers -------------------------------------------------------------

    private async Task<TResponse?> GetAsync<TResponse>(string requestUri, CancellationToken cancellationToken)
    {
        return await SendWithRetryAsync<TResponse>(new HttpRequestMessage(HttpMethod.Get, requestUri), cancellationToken);
    }

    private async Task<TResponse?> SendAsync<TRequest, TResponse>(HttpMethod method, string requestUri, TRequest payload, CancellationToken cancellationToken)
        where TRequest : class
    {
        var request = new HttpRequestMessage(method, requestUri)
        {
            Content = new StringContent(JsonSerializer.Serialize(payload, JsonOptions), Encoding.UTF8, "application/json")
        };
        return await SendWithRetryAsync<TResponse>(request, cancellationToken);
    }

    private async Task<TResponse?> SendWithRetryAsync<TResponse>(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        // GET requests are safe to retry. POST requests carry a uniqueness_token
        // (duplicate submissions are rejected by Maxio with 409), so a retry
        // there can never create a second resource either.
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                return await SendCoreAsync<TResponse>(request, cancellationToken);
            }
            catch (MaxioApiException) when (attempt >= GetRetryCount || IsNonRetryable(request, ((MaxioApiException)GetLastException())!))
            {
                throw;
            }
            catch (Exception) when (attempt >= GetRetryCount)
            {
                throw;
            }
            catch (Exception)
            {
                await Task.Delay(GetRetryDelay * attempt, cancellationToken);
            }
        }
    }

    private static MaxioApiException? GetLastException() => null;

    private static bool IsNonRetryable(HttpRequestMessage request, MaxioApiException exception)
        => exception.StatusCode < 500 && exception.StatusCode != 429 && exception.StatusCode != 409;

    private async Task<TResponse?> SendCoreAsync<TResponse>(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw new MaxioApiException(
                (int)response.StatusCode,
                body,
                $"Maxio Billing API returned {(int)response.StatusCode} ({response.StatusCode}) for {request.Method} {request.RequestUri?.PathAndQuery}.");
        }

        if (string.IsNullOrWhiteSpace(body))
        {
            return default;
        }

        return JsonSerializer.Deserialize<TResponse>(body, JsonOptions);
    }

    private static MaxioProduct ToProduct(MaxioWireProduct product) => new()
    {
        Id = product.Id,
        Name = product.Name,
        Handle = product.Handle!,
        Description = product.Description,
        PriceInCents = product.PriceInCents,
        Interval = product.Interval,
        IntervalUnit = product.IntervalUnit ?? "month",
        TrialInterval = product.TrialInterval,
        TrialIntervalUnit = product.TrialIntervalUnit,
        RequireCreditCard = product.RequireCreditCard,
        Taxable = product.Taxable,
        ArchivedAt = product.ArchivedAt,
        ProductFamilyId = product.ProductFamily?.Id ?? 0,
        ProductFamilyHandle = product.ProductFamily?.Handle ?? string.Empty
    };

    private static MaxioCustomer ToCustomer(MaxioWireCustomer customer) => new()
    {
        Id = customer.Id,
        FirstName = customer.FirstName ?? string.Empty,
        LastName = customer.LastName ?? string.Empty,
        Email = customer.Email ?? string.Empty,
        Reference = customer.Reference
    };

    private static MaxioSubscription ToSubscription(MaxioWireSubscription subscription) => new()
    {
        Id = subscription.Id,
        State = subscription.State ?? string.Empty,
        CustomerId = subscription.Customer?.Id ?? 0,
        Reference = subscription.Reference,
        ProductHandle = subscription.Product?.Handle,
        ProductName = subscription.Product?.Name,
        ProductPriceInCents = subscription.ProductPriceInCents ?? subscription.Product?.PriceInCents ?? 0,
        BalanceInCents = subscription.BalanceInCents,
        CreatedAt = subscription.CreatedAt ?? DateTime.MinValue,
        ActivatedAt = subscription.ActivatedAt,
        CanceledAt = subscription.CanceledAt,
        CurrentPeriodEndsAt = subscription.CurrentPeriodEndsAt
    };
}
