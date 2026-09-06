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
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using Microsoft.eShopWeb.Infrastructure.Maxio.Contracts;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Talks to the Maxio Advanced Billing REST API over HTTP Basic auth (API key as the user name,
/// "X" as the password) and maps its responses onto the application's billing model.
/// </summary>
public class MaxioBillingGateway : IBillingGateway
{
    /// <summary>Maxio's maximum page size for list endpoints.</summary>
    private const int PageSize = 200;

    /// <summary>Stops a runaway pagination loop if the API ever keeps returning full pages.</summary>
    private const int MaxPages = 25;

    internal static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    /// <summary>Cache key for the billing site's settings, which are the same for every caller.</summary>
    private const string SiteCacheKey = "Maxio:Site";

    private readonly HttpClient _httpClient;
    private readonly MaxioSettings _settings;
    private readonly IMemoryCache _cache;
    private readonly IAppLogger<MaxioBillingGateway> _logger;

    public MaxioBillingGateway(HttpClient httpClient,
        IOptions<MaxioSettings> settings,
        IMemoryCache cache,
        IAppLogger<MaxioBillingGateway> logger)
    {
        _httpClient = httpClient;
        _settings = settings.Value;
        _cache = cache;
        _logger = logger;
    }

    public async Task<BillingSite> GetSiteAsync(CancellationToken cancellationToken = default)
    {
        if (_settings.SiteCacheMinutes > 0 && _cache.TryGetValue(SiteCacheKey, out BillingSite? cached) && cached is not null)
        {
            return cached;
        }

        using var response = await SendAsync(() => new HttpRequestMessage(HttpMethod.Get, "site.json"), cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);

        var envelope = await ReadAsync<SiteEnvelope>(response, cancellationToken);
        if (envelope?.Site is null)
        {
            throw new BillingException("Maxio returned no site settings.");
        }

        var site = MapSite(envelope.Site);
        if (_settings.SiteCacheMinutes > 0)
        {
            _cache.Set(SiteCacheKey, site, TimeSpan.FromMinutes(_settings.SiteCacheMinutes));
        }

        return site;
    }

    public async Task<IReadOnlyCollection<SubscriptionPlan>> ListPlansAsync(CancellationToken cancellationToken = default)
    {
        // "handle:" is how Maxio accepts a family handle where an id is expected.
        var family = Uri.EscapeDataString($"handle:{_settings.ProductFamilyHandle}");
        var site = await GetSiteAsync(cancellationToken);
        var products = await GetPagedAsync<ProductEnvelope>($"product_families/{family}/products.json", cancellationToken);

        return products
            .Select(envelope => envelope.Product)
            .Where(product => product is not null && product.ArchivedAt is null && !string.IsNullOrEmpty(product.Handle))
            .Select(product => MapPlan(product!, site.Currency))
            .OrderBy(plan => plan.PriceInCents)
            .ToList();
    }

    public async Task<BillingCustomer?> FindCustomerByReferenceAsync(string reference, CancellationToken cancellationToken = default)
    {
        var path = $"customers/lookup.json?reference={Uri.EscapeDataString(reference)}";
        using var response = await SendAsync(() => new HttpRequestMessage(HttpMethod.Get, path), cancellationToken);

        // Maxio answers a miss with 404 rather than an empty body.
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        await EnsureSuccessAsync(response, cancellationToken);
        var envelope = await ReadAsync<CustomerEnvelope>(response, cancellationToken);

        return envelope?.Customer is null ? null : MapCustomer(envelope.Customer);
    }

    public async Task<BillingCustomer> CreateCustomerAsync(NewBillingCustomer customer, CancellationToken cancellationToken = default)
    {
        var body = new CreateCustomerRequest
        {
            Customer = new CreateCustomerAttributes
            {
                FirstName = customer.FirstName,
                LastName = customer.LastName,
                Email = customer.Email,
                Reference = customer.Reference,
                Organization = customer.Organization
            }
        };

        using var response = await SendAsync(() => new HttpRequestMessage(HttpMethod.Post, "customers.json")
        {
            Content = JsonContent.Create(body, options: SerializerOptions)
        }, cancellationToken);

        await EnsureSuccessAsync(response, cancellationToken);
        var envelope = await ReadAsync<CustomerEnvelope>(response, cancellationToken);

        return envelope?.Customer is null
            ? throw new BillingException("Maxio accepted the customer but returned no customer in the response.")
            : MapCustomer(envelope.Customer);
    }

    public async Task<IReadOnlyCollection<BillingSubscription>> ListCustomerSubscriptionsAsync(int customerId, CancellationToken cancellationToken = default)
    {
        var site = await GetSiteAsync(cancellationToken);
        var subscriptions = await GetPagedAsync<SubscriptionEnvelope>($"customers/{customerId}/subscriptions.json", cancellationToken);

        return subscriptions
            .Select(envelope => envelope.Subscription)
            .Where(subscription => subscription is not null)
            .Select(subscription => MapSubscription(subscription!, site.Currency))
            .ToList();
    }

    public async Task<BillingSubscription> CreateSubscriptionAsync(NewSubscription subscription, CancellationToken cancellationToken = default)
    {
        var site = await GetSiteAsync(cancellationToken);

        var body = new CreateSubscriptionRequest
        {
            Subscription = new CreateSubscriptionAttributes
            {
                CustomerId = subscription.CustomerId,
                ProductHandle = subscription.PlanHandle,
                PaymentCollectionMethod = ResolvePaymentCollectionMethod(site)
            },
            UniquenessToken = subscription.UniquenessToken
        };

        using var response = await SendAsync(() => new HttpRequestMessage(HttpMethod.Post, "subscriptions.json")
        {
            Content = JsonContent.Create(body, options: SerializerOptions)
        }, cancellationToken);

        await EnsureSuccessAsync(response, cancellationToken);
        var envelope = await ReadAsync<SubscriptionEnvelope>(response, cancellationToken);

        return envelope?.Subscription is null
            ? throw new BillingException("Maxio accepted the subscription but returned no subscription in the response.")
            : MapSubscription(envelope.Subscription, site.Currency);
    }

    private async Task<IReadOnlyList<T>> GetPagedAsync<T>(string path, CancellationToken cancellationToken)
    {
        var separator = path.Contains('?', StringComparison.Ordinal) ? "&" : "?";
        var results = new List<T>();

        for (var page = 1; page <= MaxPages; page++)
        {
            var pagedPath = $"{path}{separator}page={page}&per_page={PageSize}";
            using var response = await SendAsync(() => new HttpRequestMessage(HttpMethod.Get, pagedPath), cancellationToken);
            await EnsureSuccessAsync(response, cancellationToken);

            var batch = await ReadAsync<List<T>>(response, cancellationToken);
            if (batch is null || batch.Count == 0)
            {
                break;
            }

            results.AddRange(batch);
            if (batch.Count < PageSize)
            {
                break;
            }
        }

        return results;
    }

    /// <summary>
    /// Issues a request, retrying throttled, transient and transport failures with exponential
    /// backoff. Every request this gateway sends is either a read or carries a uniqueness token, so
    /// a retry cannot silently duplicate work.
    /// </summary>
    private async Task<HttpResponseMessage> SendAsync(Func<HttpRequestMessage> requestFactory, CancellationToken cancellationToken)
    {
        for (var attempt = 0; ; attempt++)
        {
            var isLastAttempt = attempt >= _settings.MaxRetries;
            TimeSpan delay;

            try
            {
                using var request = requestFactory();
                var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);

                if (isLastAttempt || !IsTransient(response.StatusCode))
                {
                    return response;
                }

                delay = ResolveDelay(attempt, response.Headers.RetryAfter?.Delta);
                _logger.LogWarning("Maxio returned {StatusCode} for {Method} {Path}; retrying in {DelayMs}ms (attempt {Attempt} of {MaxAttempts}).",
                    (int)response.StatusCode, request.Method.Method, request.RequestUri?.ToString() ?? string.Empty, (int)delay.TotalMilliseconds, attempt + 1, _settings.MaxRetries + 1);
                response.Dispose();
            }
            catch (HttpRequestException ex)
            {
                if (isLastAttempt)
                {
                    throw new BillingUnavailableException("Could not reach Maxio Advanced Billing.", ex);
                }

                delay = ResolveDelay(attempt, null);
                _logger.LogWarning("Maxio request failed ({Error}); retrying in {DelayMs}ms.", ex.Message, (int)delay.TotalMilliseconds);
            }
            catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
            {
                // The caller did not cancel, so this is the HttpClient timeout firing.
                if (isLastAttempt)
                {
                    throw new BillingUnavailableException($"Maxio Advanced Billing did not respond within {_settings.TimeoutSeconds}s.", ex);
                }

                delay = ResolveDelay(attempt, null);
                _logger.LogWarning("Maxio request timed out; retrying in {DelayMs}ms.", (int)delay.TotalMilliseconds);
            }

            await Task.Delay(delay, cancellationToken);
        }
    }

    private static bool IsTransient(HttpStatusCode statusCode)
        => statusCode == HttpStatusCode.TooManyRequests
            || statusCode == HttpStatusCode.RequestTimeout
            || (int)statusCode >= 500;

    /// <summary>
    /// Honours Retry-After when Maxio supplies one, otherwise backs off exponentially with jitter so
    /// concurrent callers do not line up and retry in lockstep.
    /// </summary>
    private TimeSpan ResolveDelay(int attempt, TimeSpan? retryAfter)
    {
        if (retryAfter is { } wait && wait > TimeSpan.Zero)
        {
            return wait;
        }

        var backoffMs = _settings.RetryBaseDelayMilliseconds * Math.Pow(2, attempt);
        var jitterMs = Random.Shared.NextDouble() * _settings.RetryBaseDelayMilliseconds;

        return TimeSpan.FromMilliseconds(backoffMs + jitterMs);
    }

    private static async Task<T?> ReadAsync<T>(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        try
        {
            return await response.Content.ReadFromJsonAsync<T>(SerializerOptions, cancellationToken);
        }
        catch (JsonException ex)
        {
            throw new BillingException("Maxio returned a response that could not be understood.", ex);
        }
    }

    /// <summary>
    /// Turns a non-success response into the matching application-level billing exception. The API
    /// key is never included in what is thrown or logged.
    /// </summary>
    private async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var body = await SafeReadBodyAsync(response, cancellationToken);
        var errors = ParseErrors(body);
        var detail = errors.Count > 0 ? string.Join(" ", errors) : response.ReasonPhrase ?? string.Empty;

        _logger.LogWarning("Maxio responded {StatusCode} for {Path}: {Detail}",
            (int)response.StatusCode, response.RequestMessage?.RequestUri?.ToString() ?? string.Empty, detail);

        throw response.StatusCode switch
        {
            HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden =>
                new BillingException("Maxio rejected the configured API credentials. Check Maxio:ApiKey and Maxio:Subdomain."),

            HttpStatusCode.NotFound =>
                new BillingException($"Maxio could not find the requested resource ({response.RequestMessage?.RequestUri?.AbsolutePath})."),

            HttpStatusCode.Conflict =>
                new BillingConflictException($"Maxio rejected the request as a duplicate. {detail}".TrimEnd()),

            HttpStatusCode.UnprocessableEntity when IsReferenceTaken(errors) =>
                new BillingConflictException($"Maxio reports the reference is already in use. {detail}".TrimEnd()),

            HttpStatusCode.UnprocessableEntity =>
                new BillingValidationException(detail.Length > 0 ? detail : "Maxio rejected the request.", errors),

            HttpStatusCode.TooManyRequests =>
                new BillingUnavailableException("Maxio is throttling requests. Please retry shortly."),

            _ when (int)response.StatusCode >= 500 =>
                new BillingUnavailableException($"Maxio returned {(int)response.StatusCode}. {detail}".TrimEnd()),

            _ => new BillingException($"Maxio returned {(int)response.StatusCode}. {detail}".TrimEnd())
        };
    }

    private static bool IsReferenceTaken(IReadOnlyCollection<string> errors)
        => errors.Any(error => error.Contains("reference", StringComparison.OrdinalIgnoreCase)
            && error.Contains("unique", StringComparison.OrdinalIgnoreCase));

    private static async Task<string> SafeReadBodyAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        try
        {
            return await response.Content.ReadAsStringAsync(cancellationToken);
        }
        catch (Exception)
        {
            return string.Empty;
        }
    }

    /// <summary>
    /// Maxio reports failures as {"errors": ["..."]} for general problems and
    /// {"errors": {"field": ["..."]}} for per-field validation, so both shapes are handled.
    /// </summary>
    private static IReadOnlyList<string> ParseErrors(string body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return Array.Empty<string>();
        }

        try
        {
            using var document = JsonDocument.Parse(body);
            if (document.RootElement.ValueKind != JsonValueKind.Object
                || !document.RootElement.TryGetProperty("errors", out var errors))
            {
                return Array.Empty<string>();
            }

            return errors.ValueKind switch
            {
                JsonValueKind.String => new[] { errors.GetString()! },
                JsonValueKind.Array => errors.EnumerateArray()
                    .Select(element => element.ToString())
                    .Where(text => !string.IsNullOrWhiteSpace(text))
                    .ToArray(),
                JsonValueKind.Object => errors.EnumerateObject()
                    .SelectMany(property => property.Value.ValueKind == JsonValueKind.Array
                        ? property.Value.EnumerateArray().Select(element => $"{property.Name}: {element}")
                        : new[] { $"{property.Name}: {property.Value}" })
                    .ToArray(),
                _ => Array.Empty<string>()
            };
        }
        catch (JsonException)
        {
            return Array.Empty<string>();
        }
    }

    /// <summary>
    /// eShopOnWeb's subscribe flow captures no payment method, so a subscription created with the
    /// site's usual "automatic" method would be rejected for having no card on file. Unless the
    /// deployment says otherwise, bill by invoice instead.
    /// </summary>
    private string ResolvePaymentCollectionMethod(BillingSite site)
        => string.IsNullOrWhiteSpace(_settings.PaymentCollectionMethod)
            ? site.InvoicePaymentCollectionMethod
            : _settings.PaymentCollectionMethod.Trim();

    private static BillingSite MapSite(SiteResource site) => new()
    {
        Id = site.Id,
        Name = site.Name ?? string.Empty,
        Subdomain = site.Subdomain ?? string.Empty,
        Currency = site.Currency ?? string.Empty,
        RelationshipInvoicingEnabled = site.RelationshipInvoicingEnabled,
        DefaultPaymentCollectionMethod = site.DefaultPaymentCollectionMethod,
        TestMode = site.Test
    };

    private static SubscriptionPlan MapPlan(ProductResource product, string currency) => new()
    {
        Currency = currency,
        Id = product.Id,
        Handle = product.Handle ?? string.Empty,
        Name = product.Name ?? string.Empty,
        Description = product.Description,
        PriceInCents = product.PriceInCents,
        Interval = product.Interval,
        IntervalUnit = product.IntervalUnit ?? string.Empty,
        RequiresPaymentMethod = product.RequireCreditCard,
        PricePointName = product.ProductPricePointName,
        ProductFamilyHandle = product.ProductFamily?.Handle,
        TrialPriceInCents = product.TrialPriceInCents ?? 0,
        TrialInterval = product.TrialInterval,
        TrialIntervalUnit = product.TrialIntervalUnit,
        InitialChargeInCents = product.InitialChargeInCents ?? 0
    };

    private static BillingCustomer MapCustomer(CustomerResource customer) => new()
    {
        Id = customer.Id,
        Reference = customer.Reference,
        FirstName = customer.FirstName ?? string.Empty,
        LastName = customer.LastName ?? string.Empty,
        Email = customer.Email ?? string.Empty,
        Organization = customer.Organization
    };

    private static BillingSubscription MapSubscription(SubscriptionResource subscription, string currency) => new()
    {
        Currency = currency,
        Id = subscription.Id,
        State = subscription.State ?? string.Empty,
        CustomerId = subscription.Customer?.Id ?? 0,
        CustomerReference = subscription.Customer?.Reference,
        PlanHandle = subscription.Product?.Handle,
        PlanName = subscription.Product?.Name,
        PlanPriceInCents = subscription.Product?.PriceInCents ?? 0,
        PlanInterval = subscription.Product?.Interval ?? 0,
        PlanIntervalUnit = subscription.Product?.IntervalUnit,
        PriceInCents = subscription.ProductPriceInCents,
        NextBillingAt = subscription.NextAssessmentAt,
        CurrentPeriodStartsAt = subscription.CurrentPeriodStartedAt,
        CurrentPeriodEndsAt = subscription.CurrentPeriodEndsAt,
        ActivatedAt = subscription.ActivatedAt,
        CanceledAt = subscription.CanceledAt,
        TrialEndedAt = subscription.TrialEndedAt,
        CreatedAt = subscription.CreatedAt,
        BalanceInCents = subscription.BalanceInCents
    };
}
