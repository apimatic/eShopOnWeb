using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.Infrastructure.Identity;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// A deliberately small client for only the operations used by this application. Request paths,
/// query parameters, JSON wrappers, fields, authentication, and server template come from maxio-spec/openapi.yaml.
/// </summary>
public sealed class MaxioBillingService : IMaxioBillingService
{
    private const int PageSize = 200;
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> EnrollmentLocks = new();
    // Maxio schemas use snake_case property names (for example product_handle and price_in_cents).
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
    };

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly MaxioOptions _options;
    private readonly AppIdentityDbContext _identityContext;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly ILogger<MaxioBillingService> _logger;

    public MaxioBillingService(
        IHttpClientFactory httpClientFactory,
        IOptions<MaxioOptions> options,
        AppIdentityDbContext identityContext,
        UserManager<ApplicationUser> userManager,
        ILogger<MaxioBillingService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _options = options.Value;
        _identityContext = identityContext;
        _userManager = userManager;
        _logger = logger;
    }

    public async Task<IReadOnlyList<SubscriptionPlanDto>> GetPlansAsync(CancellationToken cancellationToken)
    {
        EnsureConfigured();
        var plans = new List<SubscriptionPlanDto>();

        for (var page = 1; ; page++)
        {
            using var response = await SendAsync(HttpMethod.Get, $"products.json?page={page}&per_page={PageSize}", null, cancellationToken);
            EnsureSuccess(response, "listing subscription plans");
            var products = await DeserializeAsync<List<MaxioProductResponse>>(response, cancellationToken) ?? new();

            plans.AddRange(products
                .Select(item => item.Product)
                .Where(product => string.Equals(product.ProductFamily?.Handle, _options.ProductFamilyHandle, StringComparison.Ordinal))
                .Where(product => product.ArchivedAt is null && !string.IsNullOrWhiteSpace(product.Handle))
                .Select(ToPlan));

            if (products.Count < PageSize)
            {
                break;
            }
        }

        return plans.OrderBy(plan => plan.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    public async Task<SubscriptionDto> SubscribeAsync(ApplicationUser user, string planHandle, CancellationToken cancellationToken)
    {
        EnsureConfigured();
        if (string.IsNullOrWhiteSpace(planHandle))
        {
            throw new ArgumentException("A planHandle is required.", nameof(planHandle));
        }

        var plan = (await GetPlansAsync(cancellationToken))
            .SingleOrDefault(candidate => string.Equals(candidate.Handle, planHandle, StringComparison.Ordinal));
        if (plan is null)
        {
            throw new ArgumentException("The requested plan is not available.", nameof(planHandle));
        }
        if (plan.RequiresPaymentMethod)
        {
            throw new ArgumentException("The requested plan requires a payment method and cannot be enrolled through this cardless endpoint.", nameof(planHandle));
        }

        var lockKey = $"{user.Id}:{plan.Handle}";
        var enrollmentLock = EnrollmentLocks.GetOrAdd(lockKey, _ => new SemaphoreSlim(1, 1));
        await enrollmentLock.WaitAsync(cancellationToken);
        try
        {
            var customer = await EnsureCustomerAsync(user, cancellationToken);
            var subscriptionReference = SubscriptionReference(user.Id, plan.Handle);
            var existingSubscription = await FindSubscriptionAsync(subscriptionReference, cancellationToken);
            if (existingSubscription is not null)
            {
                await SaveSubscriptionRecordAsync(user.Id, plan.Handle, customer.Id, existingSubscription, subscriptionReference, cancellationToken);
                return ToSubscription(existingSubscription);
            }

            var request = new CreateSubscriptionRequest
            {
                Subscription = new CreateSubscription
                {
                    ProductHandle = plan.Handle,
                    CustomerId = customer.Id,
                    Reference = subscriptionReference,
                    // The selected demo plans do not require a stored payment method. The OpenAPI
                    // Collection-Method contract defines remittance as the cardless collection mode.
                    PaymentCollectionMethod = "remittance"
                }
            };

            using var createResponse = await SendAsync(HttpMethod.Post, "subscriptions.json", request, cancellationToken);
            if (!createResponse.IsSuccessStatusCode)
            {
                // A repeated request can race after the original request reached Maxio. The reference
                // is deterministic, so re-reading it prevents duplicate enrollment after that race.
                var racedSubscription = await FindSubscriptionAsync(subscriptionReference, cancellationToken);
                if (racedSubscription is not null)
                {
                    await SaveSubscriptionRecordAsync(user.Id, plan.Handle, customer.Id, racedSubscription, subscriptionReference, cancellationToken);
                    return ToSubscription(racedSubscription);
                }

                EnsureSuccess(createResponse, "creating the subscription");
            }

            var created = await DeserializeAsync<MaxioSubscriptionResponse>(createResponse, cancellationToken);
            if (created?.Subscription is null)
            {
                throw new MaxioApiException(HttpStatusCode.BadGateway, "creating the subscription");
            }

            await SaveSubscriptionRecordAsync(user.Id, plan.Handle, customer.Id, created.Subscription, subscriptionReference, cancellationToken);
            return ToSubscription(created.Subscription);
        }
        finally
        {
            enrollmentLock.Release();
        }
    }

    public async Task<IReadOnlyList<SubscriptionDto>> GetSubscriptionsAsync(ApplicationUser user, CancellationToken cancellationToken)
    {
        EnsureConfigured();
        var customer = await FindCustomerAsync(CustomerReference(user.Id), cancellationToken);
        if (customer is null)
        {
            return Array.Empty<SubscriptionDto>();
        }

        await SaveCustomerIdAsync(user, customer.Id);
        using var response = await SendAsync(HttpMethod.Get, $"customers/{customer.Id}/subscriptions.json", null, cancellationToken);
        EnsureSuccess(response, "listing subscriptions");
        var subscriptions = await DeserializeAsync<List<MaxioSubscriptionResponse>>(response, cancellationToken) ?? new();

        return subscriptions
            .Where(item => item.Subscription is not null)
            .Select(item => ToSubscription(item.Subscription!))
            .OrderByDescending(subscription => subscription.NextBillingDate)
            .ToList();
    }

    private async Task<MaxioCustomer> EnsureCustomerAsync(ApplicationUser user, CancellationToken cancellationToken)
    {
        var reference = CustomerReference(user.Id);
        var existing = await FindCustomerAsync(reference, cancellationToken);
        if (existing is not null)
        {
            await SaveCustomerIdAsync(user, existing.Id);
            return existing;
        }

        var email = user.Email ?? user.UserName;
        if (string.IsNullOrWhiteSpace(email))
        {
            throw new InvalidOperationException("The authenticated user does not have an email address.");
        }

        var customerName = email.Split('@', 2)[0];
        var request = new CreateCustomerRequest
        {
            Customer = new CreateCustomer
            {
                FirstName = string.IsNullOrWhiteSpace(customerName) ? "Shopper" : customerName,
                LastName = "Shopper",
                Email = email,
                Reference = reference
            }
        };

        using var createResponse = await SendAsync(HttpMethod.Post, "customers.json", request, cancellationToken);
        if (!createResponse.IsSuccessStatusCode)
        {
            // Customer reference is unique according to the Maxio create-customer operation. If another
            // request created it between lookup and POST, use that single customer rather than creating another.
            var racedCustomer = await FindCustomerAsync(reference, cancellationToken);
            if (racedCustomer is not null)
            {
                await SaveCustomerIdAsync(user, racedCustomer.Id);
                return racedCustomer;
            }

            EnsureSuccess(createResponse, "creating the customer");
        }

        var created = await DeserializeAsync<MaxioCustomerResponse>(createResponse, cancellationToken);
        if (created?.Customer is null)
        {
            throw new MaxioApiException(HttpStatusCode.BadGateway, "creating the customer");
        }

        await SaveCustomerIdAsync(user, created.Customer.Id);
        return created.Customer;
    }

    private async Task<MaxioCustomer?> FindCustomerAsync(string reference, CancellationToken cancellationToken)
    {
        using var response = await SendAsync(HttpMethod.Get, $"customers/lookup.json?reference={Uri.EscapeDataString(reference)}", null, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        EnsureSuccess(response, "looking up the customer");
        return (await DeserializeAsync<MaxioCustomerResponse>(response, cancellationToken))?.Customer;
    }

    private async Task<MaxioSubscription?> FindSubscriptionAsync(string reference, CancellationToken cancellationToken)
    {
        using var response = await SendAsync(HttpMethod.Get, $"subscriptions/lookup.json?reference={Uri.EscapeDataString(reference)}", null, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        EnsureSuccess(response, "looking up the subscription");
        return (await DeserializeAsync<MaxioSubscriptionResponse>(response, cancellationToken))?.Subscription;
    }

    private async Task SaveCustomerIdAsync(ApplicationUser user, int customerId)
    {
        if (user.MaxioCustomerId == customerId)
        {
            return;
        }

        user.MaxioCustomerId = customerId;
        var result = await _userManager.UpdateAsync(user);
        if (!result.Succeeded)
        {
            _logger.LogWarning("Could not save the Maxio customer correlation for eShop user {UserId}.", user.Id);
        }
    }

    private async Task SaveSubscriptionRecordAsync(string userId, string planHandle, int customerId, MaxioSubscription subscription, string reference, CancellationToken cancellationToken)
    {
        var existing = await _identityContext.MaxioSubscriptions
            .SingleOrDefaultAsync(record => record.ApplicationUserId == userId && record.PlanHandle == planHandle, cancellationToken);
        if (existing is null)
        {
            _identityContext.MaxioSubscriptions.Add(new MaxioSubscriptionRecord
            {
                ApplicationUserId = userId,
                PlanHandle = planHandle,
                MaxioCustomerId = customerId,
                MaxioSubscriptionId = subscription.Id,
                SubscriptionReference = reference,
                CreatedAt = DateTimeOffset.UtcNow
            });
        }
        else
        {
            existing.MaxioCustomerId = customerId;
            existing.MaxioSubscriptionId = subscription.Id;
            existing.SubscriptionReference = reference;
        }

        await _identityContext.SaveChangesAsync(cancellationToken);
    }

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string pathAndQuery, object? body, CancellationToken cancellationToken)
    {
        var request = new HttpRequestMessage(method, new Uri(ApiBaseAddress(), pathAndQuery));
        var credential = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{_options.ApiKey}:x"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", credential);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        if (body is not null)
        {
            request.Content = new StringContent(JsonSerializer.Serialize(body, JsonOptions), Encoding.UTF8, "application/json");
        }

        return await _httpClientFactory.CreateClient().SendAsync(request, cancellationToken);
    }

    private static async Task<T?> DeserializeAsync<T>(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        return await JsonSerializer.DeserializeAsync<T>(stream, JsonOptions, cancellationToken);
    }

    private static void EnsureSuccess(HttpResponseMessage response, string operation)
    {
        if (!response.IsSuccessStatusCode)
        {
            throw new MaxioApiException(response.StatusCode, operation);
        }
    }

    private void EnsureConfigured()
    {
        if (string.IsNullOrWhiteSpace(_options.ApiKey) || string.IsNullOrWhiteSpace(_options.Subdomain) || string.IsNullOrWhiteSpace(_options.ProductFamilyHandle))
        {
            throw new InvalidOperationException("Maxio is not configured. Set Maxio:ApiKey, Maxio:Subdomain, and Maxio:ProductFamilyHandle.");
        }

        _ = ApiBaseAddress();
    }

    private Uri ApiBaseAddress()
    {
        var configuredBaseUrl = _options.BaseUrl;
        var value = string.IsNullOrWhiteSpace(configuredBaseUrl)
            ? $"https://{_options.Subdomain}.chargify.com/"
            : configuredBaseUrl;

        if (!Uri.TryCreate(value, UriKind.Absolute, out var address) || address.Scheme != Uri.UriSchemeHttps)
        {
            throw new InvalidOperationException("Maxio:BaseUrl must be an absolute HTTPS URL when it is supplied.");
        }

        return address.AbsoluteUri.EndsWith("/", StringComparison.Ordinal) ? address : new Uri(address.AbsoluteUri + "/");
    }

    private static string CustomerReference(string userId) => $"eshop-user-{userId}";
    private static string SubscriptionReference(string userId, string planHandle) => $"eshop-subscription-{userId}-{planHandle}";

    private static SubscriptionPlanDto ToPlan(MaxioProduct product) => new()
    {
        Handle = product.Handle!,
        Name = product.Name,
        PriceInCents = product.PriceInCents,
        Interval = product.Interval,
        IntervalUnit = product.IntervalUnit,
        RequiresPaymentMethod = product.RequireCreditCard
    };

    private static SubscriptionDto ToSubscription(MaxioSubscription subscription) => new()
    {
        Id = subscription.Id,
        PlanHandle = subscription.Product?.Handle ?? string.Empty,
        PlanName = subscription.Product?.Name ?? string.Empty,
        PriceInCents = subscription.ProductPriceInCents,
        State = subscription.State,
        NextBillingDate = subscription.CurrentPeriodEndsAt,
        NextAssessmentAt = subscription.NextAssessmentAt
    };

    private sealed class MaxioProductResponse { public MaxioProduct Product { get; init; } = new(); }
    private sealed class MaxioProduct
    {
        public string Name { get; init; } = string.Empty;
        public string? Handle { get; init; }
        public long PriceInCents { get; init; }
        public int Interval { get; init; }
        public string IntervalUnit { get; init; } = string.Empty;
        public bool RequireCreditCard { get; init; }
        public DateTimeOffset? ArchivedAt { get; init; }
        public MaxioProductFamily? ProductFamily { get; init; }
    }
    private sealed class MaxioProductFamily { public string? Handle { get; init; } }
    private sealed class MaxioCustomerResponse { public MaxioCustomer? Customer { get; init; } }
    private sealed class MaxioCustomer { public int Id { get; init; } }
    private sealed class MaxioSubscriptionResponse { public MaxioSubscription? Subscription { get; init; } }
    private sealed class MaxioSubscription
    {
        public int Id { get; init; }
        public string State { get; init; } = string.Empty;
        public long ProductPriceInCents { get; init; }
        public DateTimeOffset? CurrentPeriodEndsAt { get; init; }
        public DateTimeOffset? NextAssessmentAt { get; init; }
        public MaxioProduct? Product { get; init; }
    }
    private sealed class CreateCustomerRequest { public CreateCustomer Customer { get; init; } = new(); }
    private sealed class CreateCustomer
    {
        public string FirstName { get; init; } = string.Empty;
        public string LastName { get; init; } = string.Empty;
        public string Email { get; init; } = string.Empty;
        public string Reference { get; init; } = string.Empty;
    }
    private sealed class CreateSubscriptionRequest { public CreateSubscription Subscription { get; init; } = new(); }
    private sealed class CreateSubscription
    {
        public string ProductHandle { get; init; } = string.Empty;
        public int CustomerId { get; init; }
        public string Reference { get; init; } = string.Empty;
        public string PaymentCollectionMethod { get; init; } = string.Empty;
    }
}
