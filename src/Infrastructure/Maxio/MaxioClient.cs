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
using Microsoft.eShopWeb.Infrastructure.Maxio.Wire;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Hand-written client for the subset of the Maxio Advanced Billing API used by eShopOnWeb,
/// built directly against the endpoints/schemas declared in maxio-spec/openapi.yaml. The
/// HttpClient injected here is expected to already carry the site's base address and Basic
/// Auth credentials (see Infrastructure/Dependencies.cs).
/// </summary>
public class MaxioClient : IMaxioClient
{
    private const int MaxPerPage = 200;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _httpClient;

    public MaxioClient(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<IReadOnlyList<MaxioProduct>> ListProductsForFamilyAsync(string productFamilyHandle, CancellationToken cancellationToken = default)
    {
        var results = new List<MaxioProduct>();
        var page = 1;
        while (true)
        {
            var path = $"product_families/handle:{Uri.EscapeDataString(productFamilyHandle)}/products.json?page={page}&per_page={MaxPerPage}";
            using var response = await _httpClient.GetAsync(path, cancellationToken);
            await EnsureSuccessAsync(response, cancellationToken);

            var wireItems = await response.Content.ReadFromJsonAsync<List<ProductResponseWire>>(JsonOptions, cancellationToken) ?? [];
            if (wireItems.Count == 0)
            {
                break;
            }

            results.AddRange(wireItems.Where(w => w.Product is not null).Select(w => ToProduct(w.Product!)));
            if (wireItems.Count < MaxPerPage)
            {
                break;
            }

            page++;
        }

        return results;
    }

    public async Task<MaxioCustomer?> FindCustomerByReferenceAsync(string reference, CancellationToken cancellationToken = default)
    {
        var path = $"customers/lookup.json?reference={Uri.EscapeDataString(reference)}";
        using var response = await _httpClient.GetAsync(path, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        await EnsureSuccessAsync(response, cancellationToken);

        var wire = await response.Content.ReadFromJsonAsync<CustomerResponseWire>(JsonOptions, cancellationToken);
        return wire?.Customer is null ? null : ToCustomer(wire.Customer);
    }

    public async Task<MaxioCustomer> CreateCustomerAsync(MaxioCreateCustomer customer, CancellationToken cancellationToken = default)
    {
        var request = new CreateCustomerRequestWire
        {
            Customer = new CreateCustomerWire
            {
                FirstName = customer.FirstName,
                LastName = customer.LastName,
                Email = customer.Email,
                Reference = customer.Reference
            }
        };

        using var response = await _httpClient.PostAsJsonAsync("customers.json", request, JsonOptions, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);

        var wire = await response.Content.ReadFromJsonAsync<CustomerResponseWire>(JsonOptions, cancellationToken);
        if (wire?.Customer is null)
        {
            throw new MaxioApiException(response.StatusCode, "Maxio create-customer response did not include a customer.");
        }

        return ToCustomer(wire.Customer);
    }

    public async Task<IReadOnlyList<MaxioSubscription>> ListCustomerSubscriptionsAsync(int customerId, CancellationToken cancellationToken = default)
    {
        var results = new List<MaxioSubscription>();
        var page = 1;
        while (true)
        {
            var path = $"customers/{customerId}/subscriptions.json?page={page}&per_page={MaxPerPage}";
            using var response = await _httpClient.GetAsync(path, cancellationToken);
            await EnsureSuccessAsync(response, cancellationToken);

            var wireItems = await response.Content.ReadFromJsonAsync<List<SubscriptionResponseWire>>(JsonOptions, cancellationToken) ?? [];
            if (wireItems.Count == 0)
            {
                break;
            }

            results.AddRange(wireItems.Where(w => w.Subscription is not null).Select(w => ToSubscription(w.Subscription!)));
            if (wireItems.Count < MaxPerPage)
            {
                break;
            }

            page++;
        }

        return results;
    }

    public async Task<MaxioSubscription> CreateSubscriptionAsync(int customerId, string productHandle, CancellationToken cancellationToken = default)
    {
        var request = new CreateSubscriptionRequestWire
        {
            Subscription = new CreateSubscriptionWire { ProductHandle = productHandle, CustomerId = customerId }
        };

        using var response = await _httpClient.PostAsJsonAsync("subscriptions.json", request, JsonOptions, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);

        var wire = await response.Content.ReadFromJsonAsync<SubscriptionResponseWire>(JsonOptions, cancellationToken);
        if (wire?.Subscription is null)
        {
            throw new MaxioApiException(response.StatusCode, "Maxio create-subscription response did not include a subscription.");
        }

        return ToSubscription(wire.Subscription);
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        throw new MaxioApiException(response.StatusCode, MaxioErrorParser.ExtractMessage(body));
    }

    private static MaxioCustomer ToCustomer(CustomerWire wire) => new(wire.Id, wire.Reference, wire.Email);

    private static MaxioProduct ToProduct(ProductWire wire) => new(
        wire.Id, wire.Handle ?? string.Empty, wire.Name, wire.Description, wire.PriceInCents, wire.Interval, wire.IntervalUnit, wire.ArchivedAt);

    private static MaxioSubscription ToSubscription(SubscriptionWire wire) => new(
        wire.Id,
        wire.State,
        wire.Customer?.Id ?? 0,
        wire.Product?.Id ?? 0,
        wire.Product?.Handle ?? string.Empty,
        wire.Product?.Name ?? string.Empty,
        wire.Product?.PriceInCents ?? 0,
        wire.CurrentPeriodEndsAt,
        wire.NextAssessmentAt,
        wire.CreatedAt);
}
