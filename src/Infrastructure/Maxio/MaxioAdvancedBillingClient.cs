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
using Microsoft.eShopWeb.ApplicationCore.Billing;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

public class MaxioAdvancedBillingClient : IMaxioBillingClient
{
    private const int ProductPageSize = 200;

    private readonly HttpClient _http;
    private readonly ILogger<MaxioAdvancedBillingClient> _logger;

    public MaxioAdvancedBillingClient(HttpClient http, ILogger<MaxioAdvancedBillingClient> logger)
    {
        _http = http;
        _logger = logger;
    }

    public async Task<IReadOnlyList<BillingPlan>> ListProductsForProductFamilyAsync(string productFamilyHandle, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(productFamilyHandle))
        {
            throw new ArgumentException("A product family handle is required.", nameof(productFamilyHandle));
        }

        var familyId = $"handle:{Uri.EscapeDataString(productFamilyHandle)}";
        var plans = new List<BillingPlan>();
        var page = 1;

        while (true)
        {
            var path = $"product_families/{familyId}/products.json?page={page}&per_page={ProductPageSize}";
            var wrappers = await SendAsync<List<ProductResponse>>(HttpMethod.Get, path, null, cancellationToken)
                           ?? new List<ProductResponse>();

            foreach (var wrapper in wrappers)
            {
                if (wrapper.Product is null || wrapper.Product.ArchivedAt is not null)
                {
                    continue;
                }

                plans.Add(MapPlan(wrapper.Product));
            }

            if (wrappers.Count < ProductPageSize)
            {
                break;
            }

            page++;
        }

        return plans;
    }

    public async Task<BillingCustomer?> ReadCustomerByReferenceAsync(string reference, CancellationToken cancellationToken = default)
    {
        var path = $"customers/lookup.json?reference={Uri.EscapeDataString(reference)}";
        var response = await SendAsync<CustomerResponse>(HttpMethod.Get, path, null, cancellationToken, treatNotFoundAsNull: true);
        return response?.Customer is null ? null : MapCustomer(response.Customer);
    }

    public async Task<BillingCustomer> CreateCustomerAsync(string firstName, string lastName, string email, string reference, CancellationToken cancellationToken = default)
    {
        var payload = new CreateCustomerRequest
        {
            Customer = new CreateCustomerBody
            {
                FirstName = firstName,
                LastName = lastName,
                Email = email,
                Reference = reference
            }
        };

        var response = await SendAsync<CustomerResponse>(HttpMethod.Post, "customers.json", payload, cancellationToken)
                       ?? throw new MaxioClientException((int)HttpStatusCode.BadGateway, "Maxio createCustomer returned an empty body.");

        if (response.Customer is null)
        {
            throw new MaxioClientException((int)HttpStatusCode.BadGateway, "Maxio createCustomer returned no customer.");
        }

        return MapCustomer(response.Customer);
    }

    public async Task<IReadOnlyList<BillingSubscription>> ListCustomerSubscriptionsAsync(int customerId, CancellationToken cancellationToken = default)
    {
        var path = $"customers/{customerId}/subscriptions.json";
        var wrappers = await SendAsync<List<SubscriptionResponse>>(HttpMethod.Get, path, null, cancellationToken)
                       ?? new List<SubscriptionResponse>();

        return wrappers
            .Where(w => w.Subscription is not null)
            .Select(w => MapSubscription(w.Subscription!))
            .ToList();
    }

    public async Task<BillingSubscription?> FindSubscriptionByReferenceAsync(string reference, CancellationToken cancellationToken = default)
    {
        var path = $"subscriptions/lookup.json?reference={Uri.EscapeDataString(reference)}";
        var response = await SendAsync<SubscriptionResponse>(HttpMethod.Get, path, null, cancellationToken, treatNotFoundAsNull: true);
        return response?.Subscription is null ? null : MapSubscription(response.Subscription);
    }

    public async Task<BillingSubscription> CreateSubscriptionAsync(int customerId, string productHandle, string? reference, string? paymentCollectionMethod, CancellationToken cancellationToken = default)
    {
        var payload = new CreateSubscriptionRequest
        {
            Subscription = new CreateSubscriptionBody
            {
                CustomerId = customerId,
                ProductHandle = productHandle,
                Reference = reference,
                PaymentCollectionMethod = paymentCollectionMethod
            }
        };

        var response = await SendAsync<SubscriptionResponse>(HttpMethod.Post, "subscriptions.json", payload, cancellationToken)
                       ?? throw new MaxioClientException((int)HttpStatusCode.BadGateway, "Maxio createSubscription returned an empty body.");

        if (response.Subscription is null)
        {
            throw new MaxioClientException((int)HttpStatusCode.BadGateway, "Maxio createSubscription returned no subscription.");
        }

        return MapSubscription(response.Subscription);
    }

    private async Task<T?> SendAsync<T>(HttpMethod method, string relativePath, object? body, CancellationToken cancellationToken, bool treatNotFoundAsNull = false)
    {
        using var request = new HttpRequestMessage(method, relativePath);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        if (body is not null)
        {
            var json = JsonSerializer.Serialize(body, MaxioJson.SerializerOptions);
            request.Content = new StringContent(json, Encoding.UTF8, "application/json");
        }

        using var response = await _http.SendAsync(request, cancellationToken);
        var content = await response.Content.ReadAsStringAsync(cancellationToken);

        if (response.StatusCode == HttpStatusCode.NotFound && treatNotFoundAsNull)
        {
            return default;
        }

        if (!response.IsSuccessStatusCode)
        {
            var errors = ParseErrors(content);
            var message = errors.Count > 0
                ? $"Maxio {method} {relativePath} failed ({(int)response.StatusCode}): {string.Join("; ", errors)}"
                : $"Maxio {method} {relativePath} failed ({(int)response.StatusCode}).";

            _logger.LogWarning("Maxio request {Method} {Path} returned {StatusCode}.", method, StripQuery(relativePath), (int)response.StatusCode);
            throw new MaxioClientException((int)response.StatusCode, message, errors);
        }

        if (string.IsNullOrWhiteSpace(content) || typeof(T) == typeof(object))
        {
            return default;
        }

        try
        {
            return JsonSerializer.Deserialize<T>(content, MaxioJson.SerializerOptions);
        }
        catch (JsonException ex)
        {
            throw new MaxioClientException((int)HttpStatusCode.BadGateway, $"Maxio returned a payload that could not be parsed: {ex.Message}");
        }
    }

    private static IReadOnlyList<string> ParseErrors(string? body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return Array.Empty<string>();
        }

        try
        {
            using var document = JsonDocument.Parse(body);
            if (!document.RootElement.TryGetProperty("errors", out var errors))
            {
                return new[] { body };
            }

            if (errors.ValueKind == JsonValueKind.Array)
            {
                return errors.EnumerateArray()
                    .Select(item => item.ValueKind == JsonValueKind.String ? item.GetString() ?? item.GetRawText() : item.GetRawText())
                    .Where(s => !string.IsNullOrWhiteSpace(s))
                    .ToList()!;
            }

            if (errors.ValueKind == JsonValueKind.Object)
            {
                return errors.EnumerateObject()
                    .Select(property => $"{property.Name}: {property.Value.ToString()}")
                    .ToList();
            }

            return new[] { errors.ToString() };
        }
        catch (JsonException)
        {
            return new[] { body };
        }
    }

    private static string StripQuery(string path)
    {
        var q = path.IndexOf('?', StringComparison.Ordinal);
        return q < 0 ? path : path[..q];
    }

    private static BillingPlan MapPlan(ProductResource product)
    {
        return new BillingPlan
        {
            Id = product.Id,
            Handle = product.Handle ?? string.Empty,
            Name = product.Name ?? string.Empty,
            Description = product.Description,
            PriceInCents = product.PriceInCents,
            Price = CentsToDecimal(product.PriceInCents),
            Interval = product.Interval,
            IntervalUnit = product.IntervalUnit ?? string.Empty,
            RequiresPaymentMethod = product.RequireCreditCard
        };
    }

    private static BillingCustomer MapCustomer(CustomerResource customer)
    {
        return new BillingCustomer
        {
            Id = customer.Id,
            Email = customer.Email ?? string.Empty,
            Reference = customer.Reference,
            FirstName = customer.FirstName ?? string.Empty,
            LastName = customer.LastName ?? string.Empty
        };
    }

    private static BillingSubscription MapSubscription(SubscriptionResource subscription)
    {
        var priceInCents = subscription.ProductPriceInCents != 0
            ? subscription.ProductPriceInCents
            : subscription.Product?.PriceInCents ?? 0;

        return new BillingSubscription
        {
            Id = subscription.Id,
            State = subscription.State ?? string.Empty,
            ProductHandle = subscription.Product?.Handle ?? string.Empty,
            ProductName = subscription.Product?.Name ?? string.Empty,
            PriceInCents = priceInCents,
            Price = CentsToDecimal(priceInCents),
            NextBillingDate = subscription.NextAssessmentAt ?? subscription.CurrentPeriodEndsAt,
            Reference = subscription.Reference
        };
    }

    private static decimal CentsToDecimal(long cents) => cents / 100m;
}
