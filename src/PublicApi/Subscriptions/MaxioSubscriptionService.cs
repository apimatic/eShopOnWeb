using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.Infrastructure.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

/// <summary>
/// Server-side adapter for the documented Maxio Advanced Billing customer, product,
/// and subscription endpoints. It deliberately exposes only eShop's subscription view.
/// </summary>
public class MaxioSubscriptionService : ISubscriptionService
{
    private const string HttpClientName = "MaxioAdvancedBilling";
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> EnrollmentLocks = new();
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly MaxioOptions _options;
    private readonly ILogger<MaxioSubscriptionService> _logger;

    public MaxioSubscriptionService(
        IHttpClientFactory httpClientFactory,
        IServiceScopeFactory scopeFactory,
        IOptions<MaxioOptions> options,
        ILogger<MaxioSubscriptionService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _scopeFactory = scopeFactory;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<IReadOnlyList<SubscriptionPlanDto>> GetPlansAsync(CancellationToken cancellationToken)
    {
        var family = Uri.EscapeDataString(_options.ProductFamilyHandle);
        using var document = await GetJsonAsync($"product_families/handle:{family}/products.json?per_page=200", cancellationToken);
        return ExtractWrappedArray<MaxioProduct>(document.RootElement, "product")
            .Where(product => !product.ArchivedAt.HasValue && !string.IsNullOrWhiteSpace(product.Handle))
            .Select(MapPlan)
            .OrderBy(plan => plan.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public async Task<IReadOnlyList<SubscriptionDto>> GetMySubscriptionsAsync(ApplicationUser user, CancellationToken cancellationToken)
    {
        var customer = await FindCustomerAsync(CustomerReference(user.Id), cancellationToken);
        if (customer == null)
        {
            return Array.Empty<SubscriptionDto>();
        }

        var subscriptions = await GetCustomerSubscriptionsAsync(customer.Id, cancellationToken);
        return subscriptions
            .Select(subscription => MapSubscription(subscription, customer.Id))
            .OrderByDescending(subscription => subscription.NextBillingAt)
            .ToList();
    }

    public async Task<SubscriptionDto> SubscribeAsync(ApplicationUser user, string planHandle, CancellationToken cancellationToken)
    {
        var normalizedPlanHandle = planHandle?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(normalizedPlanHandle))
        {
            throw new SubscriptionPlanNotFoundException();
        }

        var availablePlans = await GetPlansAsync(cancellationToken);
        if (!availablePlans.Any(plan => string.Equals(plan.Handle, normalizedPlanHandle, StringComparison.Ordinal)))
        {
            throw new SubscriptionPlanNotFoundException();
        }

        var lockKey = $"{user.Id}:{normalizedPlanHandle}";
        var enrollmentLock = EnrollmentLocks.GetOrAdd(lockKey, _ => new SemaphoreSlim(1, 1));
        await enrollmentLock.WaitAsync(cancellationToken);
        try
        {
            var enrollment = await GetOrCreateEnrollmentAsync(user.Id, normalizedPlanHandle, cancellationToken);
            var customer = await EnsureCustomerAsync(user, enrollment.CustomerReference, cancellationToken);
            await RecordCustomerAsync(enrollment.Id, customer.Id, cancellationToken);

            var existing = await FindPlanSubscriptionAsync(customer.Id, normalizedPlanHandle, cancellationToken);
            if (existing != null)
            {
                await CompleteEnrollmentAsync(enrollment.Id, customer.Id, existing.Id, cancellationToken);
                return MapSubscription(existing, customer.Id);
            }

            // Maxio's documented duplicate-prevention window is 60 minutes. Do not reissue
            // an ambiguous operation after that window; an operator can resolve it safely.
            if (enrollment.AttemptedAtUtc.HasValue &&
                enrollment.AttemptedAtUtc.Value < DateTimeOffset.UtcNow.AddMinutes(-55))
            {
                throw new SubscriptionEnrollmentInProgressException();
            }

            await RecordAttemptAsync(enrollment.Id, cancellationToken);
            try
            {
                var created = await CreateSubscriptionAsync(customer.Id, normalizedPlanHandle, enrollment, cancellationToken);
                await CompleteEnrollmentAsync(enrollment.Id, customer.Id, created.Id, cancellationToken);
                return MapSubscription(created, customer.Id);
            }
            catch (MaxioApiException exception) when (exception.StatusCode == HttpStatusCode.Conflict)
            {
                // A duplicate-prevention response means the first request may have succeeded.
                // Confirm against Maxio before reporting an in-progress enrollment.
                var confirmed = await FindPlanSubscriptionAsync(customer.Id, normalizedPlanHandle, cancellationToken);
                if (confirmed != null)
                {
                    await CompleteEnrollmentAsync(enrollment.Id, customer.Id, confirmed.Id, cancellationToken);
                    return MapSubscription(confirmed, customer.Id);
                }

                throw new SubscriptionEnrollmentInProgressException();
            }
        }
        finally
        {
            enrollmentLock.Release();
        }
    }

    private async Task<MaxioCustomer> EnsureCustomerAsync(ApplicationUser user, string customerReference, CancellationToken cancellationToken)
    {
        var existing = await FindCustomerAsync(customerReference, cancellationToken);
        if (existing != null)
        {
            return existing;
        }

        if (string.IsNullOrWhiteSpace(user.Email))
        {
            throw new SubscriptionUserProfileException();
        }

        var (firstName, lastName) = BuildCustomerName(user.UserName);
        var request = new
        {
            customer = new
            {
                first_name = firstName,
                last_name = lastName,
                email = user.Email,
                reference = customerReference
            }
        };

        try
        {
            using var document = await PostJsonAsync("customers.json", request, cancellationToken);
            return ExtractWrappedObject<MaxioCustomer>(document.RootElement, "customer");
        }
        catch (MaxioApiException exception) when (exception.StatusCode == HttpStatusCode.UnprocessableEntity || exception.StatusCode == HttpStatusCode.Conflict)
        {
            // A concurrent request can win the unique customer-reference race.
            var customer = await FindCustomerAsync(customerReference, cancellationToken);
            if (customer != null)
            {
                return customer;
            }

            throw;
        }
    }

    private async Task<MaxioCustomer?> FindCustomerAsync(string customerReference, CancellationToken cancellationToken)
    {
        try
        {
            using var document = await GetJsonAsync($"customers/lookup.json?reference={Uri.EscapeDataString(customerReference)}", cancellationToken);
            return ExtractWrappedObject<MaxioCustomer>(document.RootElement, "customer");
        }
        catch (MaxioApiException exception) when (exception.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    private async Task<MaxioSubscription?> FindPlanSubscriptionAsync(int customerId, string planHandle, CancellationToken cancellationToken)
    {
        var subscriptions = await GetCustomerSubscriptionsAsync(customerId, cancellationToken);
        return subscriptions.FirstOrDefault(subscription =>
            string.Equals(subscription.Product?.Handle, planHandle, StringComparison.Ordinal));
    }

    private async Task<IReadOnlyList<MaxioSubscription>> GetCustomerSubscriptionsAsync(int customerId, CancellationToken cancellationToken)
    {
        using var document = await GetJsonAsync($"customers/{customerId}/subscriptions.json", cancellationToken);
        return ExtractWrappedArray<MaxioSubscription>(document.RootElement, "subscription");
    }

    private async Task<MaxioSubscription> CreateSubscriptionAsync(
        int customerId,
        string planHandle,
        MaxioSubscriptionEnrollment enrollment,
        CancellationToken cancellationToken)
    {
        var request = new
        {
            subscription = new
            {
                customer_id = customerId,
                product_handle = planHandle,
                reference = enrollment.SubscriptionReference,
                // The seeded plans do not require a payment method. Remittance keeps
                // signup card-free while Maxio remains responsible for billing.
                payment_collection_method = "remittance"
            },
            uniqueness_token = enrollment.UniquenessToken
        };

        using var document = await PostJsonAsync("subscriptions.json", request, cancellationToken);
        return ExtractWrappedObject<MaxioSubscription>(document.RootElement, "subscription");
    }

    private async Task<MaxioSubscriptionEnrollment> GetOrCreateEnrollmentAsync(string userId, string planHandle, CancellationToken cancellationToken)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<AppIdentityDbContext>();
        var enrollment = await context.MaxioSubscriptionEnrollments
            .SingleOrDefaultAsync(item => item.UserId == userId && item.PlanHandle == planHandle, cancellationToken);
        if (enrollment != null)
        {
            return enrollment;
        }

        var now = DateTimeOffset.UtcNow;
        enrollment = new MaxioSubscriptionEnrollment
        {
            UserId = userId,
            PlanHandle = planHandle,
            CustomerReference = CustomerReference(userId),
            SubscriptionReference = SubscriptionReference(userId, planHandle),
            UniquenessToken = Guid.NewGuid().ToString("N"),
            Status = "Pending",
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };
        context.MaxioSubscriptionEnrollments.Add(enrollment);

        try
        {
            await context.SaveChangesAsync(cancellationToken);
            return enrollment;
        }
        catch (DbUpdateException)
        {
            // The SQL unique index protects the same enrollment across API instances.
            context.ChangeTracker.Clear();
            return await context.MaxioSubscriptionEnrollments.SingleAsync(
                item => item.UserId == userId && item.PlanHandle == planHandle,
                cancellationToken);
        }
    }

    private Task RecordCustomerAsync(int enrollmentId, int customerId, CancellationToken cancellationToken)
    {
        return UpdateEnrollmentAsync(enrollmentId, enrollment => enrollment.MaxioCustomerId = customerId, cancellationToken);
    }

    private Task RecordAttemptAsync(int enrollmentId, CancellationToken cancellationToken)
    {
        return UpdateEnrollmentAsync(enrollmentId, enrollment => enrollment.AttemptedAtUtc = DateTimeOffset.UtcNow, cancellationToken);
    }

    private Task CompleteEnrollmentAsync(int enrollmentId, int customerId, int subscriptionId, CancellationToken cancellationToken)
    {
        return UpdateEnrollmentAsync(enrollmentId, enrollment =>
        {
            enrollment.MaxioCustomerId = customerId;
            enrollment.MaxioSubscriptionId = subscriptionId;
            enrollment.Status = "Completed";
        }, cancellationToken);
    }

    private async Task UpdateEnrollmentAsync(int enrollmentId, Action<MaxioSubscriptionEnrollment> update, CancellationToken cancellationToken)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<AppIdentityDbContext>();
        var enrollment = await context.MaxioSubscriptionEnrollments.SingleAsync(item => item.Id == enrollmentId, cancellationToken);
        update(enrollment);
        enrollment.UpdatedAtUtc = DateTimeOffset.UtcNow;
        await context.SaveChangesAsync(cancellationToken);
    }

    private async Task<JsonDocument> GetJsonAsync(string relativeUrl, CancellationToken cancellationToken)
    {
        using var request = CreateRequest(HttpMethod.Get, relativeUrl);
        return await SendAsync(request, cancellationToken);
    }

    private async Task<JsonDocument> PostJsonAsync(string relativeUrl, object payload, CancellationToken cancellationToken)
    {
        using var request = CreateRequest(HttpMethod.Post, relativeUrl);
        request.Content = new StringContent(JsonSerializer.Serialize(payload, JsonOptions), Encoding.UTF8, "application/json");
        return await SendAsync(request, cancellationToken);
    }

    private HttpRequestMessage CreateRequest(HttpMethod method, string relativeUrl)
    {
        var request = new HttpRequestMessage(method, relativeUrl);
        var credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{_options.ApiKey}:x"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", credentials);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        return request;
    }

    private async Task<JsonDocument> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        try
        {
            var client = _httpClientFactory.CreateClient(HttpClientName);
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Maxio Advanced Billing returned HTTP {StatusCode} for {Method} {Path}.", (int)response.StatusCode, request.Method, request.RequestUri?.AbsolutePath);
                throw new MaxioApiException(response.StatusCode);
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            return await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        }
        catch (HttpRequestException exception)
        {
            _logger.LogWarning(exception, "The Maxio Advanced Billing request failed before a response was received.");
            throw new MaxioApiException(HttpStatusCode.BadGateway);
        }
    }

    private static T ExtractWrappedObject<T>(JsonElement root, string propertyName)
    {
        var value = root.ValueKind == JsonValueKind.Object && root.TryGetProperty(propertyName, out var wrapped)
            ? wrapped
            : root;
        return value.Deserialize<T>(JsonOptions) ?? throw new InvalidOperationException("Maxio returned an unexpected response.");
    }

    private static List<T> ExtractWrappedArray<T>(JsonElement root, string itemPropertyName)
    {
        var array = root;
        if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("items", out var items))
        {
            array = items;
        }

        if (array.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidOperationException("Maxio returned an unexpected list response.");
        }

        var result = new List<T>();
        foreach (var item in array.EnumerateArray())
        {
            var value = item.ValueKind == JsonValueKind.Object && item.TryGetProperty(itemPropertyName, out var wrapped)
                ? wrapped
                : item;
            var deserialized = value.Deserialize<T>(JsonOptions);
            if (deserialized != null)
            {
                result.Add(deserialized);
            }
        }

        return result;
    }

    private static SubscriptionPlanDto MapPlan(MaxioProduct product)
    {
        return new SubscriptionPlanDto
        {
            Handle = product.Handle ?? string.Empty,
            Name = product.Name ?? string.Empty,
            Description = product.Description,
            PriceInCents = product.PriceInCents,
            Price = product.PriceInCents / 100m,
            Interval = product.Interval,
            IntervalUnit = product.IntervalUnit ?? string.Empty
        };
    }

    private static SubscriptionDto MapSubscription(MaxioSubscription subscription, int customerId)
    {
        var priceInCents = subscription.ProductPriceInCents ?? subscription.Product?.PriceInCents ?? 0;
        return new SubscriptionDto
        {
            Id = subscription.Id,
            CustomerId = customerId,
            PlanHandle = subscription.Product?.Handle ?? string.Empty,
            PlanName = subscription.Product?.Name ?? string.Empty,
            PriceInCents = priceInCents,
            Price = priceInCents / 100m,
            State = subscription.State ?? string.Empty,
            NextBillingAt = subscription.NextAssessmentAt
        };
    }

    private static string CustomerReference(string userId) => $"eshop-user-{Hash(userId)}";

    private static string SubscriptionReference(string userId, string planHandle) => $"eshop-subscription-{Hash($"{userId}:{planHandle}")}";

    private static string Hash(string value)
    {
        var bytes = System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private static (string FirstName, string LastName) BuildCustomerName(string? userName)
    {
        var localPart = userName?.Split('@')[0] ?? string.Empty;
        var names = localPart.Split(new[] { '.', '-', '_', ' ' }, StringSplitOptions.RemoveEmptyEntries);
        var firstName = names.Length > 0 ? names[0] : "Shopper";
        var lastName = names.Length > 1 ? names[^1] : "User";
        return (firstName, lastName);
    }

    private sealed class MaxioCustomer
    {
        public int Id { get; set; }
    }

    private sealed class MaxioProduct
    {
        public string? Handle { get; set; }

        public string? Name { get; set; }

        public string? Description { get; set; }

        [JsonPropertyName("price_in_cents")]
        public long PriceInCents { get; set; }

        public int Interval { get; set; }

        [JsonPropertyName("interval_unit")]
        public string? IntervalUnit { get; set; }

        [JsonPropertyName("archived_at")]
        public DateTimeOffset? ArchivedAt { get; set; }
    }

    private sealed class MaxioSubscription
    {
        public int Id { get; set; }

        public string? State { get; set; }

        public MaxioProduct? Product { get; set; }

        [JsonPropertyName("product_price_in_cents")]
        public long? ProductPriceInCents { get; set; }

        [JsonPropertyName("next_assessment_at")]
        public DateTimeOffset? NextAssessmentAt { get; set; }
    }
}
