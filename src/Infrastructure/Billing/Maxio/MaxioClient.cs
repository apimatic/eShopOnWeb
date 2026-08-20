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
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Billing.Maxio;

public sealed class MaxioClient : IMaxioClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly HttpClient _httpClient;
    private readonly MaxioOptions _options;
    private readonly string _apiBaseUrl;

    public MaxioClient(HttpClient httpClient, IOptions<MaxioOptions> options)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _apiBaseUrl = _options.GetApiBaseUrl();

        var credentials = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{_options.ApiKey}:x"));
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", credentials);
        _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("eShopOnWeb-Maxio/1.0");
    }

    public async Task<IReadOnlyList<SubscriptionPlan>> ListPlansAsync(CancellationToken cancellationToken = default)
    {
        var family = Uri.EscapeDataString($"handle:{_options.ProductFamilyHandle}");
        var responses = await SendAsync<List<MaxioProductResponse>>(
            HttpMethod.Get,
            $"product_families/{family}/products.json?include_archived=false&page=1&per_page=200",
            null,
            cancellationToken);

        return responses
            .Select(response => response.Product)
            .Where(product => product is not null && product.ArchivedAt is null)
            .Select(product => product!)
            .Where(product => string.Equals(product.ProductFamily?.Handle, _options.ProductFamilyHandle, StringComparison.Ordinal))
            .Select(ToPlan)
            .OrderBy(plan => plan.PriceInCents)
            .ThenBy(plan => plan.Name, StringComparer.Ordinal)
            .ToArray();
    }

    public async Task<MaxioCustomer?> FindCustomerAsync(string reference, CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await SendAsync<MaxioCustomerResponse>(
                HttpMethod.Get,
                $"customers/lookup.json?reference={Uri.EscapeDataString(reference)}",
                null,
                cancellationToken);
            return ToCustomer(response);
        }
        catch (MaxioApiException exception) when (exception.StatusCode == (int)HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    public async Task<MaxioCustomer> CreateCustomerAsync(
        SubscriptionUser user,
        string reference,
        CancellationToken cancellationToken = default)
    {
        var body = new
        {
            customer = new
            {
                first_name = user.FirstName,
                last_name = user.LastName,
                email = user.Email,
                reference
            }
        };
        var response = await SendAsync<MaxioCustomerResponse>(HttpMethod.Post, "customers.json", body, cancellationToken);
        return ToCustomer(response);
    }

    public async Task<MaxioSubscription?> FindSubscriptionAsync(string reference, CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await SendAsync<MaxioSubscriptionResponse>(
                HttpMethod.Get,
                $"subscriptions/lookup.json?reference={Uri.EscapeDataString(reference)}",
                null,
                cancellationToken);
            return ToSubscription(response);
        }
        catch (MaxioApiException exception) when (exception.StatusCode == (int)HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    public async Task<IReadOnlyList<MaxioSubscription>> ListCustomerSubscriptionsAsync(
        int customerId,
        CancellationToken cancellationToken = default)
    {
        var responses = await SendAsync<List<MaxioSubscriptionResponse>>(
            HttpMethod.Get,
            $"customers/{customerId}/subscriptions.json",
            null,
            cancellationToken);
        return responses
            .Where(response => string.Equals(
                response.Subscription?.Product?.ProductFamily?.Handle,
                _options.ProductFamilyHandle,
                StringComparison.Ordinal))
            .Select(ToSubscription)
            .ToArray();
    }

    public async Task<MaxioSubscription> CreateSubscriptionAsync(
        string customerReference,
        string productHandle,
        string reference,
        CancellationToken cancellationToken = default)
    {
        var body = new
        {
            subscription = new
            {
                product_handle = productHandle,
                customer_reference = customerReference,
                reference,
                payment_collection_method = "remittance"
            }
        };
        var response = await SendAsync<MaxioSubscriptionResponse>(HttpMethod.Post, "subscriptions.json", body, cancellationToken);
        return ToSubscription(response);
    }

    private async Task<T> SendAsync<T>(
        HttpMethod method,
        string relativeUrl,
        object? body,
        CancellationToken cancellationToken)
    {
        const int maxGetAttempts = 3;
        var attempts = method == HttpMethod.Get ? maxGetAttempts : 1;

        for (var attempt = 1; attempt <= attempts; attempt++)
        {
            using var request = new HttpRequestMessage(method, BuildUri(relativeUrl));
            if (body is not null)
            {
                request.Content = JsonContent.Create(body, options: JsonOptions);
            }

            HttpResponseMessage response;
            try
            {
                response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            }
            catch (HttpRequestException) when (method == HttpMethod.Get && attempt < attempts)
            {
                await Task.Delay(TimeSpan.FromSeconds(attempt), cancellationToken);
                continue;
            }
            catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested && method == HttpMethod.Get && attempt < attempts)
            {
                await Task.Delay(TimeSpan.FromSeconds(attempt), cancellationToken);
                continue;
            }
            catch (HttpRequestException)
            {
                throw new MaxioApiException(503, new[] { "Maxio Advanced Billing could not be reached." });
            }
            catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                throw new MaxioApiException(504, new[] { "The Maxio Advanced Billing request timed out." });
            }

            using (response)
            {
                if (response.IsSuccessStatusCode)
                {
                    var result = await response.Content.ReadFromJsonAsync<T>(JsonOptions, cancellationToken);
                    return result ?? throw new MaxioApiException((int)response.StatusCode, new[] { "The response body was empty." });
                }

                var retryableGet = method == HttpMethod.Get &&
                                   ((int)response.StatusCode == 429 || (int)response.StatusCode >= 500) &&
                                   attempt < attempts;
                if (retryableGet)
                {
                    var delay = GetRetryDelay(response, attempt);
                    await Task.Delay(delay, cancellationToken);
                    continue;
                }

                var content = await response.Content.ReadAsStringAsync(cancellationToken);
                throw new MaxioApiException((int)response.StatusCode, ExtractErrors(content));
            }
        }

        throw new InvalidOperationException("The Maxio request retry loop completed unexpectedly.");
    }

    private Uri BuildUri(string relativeUrl)
    {
        var separator = _apiBaseUrl.EndsWith('/') ? string.Empty : "/";
        return new Uri($"{_apiBaseUrl}{separator}{relativeUrl.TrimStart('/')}", UriKind.Absolute);
    }

    private static TimeSpan GetRetryDelay(HttpResponseMessage response, int attempt)
    {
        if (response.Headers.RetryAfter?.Delta is { } retryAfter && retryAfter <= TimeSpan.FromSeconds(30))
        {
            return retryAfter;
        }

        return TimeSpan.FromSeconds(attempt);
    }

    private static IReadOnlyList<string> ExtractErrors(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return Array.Empty<string>();
        }

        try
        {
            using var document = JsonDocument.Parse(content);
            var root = document.RootElement;
            if (root.TryGetProperty("errors", out var errors))
            {
                return FlattenErrors(errors).Take(5).ToArray();
            }

            if (root.TryGetProperty("error", out var error))
            {
                return FlattenErrors(error).Take(5).ToArray();
            }

            if (root.TryGetProperty("message", out var message))
            {
                return FlattenErrors(message).Take(5).ToArray();
            }
        }
        catch (JsonException)
        {
            // Avoid returning an untrusted HTML/proxy response to API callers.
        }

        return Array.Empty<string>();
    }

    private static IEnumerable<string> FlattenErrors(JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                {
                    foreach (var error in FlattenErrors(item))
                    {
                        yield return error;
                    }
                }
                break;
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                {
                    foreach (var error in FlattenErrors(property.Value))
                    {
                        yield return $"{property.Name}: {error}";
                    }
                }
                break;
            case JsonValueKind.String:
                var value = element.GetString();
                if (!string.IsNullOrWhiteSpace(value))
                {
                    yield return value.Length <= 500 ? value : value[..500];
                }
                break;
            default:
                yield return element.ToString();
                break;
        }
    }

    private static SubscriptionPlan ToPlan(MaxioProductJson product)
    {
        if (string.IsNullOrWhiteSpace(product.Handle) ||
            string.IsNullOrWhiteSpace(product.Name) ||
            string.IsNullOrWhiteSpace(product.IntervalUnit) ||
            product.Interval <= 0)
        {
            throw new MaxioApiException(502, new[] { "Maxio returned an incomplete product." });
        }

        return new SubscriptionPlan(
            product.Handle,
            product.Name,
            product.Description ?? string.Empty,
            product.PriceInCents,
            product.Interval,
            product.IntervalUnit);
    }

    private static MaxioCustomer ToCustomer(MaxioCustomerResponse response)
    {
        var customer = response.Customer;
        if (customer is null || customer.Id <= 0 || string.IsNullOrWhiteSpace(customer.Reference))
        {
            throw new MaxioApiException(502, new[] { "Maxio returned an incomplete customer." });
        }

        return new MaxioCustomer(customer.Id, customer.Reference);
    }

    private static MaxioSubscription ToSubscription(MaxioSubscriptionResponse response)
    {
        var subscription = response.Subscription;
        var product = subscription?.Product;
        var customer = subscription?.Customer;
        if (subscription is null || subscription.Id <= 0 ||
            product is null || string.IsNullOrWhiteSpace(product.Handle) || string.IsNullOrWhiteSpace(product.Name) ||
            customer is null || customer.Id <= 0 || string.IsNullOrWhiteSpace(customer.Reference) ||
            string.IsNullOrWhiteSpace(subscription.State))
        {
            throw new MaxioApiException(502, new[] { "Maxio returned an incomplete subscription." });
        }

        var details = new SubscriptionDetails(
            subscription.Id,
            product.Handle,
            product.Name,
            subscription.ProductPriceInCents ?? product.PriceInCents,
            subscription.Currency ?? string.Empty,
            subscription.State,
            subscription.CurrentPeriodEndsAt ?? subscription.NextAssessmentAt);

        return new MaxioSubscription(
            details,
            customer.Id,
            customer.Reference,
            product.ProductFamily?.Handle ?? string.Empty,
            subscription.Reference ?? string.Empty);
    }
}
