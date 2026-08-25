using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Billing;

/// <summary>
/// Maxio Advanced Billing client. Maxio is the system of record: the eShop user id is stored
/// as the Maxio customer reference, which is what makes subscribe idempotent across runs.
/// </summary>
public class MaxioBillingService : IMaxioBillingService
{
    // States after which a subscription no longer bills; anything else counts as "already subscribed".
    private static readonly HashSet<string> FinalStates = new(StringComparer.OrdinalIgnoreCase)
    {
        "canceled", "expired", "trial_ended", "failed_to_create"
    };

    // Serializes subscribe operations per user so a concurrent double-click cannot pass
    // the read-before-create check twice.
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> SubscribeLocks = new();

    private static readonly System.Text.Json.JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true
    };

    private readonly HttpClient _httpClient;
    private readonly MaxioSettings _settings;
    private readonly ILogger<MaxioBillingService> _logger;

    public MaxioBillingService(HttpClient httpClient, IOptions<MaxioSettings> settings, ILogger<MaxioBillingService> logger)
    {
        _httpClient = httpClient;
        _settings = settings.Value;
        _logger = logger;
    }

    public async Task<IReadOnlyList<SubscriptionPlan>> ListPlansAsync(CancellationToken cancellationToken = default)
    {
        var family = Uri.EscapeDataString(_settings.ProductFamilyHandle);
        var items = await GetAsync<List<MaxioProductListItem>>(
            $"product_families/handle:{family}/products.json", cancellationToken);

        return (items ?? new List<MaxioProductListItem>())
            .Select(i => i.Product)
            .Where(p => p is not null && p.ArchivedAt is null)
            .Select(p => new SubscriptionPlan(
                p!.Id, p.Name, p.Handle, p.Description, p.PriceInCents, p.Interval, p.IntervalUnit))
            .ToList();
    }

    public async Task<CustomerSubscription> SubscribeAsync(string userId, string email, string productHandle, CancellationToken cancellationToken = default)
    {
        var gate = SubscribeLocks.GetOrAdd(userId, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            var customer = await EnsureCustomerAsync(userId, email, cancellationToken);

            var existing = await ListCustomerSubscriptionsAsync(customer.Id, cancellationToken);
            var current = existing.FirstOrDefault(s =>
                s.Subscription?.Product?.Handle == productHandle &&
                !FinalStates.Contains(s.Subscription.State));
            if (current?.Subscription is not null)
            {
                _logger.LogInformation(
                    "User {UserId} already has a {State} subscription {SubscriptionId} for {ProductHandle}; returning it.",
                    userId, current.Subscription.State, current.Subscription.Id, productHandle);
                return Map(current.Subscription);
            }

            var request = new MaxioCreateSubscriptionRequest
            {
                Subscription = new MaxioCreateSubscriptionAttributes
                {
                    ProductHandle = productHandle,
                    CustomerId = customer.Id,
                    Reference = $"eshop-{userId}-{productHandle}",
                    PaymentCollectionMethod = "remittance"
                },
                UniquenessToken = Guid.NewGuid().ToString()
            };

            MaxioSubscriptionEnvelope? created;
            try
            {
                created = await PostAsync<MaxioCreateSubscriptionRequest, MaxioSubscriptionEnvelope>(
                    "subscriptions.json", request, cancellationToken);
            }
            catch (MaxioApiException ex) when (ex.StatusCode == (int)HttpStatusCode.Conflict)
            {
                // A retried create with the same uniqueness token means the original request
                // was received; re-read and return the subscription it created.
                _logger.LogWarning("Duplicate submission detected for user {UserId}; re-reading subscriptions.", userId);
                var afterConflict = await ListCustomerSubscriptionsAsync(customer.Id, cancellationToken);
                var match = afterConflict.FirstOrDefault(s =>
                    s.Subscription?.Product?.Handle == productHandle &&
                    !FinalStates.Contains(s.Subscription.State));
                if (match?.Subscription is not null)
                    return Map(match.Subscription);
                throw;
            }

            if (created?.Subscription is null)
                throw new MaxioApiException((int)HttpStatusCode.OK, "Create subscription returned an empty body.");

            _logger.LogInformation(
                "Created subscription {SubscriptionId} for user {UserId} on plan {ProductHandle}.",
                created.Subscription.Id, userId, productHandle);
            return Map(created.Subscription);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<IReadOnlyList<CustomerSubscription>> GetSubscriptionsAsync(string userId, string email, CancellationToken cancellationToken = default)
    {
        var customer = await FindCustomerByReferenceAsync(userId, cancellationToken);
        if (customer is null)
            return Array.Empty<CustomerSubscription>();

        var subscriptions = await ListCustomerSubscriptionsAsync(customer.Id, cancellationToken);
        return subscriptions
            .Where(s => s.Subscription is not null)
            .Select(s => Map(s.Subscription!))
            .ToList();
    }

    private async Task<MaxioCustomer> EnsureCustomerAsync(string userId, string email, CancellationToken cancellationToken)
    {
        var existing = await FindCustomerByReferenceAsync(userId, cancellationToken);
        if (existing is not null)
            return existing;

        var request = new MaxioCreateCustomerRequest
        {
            Customer = new MaxioCustomerAttributes
            {
                FirstName = DeriveFirstName(email),
                LastName = "Shopper",
                Email = email,
                Reference = userId
            }
        };

        try
        {
            var created = await PostAsync<MaxioCreateCustomerRequest, MaxioCustomerEnvelope>(
                "customers.json", request, cancellationToken);
            if (created?.Customer is not null)
            {
                _logger.LogInformation("Created Maxio customer {CustomerId} for user {UserId}.", created.Customer.Id, userId);
                return created.Customer;
            }
        }
        catch (MaxioApiException ex) when (ex.StatusCode == (int)HttpStatusCode.UnprocessableEntity)
        {
            // Lost a race with a concurrent create: the reference is unique in Maxio, so look it up.
            _logger.LogWarning("Customer create for user {UserId} hit a duplicate reference; re-reading.", userId);
            var raced = await FindCustomerByReferenceAsync(userId, cancellationToken);
            if (raced is not null)
                return raced;
            throw;
        }

        throw new MaxioApiException((int)HttpStatusCode.OK, "Create customer returned an empty body.");
    }

    private async Task<MaxioCustomer?> FindCustomerByReferenceAsync(string reference, CancellationToken cancellationToken)
    {
        var response = await SendWithRetryAsync(
            () => new HttpRequestMessage(HttpMethod.Get, $"customers/lookup.json?reference={Uri.EscapeDataString(reference)}"),
            cancellationToken);

        if (response.StatusCode == HttpStatusCode.NotFound)
            return null;

        var envelope = await ReadAsync<MaxioCustomerEnvelope>(response, cancellationToken);
        return envelope?.Customer;
    }

    private async Task<List<MaxioSubscriptionListItem>> ListCustomerSubscriptionsAsync(long customerId, CancellationToken cancellationToken)
    {
        return await GetAsync<List<MaxioSubscriptionListItem>>(
                   $"customers/{customerId}/subscriptions.json", cancellationToken)
               ?? new List<MaxioSubscriptionListItem>();
    }

    private async Task<T?> GetAsync<T>(string relativeUri, CancellationToken cancellationToken)
    {
        var response = await SendWithRetryAsync(
            () => new HttpRequestMessage(HttpMethod.Get, relativeUri), cancellationToken);
        return await ReadAsync<T>(response, cancellationToken);
    }

    private async Task<TResponse?> PostAsync<TRequest, TResponse>(string relativeUri, TRequest body, CancellationToken cancellationToken)
    {
        var response = await SendWithRetryAsync(() =>
            {
                var message = new HttpRequestMessage(HttpMethod.Post, relativeUri)
                {
                    Content = JsonContent.Create(body, options: JsonOptions)
                };
                return message;
            },
            cancellationToken);
        return await ReadAsync<TResponse>(response, cancellationToken);
    }

    private static async Task<T?> ReadAsync<T>(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (!response.IsSuccessStatusCode)
        {
            var error = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new MaxioApiException((int)response.StatusCode, error);
        }

        return await response.Content.ReadFromJsonAsync<T>(JsonOptions, cancellationToken);
    }

    private async Task<HttpResponseMessage> SendWithRetryAsync(Func<HttpRequestMessage> requestFactory, CancellationToken cancellationToken)
    {
        const int maxAttempts = 3;
        for (var attempt = 1; ; attempt++)
        {
            using var request = requestFactory();
            try
            {
                var response = await _httpClient.SendAsync(request, cancellationToken);
                if (!IsTransient(response.StatusCode) || attempt == maxAttempts)
                    return response;

                _logger.LogWarning("Maxio returned transient status {StatusCode}; retry {Attempt}/{MaxAttempts}.",
                    (int)response.StatusCode, attempt, maxAttempts);
                response.Dispose();
            }
            catch (HttpRequestException ex) when (attempt < maxAttempts)
            {
                _logger.LogWarning(ex, "Maxio request failed transiently; retry {Attempt}/{MaxAttempts}.", attempt, maxAttempts);
            }

            await Task.Delay(TimeSpan.FromMilliseconds(250 * Math.Pow(2, attempt - 1)), cancellationToken);
        }
    }

    private static bool IsTransient(HttpStatusCode statusCode) =>
        statusCode == HttpStatusCode.RequestTimeout ||
        statusCode == HttpStatusCode.TooManyRequests ||
        (int)statusCode >= 500;

    private static CustomerSubscription Map(MaxioSubscription subscription) =>
        new(
            subscription.Id,
            subscription.Reference,
            subscription.State,
            subscription.Product?.Handle,
            subscription.Product?.Name,
            subscription.ProductPriceInCents,
            subscription.CurrentPeriodEndsAt,
            subscription.NextAssessmentAt,
            subscription.CreatedAt,
            subscription.Customer?.Id ?? 0);

    private static string DeriveFirstName(string email)
    {
        var localPart = email.Split('@')[0];
        return string.IsNullOrWhiteSpace(localPart) ? "eShop" : localPart;
    }
}
