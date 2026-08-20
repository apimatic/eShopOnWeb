using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Billing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

public sealed class MaxioBillingClient : ISubscriptionBillingGateway
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly HttpClient _httpClient;
    private readonly MaxioOptions _options;

    public MaxioBillingClient(HttpClient httpClient, IOptions<MaxioOptions> options)
    {
        _httpClient = httpClient;
        _options = options.Value;
    }

    public async Task<IReadOnlyList<BillingPlan>> ListPlansAsync(CancellationToken cancellationToken)
    {
        var family = Uri.EscapeDataString(_options.ProductFamilyHandle);
        var response = await SendAsync<ProductEnvelope[]>(
            HttpMethod.Get,
            $"product_families/handle:{family}/products.json?per_page=200",
            null,
            cancellationToken);

        return response
            .Where(x => x.Product.Handle is not null)
            .Select(x => new BillingPlan(
                x.Product.Handle!,
                x.Product.Name,
                x.Product.Description,
                x.Product.PriceInCents,
                x.Product.Interval,
                x.Product.IntervalUnit,
                x.Product.RequireCreditCard))
            .ToArray();
    }

    public async Task<BillingCustomer?> FindCustomerAsync(string reference, CancellationToken cancellationToken)
    {
        var customers = await SendAsync<CustomerEnvelope[]>(
            HttpMethod.Get,
            $"customers.json?q={Uri.EscapeDataString(reference)}&per_page=200",
            null,
            cancellationToken);

        var match = customers.Select(x => x.Customer)
            .SingleOrDefault(x => string.Equals(x.Reference, reference, StringComparison.Ordinal));
        return match is null ? null : new BillingCustomer(match.Id, match.Reference!);
    }

    public async Task<BillingCustomer> CreateCustomerAsync(NewBillingCustomer customer, CancellationToken cancellationToken)
    {
        var request = new CustomerCreateEnvelope(new CustomerCreate(
            customer.FirstName,
            customer.LastName,
            customer.Email,
            customer.Reference));
        var created = await SendAsync<CustomerEnvelope>(HttpMethod.Post, "customers.json", request, cancellationToken);
        return new BillingCustomer(created.Customer.Id, created.Customer.Reference ?? customer.Reference);
    }

    public async Task<IReadOnlyList<BillingSubscription>> ListSubscriptionsAsync(
        long customerId,
        CancellationToken cancellationToken)
    {
        var response = await SendAsync<SubscriptionEnvelope[]>(
            HttpMethod.Get,
            $"customers/{customerId}/subscriptions.json",
            null,
            cancellationToken);
        return response.Select(MapSubscription)
            .Where(x => x is not null && string.Equals(
                x.ProductFamilyHandle, _options.ProductFamilyHandle, StringComparison.Ordinal))
            .Cast<BillingSubscription>()
            .ToArray();
    }

    public async Task<BillingSubscription> CreateSubscriptionAsync(
        long customerId,
        string productHandle,
        string subscriptionReference,
        CancellationToken cancellationToken)
    {
        var site = await SendAsync<SiteEnvelope>(HttpMethod.Get, "site.json", null, cancellationToken);
        var collectionMethod = site.Site.RelationshipInvoicingEnabled ? "remittance" : "invoice";
        var request = new SubscriptionCreateEnvelope(
            new SubscriptionCreate(productHandle, customerId, subscriptionReference, collectionMethod));
        var created = await SendAsync<SubscriptionEnvelope>(
            HttpMethod.Post,
            "subscriptions.json",
            request,
            cancellationToken);
        return MapSubscription(created) ?? throw new BillingProviderException(
            "Maxio returned a subscription without product details.");
    }

    private async Task<T> SendAsync<T>(
        HttpMethod method,
        string path,
        object? body,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, path);
        if (body is not null)
        {
            request.Content = JsonContent.Create(body, options: JsonOptions);
        }

        HttpResponseMessage response;
        try
        {
            response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new BillingProviderException("Maxio Billing did not respond before the request timed out.");
        }
        catch (HttpRequestException ex)
        {
            throw new BillingProviderException("Maxio Billing could not be reached.", innerException: ex);
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                var detail = await ReadErrorAsync(response, cancellationToken);
                throw new BillingProviderException(
                    $"Maxio Billing rejected the request ({(int)response.StatusCode}{detail}).",
                    (int)response.StatusCode);
            }

            var result = await response.Content.ReadFromJsonAsync<T>(JsonOptions, cancellationToken);
            return result ?? throw new BillingProviderException("Maxio Billing returned an empty response.");
        }
    }

    private static async Task<string> ReadErrorAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.Content.Headers.ContentLength == 0)
        {
            return string.Empty;
        }

        try
        {
            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            if (!document.RootElement.TryGetProperty("errors", out var errors))
            {
                return string.Empty;
            }

            var text = errors.ValueKind == JsonValueKind.String ? errors.GetString() : errors.ToString();
            if (string.IsNullOrWhiteSpace(text))
            {
                return string.Empty;
            }

            text = text.Replace('\r', ' ').Replace('\n', ' ');
            return $": {(text.Length <= 300 ? text : text[..300])}";
        }
        catch (JsonException)
        {
            return string.Empty;
        }
    }

    private static BillingSubscription? MapSubscription(SubscriptionEnvelope envelope)
    {
        var subscription = envelope.Subscription;
        var product = subscription.Product;
        if (product?.Handle is null || product.ProductFamily?.Handle is null)
        {
            return null;
        }

        return new BillingSubscription(
            subscription.Id,
            subscription.Reference,
            subscription.State,
            product.Handle,
            product.Name,
            product.ProductFamily.Handle,
            subscription.ProductPriceInCents,
            product.Interval,
            product.IntervalUnit,
            subscription.Currency,
            subscription.CurrentPeriodEndsAt,
            subscription.NextAssessmentAt);
    }

    private sealed record ProductEnvelope([property: JsonPropertyName("product")] Product Product);

    private sealed record Product(
        [property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("handle")] string? Handle,
        [property: JsonPropertyName("description")] string? Description,
        [property: JsonPropertyName("price_in_cents")] long PriceInCents,
        [property: JsonPropertyName("interval")] int Interval,
        [property: JsonPropertyName("interval_unit")] string IntervalUnit,
        [property: JsonPropertyName("require_credit_card")] bool RequireCreditCard,
        [property: JsonPropertyName("product_family")] ProductFamily? ProductFamily);

    private sealed record ProductFamily([property: JsonPropertyName("handle")] string Handle);

    private sealed record CustomerEnvelope([property: JsonPropertyName("customer")] Customer Customer);

    private sealed record Customer(
        [property: JsonPropertyName("id")] long Id,
        [property: JsonPropertyName("reference")] string? Reference);

    private sealed record CustomerCreateEnvelope([property: JsonPropertyName("customer")] CustomerCreate Customer);

    private sealed record CustomerCreate(
        [property: JsonPropertyName("first_name")] string FirstName,
        [property: JsonPropertyName("last_name")] string LastName,
        [property: JsonPropertyName("email")] string Email,
        [property: JsonPropertyName("reference")] string Reference);

    private sealed record SubscriptionEnvelope(
        [property: JsonPropertyName("subscription")] Subscription Subscription);

    private sealed record Subscription(
        [property: JsonPropertyName("id")] long Id,
        [property: JsonPropertyName("reference")] string? Reference,
        [property: JsonPropertyName("state")] string State,
        [property: JsonPropertyName("product_price_in_cents")] long ProductPriceInCents,
        [property: JsonPropertyName("current_period_ends_at")] DateTimeOffset? CurrentPeriodEndsAt,
        [property: JsonPropertyName("next_assessment_at")] DateTimeOffset? NextAssessmentAt,
        [property: JsonPropertyName("currency")] string? Currency,
        [property: JsonPropertyName("product")] Product? Product);

    private sealed record SubscriptionCreateEnvelope(
        [property: JsonPropertyName("subscription")] SubscriptionCreate Subscription);

    private sealed record SubscriptionCreate(
        [property: JsonPropertyName("product_handle")] string ProductHandle,
        [property: JsonPropertyName("customer_id")] long CustomerId,
        [property: JsonPropertyName("reference")] string Reference,
        [property: JsonPropertyName("payment_collection_method")] string PaymentCollectionMethod);

    private sealed record SiteEnvelope([property: JsonPropertyName("site")] Site Site);

    private sealed record Site(
        [property: JsonPropertyName("relationship_invoicing_enabled")] bool RelationshipInvoicingEnabled);
}
