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
using Microsoft.eShopWeb.Infrastructure.Identity;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

/// <summary>
/// Maxio Advanced Billing client. Every route and JSON member in this class is defined by maxio-spec/openapi.yaml.
/// </summary>
public sealed class MaxioSubscriptionService : IMaxioSubscriptionService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly HttpClient _httpClient;
    private readonly MaxioOptions _options;

    public MaxioSubscriptionService(HttpClient httpClient, IOptions<MaxioOptions> options)
    {
        _httpClient = httpClient;
        _options = options.Value;

        var baseUrl = string.IsNullOrWhiteSpace(_options.BaseUrl)
            ? $"https://{_options.Subdomain}.chargify.com"
            : _options.BaseUrl;

        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var baseUri))
        {
            throw new OptionsValidationException(MaxioOptions.SectionName, typeof(MaxioOptions), new[] { "BaseUrl must be an absolute URI." });
        }

        _httpClient.BaseAddress = baseUri;
        _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        var credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{_options.ApiKey}:x"));
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", credentials);
    }

    public async Task<IReadOnlyList<SubscriptionPlan>> ListPlansAsync(CancellationToken cancellationToken)
    {
        // listProductsForProductFamily accepts a handle only in the documented "handle:{handle}" path form.
        var family = Uri.EscapeDataString($"handle:{_options.ProductFamilyHandle}");
        using var response = await _httpClient.GetAsync($"/product_families/{family}/products.json", cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);

        var products = await response.Content.ReadFromJsonAsync<List<MaxioProductResponse>>(JsonOptions, cancellationToken)
            ?? new List<MaxioProductResponse>();

        return products
            .Select(x => x.Product)
            .Where(x => x is not null && x.ArchivedAt is null && !string.IsNullOrWhiteSpace(x.Handle) && !string.IsNullOrWhiteSpace(x.Name) && !string.IsNullOrWhiteSpace(x.IntervalUnit))
            .Select(x => new SubscriptionPlan(x!.Handle!, x.Name!, x.Description, x.PriceInCents, x.Interval, x.IntervalUnit!))
            .OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public async Task<SubscriptionDetails> SubscribeAsync(ApplicationUser user, string planHandle, CancellationToken cancellationToken)
    {
        var plans = await ListPlansAsync(cancellationToken);
        var plan = plans.SingleOrDefault(x => string.Equals(x.Handle, planHandle, StringComparison.Ordinal));
        if (plan is null)
        {
            throw new SubscriptionPlanNotFoundException();
        }

        var customer = await EnsureCustomerAsync(user, cancellationToken);
        var reference = SubscriptionReference(user.Id, plan.Handle);
        var existing = await FindSubscriptionByReferenceAsync(reference, cancellationToken);
        if (existing is not null)
        {
            return ToDetails(existing);
        }

        try
        {
            using var response = await _httpClient.PostAsJsonAsync("/subscriptions.json", new MaxioCreateSubscriptionRequest
            {
                Subscription = new MaxioCreateSubscription
                {
                    ProductHandle = plan.Handle,
                    CustomerId = customer.Id,
                    Reference = reference,
                    // Collection-Method.yaml permits remittance and the seeded plans are deliberately cardless.
                    PaymentCollectionMethod = "remittance"
                }
            }, JsonOptions, cancellationToken);

            await EnsureSuccessAsync(response, cancellationToken, HttpStatusCode.Created);
            var created = await response.Content.ReadFromJsonAsync<MaxioSubscriptionResponse>(JsonOptions, cancellationToken);
            return ToDetails(created?.Subscription ?? throw new MaxioApiException((int)response.StatusCode));
        }
        catch (MaxioApiException)
        {
            // A duplicate reference can be returned when two API instances receive the same click.
            // Looking it up after an unsuccessful create makes the operation safe to retry.
            var createdByConcurrentRequest = await FindSubscriptionByReferenceAsync(reference, cancellationToken);
            if (createdByConcurrentRequest is not null)
            {
                return ToDetails(createdByConcurrentRequest);
            }

            throw;
        }
    }

    public async Task<IReadOnlyList<SubscriptionDetails>> GetSubscriptionsAsync(ApplicationUser user, CancellationToken cancellationToken)
    {
        var customer = await GetCustomerByReferenceAsync(CustomerReference(user.Id), cancellationToken);
        if (customer is null)
        {
            return Array.Empty<SubscriptionDetails>();
        }

        using var response = await _httpClient.GetAsync($"/customers/{customer.Id}/subscriptions.json", cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        var subscriptions = await response.Content.ReadFromJsonAsync<List<MaxioSubscriptionResponse>>(JsonOptions, cancellationToken)
            ?? new List<MaxioSubscriptionResponse>();

        return subscriptions
            .Select(x => x.Subscription)
            .Where(x => x is not null)
            .Select(x => ToDetails(x!))
            .ToList();
    }

    private async Task<MaxioCustomer> EnsureCustomerAsync(ApplicationUser user, CancellationToken cancellationToken)
    {
        var reference = CustomerReference(user.Id);
        var existing = await GetCustomerByReferenceAsync(reference, cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        var (firstName, lastName) = NameFor(user.UserName);
        if (string.IsNullOrWhiteSpace(user.Email))
        {
            throw new InvalidOperationException("The signed-in user must have an email address before subscribing.");
        }

        try
        {
            using var response = await _httpClient.PostAsJsonAsync("/customers.json", new MaxioCreateCustomerRequest
            {
                Customer = new MaxioCreateCustomer
                {
                    FirstName = firstName,
                    LastName = lastName,
                    Email = user.Email,
                    Reference = reference
                }
            }, JsonOptions, cancellationToken);

            await EnsureSuccessAsync(response, cancellationToken);
            var created = await response.Content.ReadFromJsonAsync<MaxioCustomerResponse>(JsonOptions, cancellationToken);
            return created?.Customer ?? throw new MaxioApiException((int)response.StatusCode);
        }
        catch (MaxioApiException)
        {
            // Customer references are unique in Maxio. Re-read after a racing create before surfacing an error.
            var createdByConcurrentRequest = await GetCustomerByReferenceAsync(reference, cancellationToken);
            if (createdByConcurrentRequest is not null)
            {
                return createdByConcurrentRequest;
            }

            throw;
        }
    }

    private async Task<MaxioCustomer?> GetCustomerByReferenceAsync(string reference, CancellationToken cancellationToken)
    {
        using var response = await _httpClient.GetAsync($"/customers/lookup.json?reference={Uri.EscapeDataString(reference)}", cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        await EnsureSuccessAsync(response, cancellationToken);
        return (await response.Content.ReadFromJsonAsync<MaxioCustomerResponse>(JsonOptions, cancellationToken))?.Customer;
    }

    private async Task<MaxioSubscription?> FindSubscriptionByReferenceAsync(string reference, CancellationToken cancellationToken)
    {
        using var response = await _httpClient.GetAsync($"/subscriptions/lookup.json?reference={Uri.EscapeDataString(reference)}", cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        await EnsureSuccessAsync(response, cancellationToken);
        return (await response.Content.ReadFromJsonAsync<MaxioSubscriptionResponse>(JsonOptions, cancellationToken))?.Subscription;
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken cancellationToken, HttpStatusCode? expectedStatus = null)
    {
        if (response.IsSuccessStatusCode && (expectedStatus is null || response.StatusCode == expectedStatus))
        {
            return;
        }

        // Consume the body so the connection can be reused. Maxio error models are deliberately not relayed to callers.
        await response.Content.LoadIntoBufferAsync();
        throw new MaxioApiException((int)response.StatusCode);
    }

    private static SubscriptionDetails ToDetails(MaxioSubscription subscription) => new(
        subscription.Id,
        subscription.Product?.Handle ?? string.Empty,
        subscription.Product?.Name ?? string.Empty,
        subscription.ProductPriceInCents,
        subscription.State ?? string.Empty,
        subscription.NextAssessmentAt ?? subscription.CurrentPeriodEndsAt);

    private static string CustomerReference(string userId) => $"eshoponweb:{userId}";
    private static string SubscriptionReference(string userId, string planHandle) => $"eshoponweb:{userId}:{planHandle}";

    private static (string FirstName, string LastName) NameFor(string? userName)
    {
        var localPart = (userName ?? "Shopper").Split('@')[0];
        var parts = localPart.Split(new[] { '.', '-', '_' }, StringSplitOptions.RemoveEmptyEntries);
        return parts.Length switch
        {
            0 => ("Shopper", "User"),
            1 => (parts[0], "Shopper"),
            _ => (parts[0], string.Join(" ", parts.Skip(1)))
        };
    }
}
