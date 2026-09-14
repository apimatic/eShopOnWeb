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
using Microsoft.eShopWeb.ApplicationCore.Billing;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Billing;

public sealed class MaxioClient : IMaxioClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly HttpClient _httpClient;
    private readonly MaxioOptions _options;
    private readonly string _baseUrl;

    public MaxioClient(HttpClient httpClient, IOptions<MaxioOptions> options)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _baseUrl = _options.ResolveBaseUrl().TrimEnd('/');

        var credentials = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{_options.ApiKey}:X"));
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", credentials);
        _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("eShopOnWeb-Maxio/1.0");
    }

    public async Task<IReadOnlyList<SubscriptionPlan>> ListPlansAsync(CancellationToken cancellationToken)
    {
        var family = Uri.EscapeDataString($"handle:{_options.ProductFamilyHandle}");
        var products = new List<ProductEnvelope>();
        const int pageSize = 200;
        for (var page = 1; ; page++)
        {
            var response = await SendAsync<List<ProductEnvelope>>(
                HttpMethod.Get,
                $"product_families/{family}/products.json?include_archived=false&page={page}&per_page={pageSize}",
                null,
                false,
                cancellationToken);
            products.AddRange(response!);
            if (response!.Count < pageSize)
            {
                break;
            }
        }

        return products
            .Select(item => item.Product)
            .Where(product => product.ArchivedAt is null &&
                string.Equals(product.ProductFamily.Handle, _options.ProductFamilyHandle, StringComparison.Ordinal))
            .Select(product => new SubscriptionPlan(
                product.Handle,
                product.Name,
                product.Description ?? string.Empty,
                product.PriceInCents,
                product.Interval,
                product.IntervalUnit,
                product.RequireCreditCard))
            .OrderBy(plan => plan.PriceInCents)
            .ToList();
    }

    public async Task<MaxioCustomer?> FindCustomerAsync(string reference, CancellationToken cancellationToken)
    {
        var response = await SendAsync<CustomerEnvelope>(
            HttpMethod.Get,
            $"customers/lookup.json?reference={Uri.EscapeDataString(reference)}",
            null,
            true,
            cancellationToken);
        return response is null ? null : new MaxioCustomer(response.Customer.Id);
    }

    public async Task<MaxioCustomer> CreateCustomerAsync(
        string firstName,
        string lastName,
        string email,
        string reference,
        CancellationToken cancellationToken)
    {
        var body = new
        {
            customer = new
            {
                first_name = firstName,
                last_name = lastName,
                email,
                reference
            }
        };
        var response = await SendAsync<CustomerEnvelope>(
            HttpMethod.Post,
            "customers.json",
            body,
            false,
            cancellationToken);
        return new MaxioCustomer(response!.Customer.Id);
    }

    public async Task<MaxioSubscription?> FindSubscriptionAsync(
        string reference,
        CancellationToken cancellationToken)
    {
        var response = await SendAsync<SubscriptionEnvelope>(
            HttpMethod.Get,
            $"subscriptions/lookup.json?reference={Uri.EscapeDataString(reference)}",
            null,
            true,
            cancellationToken);
        return response is null ? null : Map(response.Subscription);
    }

    public async Task<MaxioSubscription> CreateSubscriptionAsync(
        string productHandle,
        string customerReference,
        string subscriptionReference,
        CancellationToken cancellationToken)
    {
        var body = new
        {
            subscription = new
            {
                product_handle = productHandle,
                customer_reference = customerReference,
                reference = subscriptionReference,
                payment_collection_method = "remittance"
            }
        };
        var response = await SendAsync<SubscriptionEnvelope>(
            HttpMethod.Post,
            "subscriptions.json",
            body,
            false,
            cancellationToken);
        return Map(response!.Subscription);
    }

    public async Task<IReadOnlyList<MaxioSubscription>> ListCustomerSubscriptionsAsync(
        long customerId,
        CancellationToken cancellationToken)
    {
        var response = await SendAsync<List<SubscriptionEnvelope>>(
            HttpMethod.Get,
            $"customers/{customerId}/subscriptions.json",
            null,
            false,
            cancellationToken);
        return response!.Select(item => Map(item.Subscription)).ToList();
    }

    private async Task<T?> SendAsync<T>(
        HttpMethod method,
        string relativeUrl,
        object? body,
        bool allowNotFound,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, new Uri($"{_baseUrl}/{relativeUrl}", UriKind.Absolute));
        if (body is not null)
        {
            request.Content = JsonContent.Create(body, options: JsonOptions);
        }

        HttpResponseMessage response;
        try
        {
            response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        }
        catch (OperationCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            throw new BillingProviderException("The billing provider timed out.", true, null, exception);
        }
        catch (HttpRequestException exception)
        {
            throw new BillingProviderException("The billing provider could not be reached.", true, null, exception);
        }

        using (response)
        {
            if (allowNotFound && response.StatusCode == HttpStatusCode.NotFound)
            {
                return default;
            }

            if (!response.IsSuccessStatusCode)
            {
                var detail = await ReadErrorDetailAsync(response, cancellationToken);
                var transient = response.StatusCode == HttpStatusCode.RequestTimeout ||
                    response.StatusCode == HttpStatusCode.TooManyRequests ||
                    (int)response.StatusCode >= 500;
                throw new BillingProviderException(
                    string.IsNullOrWhiteSpace(detail)
                        ? "The billing provider rejected the request."
                        : $"The billing provider rejected the request: {detail}",
                    transient,
                    response.StatusCode);
            }

            var value = await response.Content.ReadFromJsonAsync<T>(JsonOptions, cancellationToken);
            return value ?? throw new BillingProviderException(
                "The billing provider returned an empty response.",
                true,
                response.StatusCode);
        }
    }

    private static async Task<string> ReadErrorDetailAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        try
        {
            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            if (!document.RootElement.TryGetProperty("errors", out var errors))
            {
                return string.Empty;
            }

            return errors.ValueKind switch
            {
                JsonValueKind.Array => string.Join("; ", errors.EnumerateArray().Select(item => item.ToString())),
                JsonValueKind.Object => string.Join("; ", errors.EnumerateObject().Select(item => $"{item.Name}: {item.Value}")),
                _ => errors.ToString()
            };
        }
        catch (JsonException)
        {
            return string.Empty;
        }
    }

    private static MaxioSubscription Map(SubscriptionData subscription)
    {
        var product = subscription.Product ?? throw new BillingProviderException(
            "The billing provider returned a subscription without a plan.",
            false);
        return new MaxioSubscription(
            subscription.Id,
            subscription.Customer.Id,
            product.Handle,
            product.Name,
            product.ProductFamily.Handle,
            product.ProductPricePointName ?? subscription.ProductPricePointName ?? "Default",
            subscription.ProductPriceInCents,
            product.Interval,
            product.IntervalUnit,
            subscription.State,
            subscription.NextAssessmentAt);
    }
}
