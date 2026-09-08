using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// HTTP client for the Maxio Advanced Billing (formerly Chargify) API.
///
/// The contract implemented here was confirmed against the official Maxio Advanced Billing
/// developer documentation (developers.maxio.com) and verified live against the sandbox API:
///   - Authentication: HTTP Basic; user name = site API key, password = "x".
///   - Base URL: https://{subdomain}.chargify.com (US) / https://{subdomain}.ebilling.maxio.com (EU),
///     overridable verbatim via Maxio:BaseUrl.
///   - GET  /product_families.json, GET /product_families/{id}/products.json (paged).
///   - GET  /customers/lookup.json?reference=..., POST /customers.json.
///   - GET  /customers/{id}/subscriptions.json (paged), POST /subscriptions.json.
///   - Card-less subscription enrollment (plans seeded with no required payment method) is
///     created with payment_collection_method = "remittance".
/// </summary>
public class MaxioBillingService : IMaxioBillingService
{
    private const int ListPerPage = 200;
    private const int MaxListPages = 25;
    private const int MaxSendAttempts = 3;
    private const string CardlessCollectionMethod = "remittance";

    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly HttpClient _httpClient;
    private readonly MaxioOptions _options;
    private readonly ILogger<MaxioBillingService> _logger;
    private readonly string _baseUrl;

    // Serializes concurrent enrollments of the same subscription reference within this process,
    // so double-clicks cannot race past the idempotency check and create two subscriptions.
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _subscriptionLocks = new();

    public MaxioBillingService(HttpClient httpClient, IOptions<MaxioOptions> options, ILogger<MaxioBillingService> logger)
    {
        _options = options.Value;
        _httpClient = httpClient;
        _logger = logger;
        _baseUrl = ResolveBaseUrl(_options);

        if (string.IsNullOrWhiteSpace(_options.ApiKey))
        {
            throw new InvalidOperationException("Maxio:ApiKey is not configured (set it via user-secrets or the MAXIO_API_KEY environment variable).");
        }

        _httpClient.Timeout = TimeSpan.FromSeconds(60);
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Basic",
            Convert.ToBase64String(Encoding.ASCII.GetBytes($"{_options.ApiKey}:x")));
        _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    }

    public async Task<IReadOnlyList<MaxioPlan>> GetPlansAsync(CancellationToken cancellationToken = default)
    {
        var family = await FindProductFamilyAsync(_options.ProductFamilyHandle, cancellationToken);
        if (family == null)
        {
            throw new MaxioApiException(
                $"Maxio product family with handle '{_options.ProductFamilyHandle}' was not found. Check the Maxio:ProductFamilyHandle configuration.");
        }

        var products = await ListAllAsync<ApiProduct, ApiProductResponse>(
            $"product_families/{family.Id}/products.json", w => w.Product, cancellationToken);
        return products.Select(MapPlan).ToList();
    }

    public async Task<MaxioPlan?> GetPlanByHandleAsync(string planHandle, CancellationToken cancellationToken = default)
    {
        var plans = await GetPlansAsync(cancellationToken);
        return plans.FirstOrDefault(p =>
            string.Equals(p.Handle, planHandle, StringComparison.OrdinalIgnoreCase));
    }

    public async Task<MaxioSubscribeResult> SubscribeAsync(MaxioSubscribeRequest request, CancellationToken cancellationToken = default)
    {
        var subscriptionRef = request.SubscriptionReference;
        var gate = _subscriptionLocks.GetOrAdd(subscriptionRef, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            var customer = await EnsureCustomerAsync(request, cancellationToken);

            // Idempotency: reuse an existing live subscription for this (customer, plan) pair.
            var existing = await ListCustomerSubscriptionsAsync(customer.Id, cancellationToken);
            var live = existing.FirstOrDefault(s =>
                string.Equals(s.Reference, subscriptionRef, StringComparison.Ordinal) && IsLive(s.State));
            if (live != null)
            {
                return new MaxioSubscribeResult { Subscription = live, AlreadySubscribed = true, Customer = customer };
            }

            var body = new CreateSubscriptionBody(new ApiSubscriptionCreate(
                ProductHandle: request.PlanHandle,
                CustomerId: customer.Id,
                Reference: subscriptionRef,
                PaymentCollectionMethod: CardlessCollectionMethod));

            var created = await SendAsync<ApiSubscriptionResponse>(
                HttpMethod.Post, "subscriptions.json", body, HttpStatusCode.Created, cancellationToken);
            var subscription = MapSubscription(created.Subscription);

            _logger.LogInformation(
                "Created Maxio subscription {SubscriptionId} ({PlanHandle}) for customer reference {CustomerReference}.",
                subscription.Id, request.PlanHandle, request.CustomerReference);

            return new MaxioSubscribeResult { Subscription = subscription, AlreadySubscribed = false, Customer = customer };
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<IReadOnlyList<MaxioSubscription>> GetSubscriptionsForCustomerReferenceAsync(
        string customerReference, CancellationToken cancellationToken = default)
    {
        var customer = await TryGetCustomerByReferenceAsync(customerReference, cancellationToken);
        if (customer == null)
        {
            return Array.Empty<MaxioSubscription>();
        }

        return await ListCustomerSubscriptionsAsync(customer.Id, cancellationToken);
    }

    private async Task<MaxioCustomer> EnsureCustomerAsync(MaxioSubscribeRequest request, CancellationToken cancellationToken)
    {
        var existing = await TryGetCustomerByReferenceAsync(request.CustomerReference, cancellationToken);
        if (existing != null)
        {
            return existing;
        }

        var body = new CreateCustomerBody(new ApiCustomerCreate(
            FirstName: request.CustomerFirstName,
            LastName: request.CustomerLastName,
            Email: request.CustomerEmail,
            Reference: request.CustomerReference));

        try
        {
            var response = await SendAsync<ApiCustomerResponse>(
                HttpMethod.Post, "customers.json", body, HttpStatusCode.Created, cancellationToken);
            return MapCustomer(response.Customer);
        }
        catch (MaxioApiException ex) when (ex.StatusCode == (int)HttpStatusCode.UnprocessableEntity)
        {
            // Lost a race across processes: the customer was created between the lookup and this
            // call. Maxio enforces unique customer references, so fall back to the lookup.
            var raced = await TryGetCustomerByReferenceAsync(request.CustomerReference, cancellationToken);
            if (raced != null)
            {
                return raced;
            }

            throw;
        }
    }

    private async Task<MaxioCustomer?> TryGetCustomerByReferenceAsync(string reference, CancellationToken cancellationToken)
    {
        try
        {
            var response = await SendAsync<ApiCustomerResponse>(
                HttpMethod.Get, $"customers/lookup.json?reference={Uri.EscapeDataString(reference)}", null, HttpStatusCode.OK, cancellationToken);
            return MapCustomer(response.Customer);
        }
        catch (MaxioApiException ex) when (ex.StatusCode == (int)HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    private async Task<IReadOnlyList<MaxioSubscription>> ListCustomerSubscriptionsAsync(int customerId, CancellationToken cancellationToken)
    {
        var subscriptions = await ListAllAsync<ApiSubscription, ApiSubscriptionResponse>(
            $"customers/{customerId}/subscriptions.json", w => w.Subscription, cancellationToken);
        return subscriptions.Select(MapSubscription).ToList();
    }

    private async Task<ApiProductFamily?> FindProductFamilyAsync(string handle, CancellationToken cancellationToken)
    {
        var families = await ListAllAsync<ApiProductFamily, ApiProductFamilyResponse>(
            "product_families.json", w => w.ProductFamily, cancellationToken);
        return families.FirstOrDefault(f =>
            string.Equals(f.Handle, handle, StringComparison.OrdinalIgnoreCase));
    }

    private async Task<List<TItem>> ListAllAsync<TItem, TWrapper>(
        string path, Func<TWrapper, TItem?> unwrap, CancellationToken cancellationToken)
        where TWrapper : class
    {
        var results = new List<TItem>();
        for (var page = 1; page <= MaxListPages; page++)
        {
            var separator = path.Contains('?') ? '&' : '?';
            var batch = await SendAsync<List<TWrapper>>(
                HttpMethod.Get, $"{path}{separator}page={page}&per_page={ListPerPage}", null, HttpStatusCode.OK, cancellationToken);

            if (batch == null || batch.Count == 0)
            {
                break;
            }

            foreach (var wrapper in batch)
            {
                var item = unwrap(wrapper);
                if (item != null)
                {
                    results.Add(item);
                }
            }

            if (batch.Count < ListPerPage)
            {
                break;
            }
        }

        return results;
    }

    private async Task<TResponse> SendAsync<TResponse>(
        HttpMethod method, string path, object? body, HttpStatusCode expectedStatus, CancellationToken cancellationToken)
    {
        var response = await SendCoreAsync(method, path, body, cancellationToken);
        string raw;
        using (response)
        {
            raw = await response.Content.ReadAsStringAsync(cancellationToken);
        }

        if (response.StatusCode != expectedStatus)
        {
            _logger.LogWarning("Maxio call {Method} {Path} returned {StatusCode}: {Body}",
                method, path, (int)response.StatusCode, raw);
            throw new MaxioApiException(
                $"Maxio API call {method} {path} failed with HTTP {(int)response.StatusCode}.", (int)response.StatusCode, raw);
        }

        try
        {
            return JsonSerializer.Deserialize<TResponse>(raw, SerializerOptions)
                ?? throw new MaxioApiException(
                    $"Maxio API call {method} {path} returned an empty body.", (int)response.StatusCode, raw);
        }
        catch (JsonException ex)
        {
            throw new MaxioApiException(
                $"Maxio API call {method} {path} returned an unexpected body.", (int)response.StatusCode, raw, ex);
        }
    }

    private async Task<HttpResponseMessage> SendCoreAsync(HttpMethod method, string path, object? body, CancellationToken cancellationToken)
    {
        for (var attempt = 1; ; attempt++)
        {
            using var request = BuildRequest(method, path, body);
            var response = await _httpClient.SendAsync(request, cancellationToken);

            var isTransient = (int)response.StatusCode >= 500 || response.StatusCode == HttpStatusCode.TooManyRequests;
            if (!isTransient || attempt >= MaxSendAttempts)
            {
                return response;
            }

            var delay = TimeSpan.FromMilliseconds(250 * Math.Pow(2, attempt - 1));
            _logger.LogWarning(
                "Maxio call {Method} {Path} returned {StatusCode}; retrying in {DelayMs} ms (attempt {Attempt}/{MaxAttempts}).",
                method, path, (int)response.StatusCode, delay.TotalMilliseconds, attempt, MaxSendAttempts);
            response.Dispose();
            await Task.Delay(delay, cancellationToken);
        }
    }

    private HttpRequestMessage BuildRequest(HttpMethod method, string path, object? body)
    {
        var uri = new Uri($"{_baseUrl}/{path}");
        var request = new HttpRequestMessage(method, uri);
        if (body != null)
        {
            request.Content = JsonContent.Create(body, options: SerializerOptions);
        }

        return request;
    }

    private string ResolveBaseUrl(MaxioOptions options)
    {
        if (!string.IsNullOrWhiteSpace(options.BaseUrl))
        {
            return options.BaseUrl.TrimEnd('/');
        }

        if (string.IsNullOrWhiteSpace(options.Subdomain))
        {
            throw new InvalidOperationException(
                "Maxio:Subdomain is not configured (set it via user-secrets or the MAXIO_SITE_SUBDOMAIN environment variable), and no Maxio:BaseUrl override is present.");
        }

        var environment = options.Environment?.Trim().ToLowerInvariant();
        var host = environment switch
        {
            null or "" or "us" => $"{options.Subdomain}.chargify.com",
            "eu" => $"{options.Subdomain}.ebilling.maxio.com",
            _ => throw new InvalidOperationException($"Unsupported Maxio environment '{options.Environment}'. Expected 'US' or 'EU'.")
        };

        return $"https://{host}";
    }

    private static MaxioPlan MapPlan(ApiProduct product) => new()
    {
        ProductId = product.Id,
        Handle = product.Handle ?? string.Empty,
        Name = product.Name ?? string.Empty,
        Description = product.Description,
        PriceInCents = product.PriceInCents ?? 0,
        Interval = product.Interval ?? 1,
        IntervalUnit = product.IntervalUnit ?? "month"
    };

    private static MaxioCustomer MapCustomer(ApiCustomer? customer) => new()
    {
        Id = customer?.Id ?? 0,
        Reference = customer?.Reference,
        Email = customer?.Email,
        FirstName = customer?.FirstName,
        LastName = customer?.LastName
    };

    private static MaxioSubscription MapSubscription(ApiSubscription? subscription) => new()
    {
        Id = subscription?.Id ?? 0,
        State = subscription?.State ?? string.Empty,
        Reference = subscription?.Reference,
        CustomerId = subscription?.Customer?.Id ?? 0,
        PlanHandle = subscription?.Product?.Handle,
        PlanName = subscription?.Product?.Name,
        PriceInCents = subscription?.Product?.PriceInCents,
        Interval = subscription?.Product?.Interval,
        IntervalUnit = subscription?.Product?.IntervalUnit,
        Currency = subscription?.Currency,
        ActivatedAt = subscription?.ActivatedAt,
        NextBillingDate = subscription?.NextAssessmentAt ?? subscription?.CurrentPeriodEndsAt,
        CanceledAt = subscription?.CanceledAt,
        CancelAtEndOfPeriod = subscription?.CancelAtEndOfPeriod ?? false
    };

    private static bool IsLive(string state)
    {
        // Terminal states allow the customer to re-subscribe to the same plan;
        // every other state (active, trialing, past_due, on_hold, ...) is reused.
        return !string.Equals(state, "canceled", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(state, "expired", StringComparison.OrdinalIgnoreCase);
    }

    // Wire models (snake_case JSON, handled by the naming policy).
    private sealed class CreateCustomerBody(ApiCustomerCreate Customer)
    {
        public ApiCustomerCreate Customer { get; } = Customer;
    }

    private sealed class ApiCustomerCreate(
        string FirstName, string LastName, string Email, string Reference)
    {
        public string FirstName { get; } = FirstName;
        public string LastName { get; } = LastName;
        public string Email { get; } = Email;
        public string Reference { get; } = Reference;
    }

    private sealed class CreateSubscriptionBody(ApiSubscriptionCreate Subscription)
    {
        public ApiSubscriptionCreate Subscription { get; } = Subscription;
    }

    private sealed class ApiSubscriptionCreate(
        string ProductHandle, int CustomerId, string Reference, string PaymentCollectionMethod)
    {
        public string ProductHandle { get; } = ProductHandle;
        public int CustomerId { get; } = CustomerId;
        public string Reference { get; } = Reference;
        public string PaymentCollectionMethod { get; } = PaymentCollectionMethod;
    }

    private sealed class ApiCustomerResponse
    {
        public ApiCustomer? Customer { get; set; }
    }

    private sealed class ApiCustomer
    {
        public int Id { get; set; }
        public string? Reference { get; set; }
        public string? Email { get; set; }
        public string? FirstName { get; set; }
        public string? LastName { get; set; }
    }

    private sealed class ApiProductFamilyResponse
    {
        public ApiProductFamily? ProductFamily { get; set; }
    }

    private sealed class ApiProductFamily
    {
        public int Id { get; set; }
        public string? Handle { get; set; }
        public string? Name { get; set; }
    }

    private sealed class ApiProductResponse
    {
        public ApiProduct? Product { get; set; }
    }

    private sealed class ApiProduct
    {
        public int Id { get; set; }
        public string? Handle { get; set; }
        public string? Name { get; set; }
        public string? Description { get; set; }
        public long? PriceInCents { get; set; }
        public int? Interval { get; set; }
        public string? IntervalUnit { get; set; }
        public DateTimeOffset? ArchivedAt { get; set; }
    }

    private sealed class ApiSubscriptionResponse
    {
        public ApiSubscription? Subscription { get; set; }
    }

    private sealed class ApiSubscription
    {
        public int Id { get; set; }
        public string? State { get; set; }
        public string? Reference { get; set; }
        public string? Currency { get; set; }
        public DateTimeOffset? ActivatedAt { get; set; }
        public DateTimeOffset? CanceledAt { get; set; }
        public DateTimeOffset? NextAssessmentAt { get; set; }
        public DateTimeOffset? CurrentPeriodEndsAt { get; set; }
        public bool? CancelAtEndOfPeriod { get; set; }
        public ApiProduct? Product { get; set; }
        public ApiCustomer? Customer { get; set; }
    }
}
