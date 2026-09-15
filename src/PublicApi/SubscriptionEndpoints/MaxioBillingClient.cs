using System;
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
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.Infrastructure.Identity;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public interface IMaxioBillingClient
{
    Task<IReadOnlyList<SubscriptionPlanDto>> ListPlansAsync(CancellationToken cancellationToken);
    Task<CreateSubscriptionResponse> SubscribeAsync(ApplicationUser user, string planHandle, CancellationToken cancellationToken);
    Task<IReadOnlyList<SubscriptionDto>> ListMySubscriptionsAsync(ApplicationUser user, CancellationToken cancellationToken);
}

public sealed class MaxioConfigurationException : Exception
{
    public MaxioConfigurationException() : base("Maxio billing has not been configured for this API.") { }
}

public sealed class MaxioApiException : Exception
{
    public MaxioApiException(HttpStatusCode statusCode)
        : base($"Maxio Billing API request failed with status {(int)statusCode}.") => StatusCode = statusCode;

    public HttpStatusCode StatusCode { get; }
}

public sealed class SubscriptionEnrollmentInProgressException : Exception
{
    public SubscriptionEnrollmentInProgressException()
        : base("An enrollment for this plan is already being processed. Please refresh your subscriptions shortly.") { }
}

public sealed class MaxioBillingClient : IMaxioBillingClient
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);
    private static readonly HashSet<string> EndedStates = new(StringComparer.OrdinalIgnoreCase)
    {
        "canceled", "expired", "failed_to_create", "trial_ended", "on_hold"
    };

    private readonly HttpClient _httpClient;
    private readonly MaxioOptions _options;
    private readonly SubscriptionEnrollmentLock _enrollmentLock;
    private readonly AppIdentityDbContext _identityDbContext;

    public MaxioBillingClient(HttpClient httpClient, IOptions<MaxioOptions> options, SubscriptionEnrollmentLock enrollmentLock,
        AppIdentityDbContext identityDbContext)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _enrollmentLock = enrollmentLock;
        _identityDbContext = identityDbContext;
    }

    public async Task<IReadOnlyList<SubscriptionPlanDto>> ListPlansAsync(CancellationToken cancellationToken)
    {
        var familyHandle = RequireProductFamilyHandle();
        var response = await SendAsync(HttpMethod.Get,
            $"product_families/handle:{Uri.EscapeDataString(familyHandle)}/products.json?page=1&per_page=200",
            null, cancellationToken);
        var products = await ReadAsync<List<MaxioProductEnvelope>>(response, cancellationToken);

        return products
            .Select(item => item.Product)
            .Where(product => !string.IsNullOrWhiteSpace(product.Handle) && product.ArchivedAt is null)
            .Select(product => new SubscriptionPlanDto
            {
                Handle = product.Handle!,
                Name = product.Name,
                Description = product.Description,
                PriceInCents = product.PriceInCents,
                Interval = product.Interval,
                IntervalUnit = product.IntervalUnit,
                RequiresPaymentMethod = product.RequireCreditCard
            })
            .OrderBy(product => product.PriceInCents)
            .ToList();
    }

    public async Task<CreateSubscriptionResponse> SubscribeAsync(ApplicationUser user, string planHandle, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(planHandle))
        {
            throw new ArgumentException("A planHandle is required.", nameof(planHandle));
        }

        var selectedPlan = (await ListPlansAsync(cancellationToken))
            .SingleOrDefault(plan => string.Equals(plan.Handle, planHandle, StringComparison.Ordinal));
        if (selectedPlan is null)
        {
            throw new ArgumentException("The requested plan is not available in the configured Maxio product family.", nameof(planHandle));
        }

        var customerReference = CustomerReference(user.Id);
        using (await _enrollmentLock.AcquireAsync($"{customerReference}:{selectedPlan.Handle}", cancellationToken))
        {
            var customer = await GetOrCreateCustomerAsync(user, customerReference, cancellationToken);
            var existing = await ListCustomerSubscriptionsAsync(customer.Id, cancellationToken);
            var existingSubscription = existing.FirstOrDefault(subscription =>
                string.Equals(subscription.Product?.Handle, selectedPlan.Handle, StringComparison.Ordinal) &&
                !EndedStates.Contains(subscription.State));

            if (existingSubscription is not null)
            {
                await RecordEnrollmentAsync(user.Id, selectedPlan.Handle, customer.Id, existingSubscription.Id, cancellationToken);
                return new CreateSubscriptionResponse
                {
                    Subscription = existingSubscription.ToDto(),
                    AlreadySubscribed = true
                };
            }

            var reservation = await ReserveEnrollmentAsync(user.Id, selectedPlan.Handle, customer.Id, cancellationToken);

            MaxioSubscriptionEnvelope created;
            try
            {
                created = await PostAsync<MaxioSubscriptionEnvelope>("subscriptions.json", new
                {
                    subscription = new
                    {
                        product_handle = selectedPlan.Handle,
                        customer_id = customer.Id,
                        // The configured plans intentionally permit enrollment without card capture.
                        // Invoice collection is the documented non-automatic collection method.
                        payment_collection_method = "invoice"
                    }
                }, cancellationToken);
            }
            catch (MaxioApiException)
            {
                // A definitive API error did not create a subscription, so allow a corrected
                // request to reserve the plan again. Transport failures intentionally retain
                // the reservation because Maxio may have accepted the request.
                _identityDbContext.MaxioSubscriptionEnrollments.Remove(reservation);
                await _identityDbContext.SaveChangesAsync(cancellationToken);
                throw;
            }

            reservation.MaxioSubscriptionId = created.Subscription.Id;
            await _identityDbContext.SaveChangesAsync(cancellationToken);

            return new CreateSubscriptionResponse
            {
                Subscription = created.Subscription.ToDto(),
                AlreadySubscribed = false
            };
        }
    }

    public async Task<IReadOnlyList<SubscriptionDto>> ListMySubscriptionsAsync(ApplicationUser user, CancellationToken cancellationToken)
    {
        var customer = await TryGetCustomerByReferenceAsync(CustomerReference(user.Id), cancellationToken);
        if (customer is null)
        {
            return Array.Empty<SubscriptionDto>();
        }

        return (await ListCustomerSubscriptionsAsync(customer.Id, cancellationToken))
            .Select(MaxioSubscriptionMapper.ToDto)
            .OrderByDescending(subscription => subscription.NextBillingDate)
            .ToList();
    }

    private async Task<MaxioCustomer> GetOrCreateCustomerAsync(ApplicationUser user, string reference, CancellationToken cancellationToken)
    {
        var existing = await TryGetCustomerByReferenceAsync(reference, cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        var (firstName, lastName) = CustomerName(user);
        try
        {
            var created = await PostAsync<MaxioCustomerEnvelope>("customers.json", new
            {
                customer = new
                {
                    first_name = firstName,
                    last_name = lastName,
                    email = user.Email ?? user.UserName ?? $"{user.Id}@invalid.local",
                    reference
                }
            }, cancellationToken);
            return created.Customer;
        }
        catch (MaxioApiException exception) when (exception.StatusCode == HttpStatusCode.UnprocessableEntity)
        {
            // Customer references are unique in Maxio. A concurrent request can create it
            // between the lookup and create calls, in which case the authoritative lookup wins.
            var concurrentlyCreatedCustomer = await TryGetCustomerByReferenceAsync(reference, cancellationToken);
            if (concurrentlyCreatedCustomer is null)
            {
                throw;
            }

            return concurrentlyCreatedCustomer;
        }
    }

    private async Task<MaxioCustomer?> TryGetCustomerByReferenceAsync(string reference, CancellationToken cancellationToken)
    {
        var response = await SendAsync(HttpMethod.Get,
            $"customers/lookup.json?reference={Uri.EscapeDataString(reference)}", null, cancellationToken,
            allowNotFound: true);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        return (await ReadAsync<MaxioCustomerEnvelope>(response, cancellationToken)).Customer;
    }

    private async Task<IReadOnlyList<MaxioSubscription>> ListCustomerSubscriptionsAsync(long customerId, CancellationToken cancellationToken)
    {
        var response = await SendAsync(HttpMethod.Get, $"customers/{customerId}/subscriptions.json", null, cancellationToken);
        var subscriptions = await ReadAsync<List<MaxioSubscriptionEnvelope>>(response, cancellationToken);
        return subscriptions.Select(item => item.Subscription).ToList();
    }

    private async Task RecordEnrollmentAsync(string userId, string planHandle, long customerId, long subscriptionId,
        CancellationToken cancellationToken)
    {
        var enrollment = await _identityDbContext.MaxioSubscriptionEnrollments
            .SingleOrDefaultAsync(item => item.UserId == userId && item.PlanHandle == planHandle, cancellationToken);
        if (enrollment is null)
        {
            _identityDbContext.MaxioSubscriptionEnrollments.Add(new MaxioSubscriptionEnrollment
            {
                UserId = userId,
                PlanHandle = planHandle,
                MaxioCustomerId = customerId,
                MaxioSubscriptionId = subscriptionId,
                CreatedAt = DateTimeOffset.UtcNow
            });
        }
        else
        {
            enrollment.MaxioCustomerId = customerId;
            enrollment.MaxioSubscriptionId = subscriptionId;
        }

        try
        {
            await _identityDbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            // Another instance recorded the same authoritative Maxio subscription first.
            _identityDbContext.ChangeTracker.Clear();
        }
    }

    private async Task<MaxioSubscriptionEnrollment> ReserveEnrollmentAsync(string userId, string planHandle,
        long customerId, CancellationToken cancellationToken)
    {
        var existing = await _identityDbContext.MaxioSubscriptionEnrollments
            .SingleOrDefaultAsync(item => item.UserId == userId && item.PlanHandle == planHandle, cancellationToken);
        if (existing is not null)
        {
            if (existing.MaxioSubscriptionId is null)
            {
                throw new SubscriptionEnrollmentInProgressException();
            }

            _identityDbContext.MaxioSubscriptionEnrollments.Remove(existing);
            await _identityDbContext.SaveChangesAsync(cancellationToken);
        }

        var reservation = new MaxioSubscriptionEnrollment
        {
            UserId = userId,
            PlanHandle = planHandle,
            MaxioCustomerId = customerId,
            CreatedAt = DateTimeOffset.UtcNow
        };
        _identityDbContext.MaxioSubscriptionEnrollments.Add(reservation);
        try
        {
            await _identityDbContext.SaveChangesAsync(cancellationToken);
            return reservation;
        }
        catch (DbUpdateException)
        {
            _identityDbContext.ChangeTracker.Clear();
            throw new SubscriptionEnrollmentInProgressException();
        }
    }

    private async Task<T> PostAsync<T>(string path, object body, CancellationToken cancellationToken)
    {
        var response = await SendAsync(HttpMethod.Post, path, JsonContent.Create(body), cancellationToken);
        return await ReadAsync<T>(response, cancellationToken);
    }

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string relativePath, HttpContent? content,
        CancellationToken cancellationToken, bool allowNotFound = false)
    {
        using var request = new HttpRequestMessage(method, BuildUri(relativePath)) { Content = content };
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(
            Encoding.UTF8.GetBytes($"{RequireApiKey()}:X")));

        var response = await _httpClient.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode && !(allowNotFound && response.StatusCode == HttpStatusCode.NotFound))
        {
            response.Dispose();
            throw new MaxioApiException(response.StatusCode);
        }

        return response;
    }

    private static async Task<T> ReadAsync<T>(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        using (response)
        {
            return await response.Content.ReadFromJsonAsync<T>(SerializerOptions, cancellationToken)
                ?? throw new MaxioApiException(response.StatusCode);
        }
    }

    private Uri BuildUri(string relativePath)
    {
        var baseUrl = _options.BaseUrl;
        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            var subdomain = _options.Subdomain?.Trim();
            if (string.IsNullOrWhiteSpace(subdomain))
            {
                throw new MaxioConfigurationException();
            }

            baseUrl = $"https://{subdomain}.chargify.com";
        }

        if (!Uri.TryCreate($"{baseUrl.TrimEnd('/')}/{relativePath}", UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
        {
            throw new MaxioConfigurationException();
        }

        return uri;
    }

    private string RequireApiKey() => string.IsNullOrWhiteSpace(_options.ApiKey)
        ? throw new MaxioConfigurationException()
        : _options.ApiKey;

    private string RequireProductFamilyHandle() => string.IsNullOrWhiteSpace(_options.ProductFamilyHandle)
        ? throw new MaxioConfigurationException()
        : _options.ProductFamilyHandle;

    private static string CustomerReference(string userId) => $"eshop-user:{userId}";

    private static (string FirstName, string LastName) CustomerName(ApplicationUser user)
    {
        var localPart = (user.Email ?? user.UserName ?? "eshop").Split('@')[0].Trim();
        return (string.IsNullOrWhiteSpace(localPart) ? "eShop" : localPart, "Shopper");
    }
}

internal sealed class MaxioProductEnvelope
{
    public MaxioProduct Product { get; init; } = new();
}

internal sealed class MaxioProduct
{
    [JsonPropertyName("handle")]
    public string? Handle { get; init; }
    [JsonPropertyName("name")]
    public string Name { get; init; } = string.Empty;
    [JsonPropertyName("description")]
    public string? Description { get; init; }
    [JsonPropertyName("price_in_cents")]
    public long PriceInCents { get; init; }
    [JsonPropertyName("interval")]
    public int Interval { get; init; }
    [JsonPropertyName("interval_unit")]
    public string IntervalUnit { get; init; } = string.Empty;
    [JsonPropertyName("require_credit_card")]
    public bool RequireCreditCard { get; init; }
    [JsonPropertyName("archived_at")]
    public DateTimeOffset? ArchivedAt { get; init; }
}

internal sealed class MaxioCustomerEnvelope
{
    public MaxioCustomer Customer { get; init; } = new();
}

internal sealed class MaxioCustomer
{
    public long Id { get; init; }
}

internal sealed class MaxioSubscriptionEnvelope
{
    public MaxioSubscription Subscription { get; init; } = new();
}

internal sealed class MaxioSubscription
{
    [JsonPropertyName("id")]
    public long Id { get; init; }
    [JsonPropertyName("state")]
    public string State { get; init; } = string.Empty;
    [JsonPropertyName("product")]
    public MaxioProduct? Product { get; init; }
    [JsonPropertyName("product_price_in_cents")]
    public long ProductPriceInCents { get; init; }
    [JsonPropertyName("current_period_ends_at")]
    public DateTimeOffset? CurrentPeriodEndsAt { get; init; }
    [JsonPropertyName("next_assessment_at")]
    public DateTimeOffset? NextAssessmentAt { get; init; }
}

internal static class MaxioSubscriptionMapper
{
    public static SubscriptionDto ToDto(this MaxioSubscription subscription) => new()
    {
        Id = subscription.Id,
        PlanHandle = subscription.Product?.Handle ?? string.Empty,
        PlanName = subscription.Product?.Name ?? "Subscription",
        PriceInCents = subscription.ProductPriceInCents,
        State = subscription.State,
        NextBillingDate = subscription.NextAssessmentAt ?? subscription.CurrentPeriodEndsAt
    };
}
