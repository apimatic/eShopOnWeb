using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.eShopWeb.Infrastructure.Identity;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

public sealed class MaxioBillingService : IMaxioBillingService
{
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> UserLocks = new();
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _httpClient;
    private readonly AppIdentityDbContext _identityDb;
    private readonly MaxioOptions _options;
    private readonly ILogger<MaxioBillingService> _logger;

    public MaxioBillingService(
        HttpClient httpClient,
        AppIdentityDbContext identityDb,
        IOptions<MaxioOptions> options,
        ILogger<MaxioBillingService> logger)
    {
        _httpClient = httpClient;
        _identityDb = identityDb;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<IReadOnlyList<SubscriptionPlanDto>> GetPlansAsync(CancellationToken cancellationToken)
    {
        EnsureConfigured();
        var response = await SendAsync<MaxioProductListEnvelope>(
            HttpMethod.Get,
            $"product_families/handle:{Uri.EscapeDataString(_options.ProductFamilyHandle)}/products.json?per_page=200",
            null,
            cancellationToken);

        return response.Items
            .Select(item => item.Product)
            .Where(product => !string.IsNullOrWhiteSpace(product.Handle) && product.ArchivedAt is null)
            .Select(product => new SubscriptionPlanDto(
                product.Handle!,
                product.Name,
                product.Description,
                product.PriceInCents,
                product.Interval,
                product.IntervalUnit,
                product.RequireCreditCard))
            .ToArray();
    }

    public async Task<SubscriptionDto> SubscribeAsync(
        ApplicationUser user,
        string planHandle,
        CancellationToken cancellationToken)
    {
        EnsureConfigured();
        if (string.IsNullOrWhiteSpace(user.Id))
            throw new InvalidOperationException("The authenticated user has no identity id.");

        var userLock = UserLocks.GetOrAdd(user.Id, _ => new SemaphoreSlim(1, 1));
        await userLock.WaitAsync(cancellationToken);
        try
        {
            var plans = await GetPlansAsync(cancellationToken);
            var plan = plans.SingleOrDefault(candidate =>
                string.Equals(candidate.Handle, planHandle.Trim(), StringComparison.Ordinal));
            if (plan is null)
                throw new SubscriptionPlanNotFoundException(planHandle);

            var customer = await EnsureCustomerAsync(user, cancellationToken);
            var reference = CreateSubscriptionReference(CreateExternalUserKey(user), plan.Handle);
            var existing = (await GetCustomerSubscriptionsAsync(customer.Id, cancellationToken))
                .FirstOrDefault(subscription => string.Equals(subscription.Reference, reference, StringComparison.Ordinal));

            if (existing is not null)
            {
                await SaveSubscriptionMappingAsync(user.Id, customer.Id, existing, plan.Handle, reference, cancellationToken);
                return ToSubscriptionDto(existing, customer.Id, plan);
            }

            var body = new
            {
                subscription = new
                {
                    product_handle = plan.Handle,
                    customer_id = customer.Id,
                    reference,
                    payment_collection_method = await GetPaymentCollectionMethodAsync(cancellationToken)
                },
                uniqueness_token = Guid.NewGuid().ToString()
            };

            MaxioSubscription created;
            try
            {
                var createdResponse = await SendAsync<MaxioSubscriptionEnvelope>(
                    HttpMethod.Post,
                    "subscriptions.json",
                    body,
                    cancellationToken);
                created = createdResponse.Subscription;
            }
            catch (MaxioApiException ex) when (ex.StatusCode is HttpStatusCode.Conflict or HttpStatusCode.UnprocessableEntity)
            {
                // The reference is unique in Maxio. A 409 can mean the request was
                // processed before a transport failure, and a 422 can be a reference
                // collision from another instance. Re-read before reporting failure.
                var recovered = (await GetCustomerSubscriptionsAsync(customer.Id, cancellationToken))
                    .FirstOrDefault(subscription => string.Equals(subscription.Reference, reference, StringComparison.Ordinal));
                if (recovered is null)
                    throw;
                created = recovered;
            }

            await SaveSubscriptionMappingAsync(user.Id, customer.Id, created, plan.Handle, reference, cancellationToken);
            return ToSubscriptionDto(created, customer.Id, plan);
        }
        finally
        {
            userLock.Release();
        }
    }

    public async Task<IReadOnlyList<SubscriptionDto>> GetMySubscriptionsAsync(
        ApplicationUser user,
        CancellationToken cancellationToken)
    {
        EnsureConfigured();
        var reference = CreateCustomerReference(user);
        var customer = await GetCustomerByReferenceAsync(reference, cancellationToken);
        if (customer is null)
            return Array.Empty<SubscriptionDto>();

        await SaveCustomerMappingAsync(user.Id, customer.Id, cancellationToken);
        var plans = await GetPlansAsync(cancellationToken);
        var planByHandle = plans.ToDictionary(plan => plan.Handle, StringComparer.Ordinal);
        var subscriptions = await GetCustomerSubscriptionsAsync(customer.Id, cancellationToken);

        foreach (var subscription in subscriptions)
        {
            var handle = subscription.Product?.Handle ?? string.Empty;
            if (string.IsNullOrWhiteSpace(subscription.Reference) || string.IsNullOrWhiteSpace(handle))
                continue;

            await SaveSubscriptionMappingAsync(user.Id, customer.Id, subscription, handle, subscription.Reference, cancellationToken);
        }

        return subscriptions
            .Select(subscription =>
            {
                var handle = subscription.Product?.Handle ?? string.Empty;
                planByHandle.TryGetValue(handle, out var plan);
                return ToSubscriptionDto(subscription, customer.Id, plan);
            })
            .ToArray();
    }

    private async Task<MaxioCustomer> EnsureCustomerAsync(ApplicationUser user, CancellationToken cancellationToken)
    {
        var reference = CreateCustomerReference(user);
        var customer = await GetCustomerByReferenceAsync(reference, cancellationToken);
        if (customer is null)
        {
            var email = user.Email ?? user.UserName ?? $"{user.Id}@invalid.local";
            var body = new
            {
                customer = new
                {
                    first_name = email.Split('@')[0],
                    last_name = "eShopOnWeb customer",
                    email,
                    reference
                },
                uniqueness_token = Guid.NewGuid().ToString()
            };

            try
            {
                customer = (await SendAsync<MaxioCustomerEnvelope>(
                    HttpMethod.Post,
                    "customers.json",
                    body,
                    cancellationToken)).Customer;
            }
            catch (MaxioApiException ex) when (ex.StatusCode is HttpStatusCode.Conflict or HttpStatusCode.UnprocessableEntity)
            {
                var recovered = await GetCustomerByReferenceAsync(reference, cancellationToken);
                if (recovered is null)
                    throw;
                customer = recovered;
            }
        }

        await SaveCustomerMappingAsync(user.Id, customer.Id, cancellationToken);
        return customer;
    }

    private async Task<MaxioCustomer?> GetCustomerByReferenceAsync(string reference, CancellationToken cancellationToken)
    {
        try
        {
            return (await SendAsync<MaxioCustomerEnvelope>(
                HttpMethod.Get,
                $"customers/lookup.json?reference={Uri.EscapeDataString(reference)}",
                null,
                cancellationToken)).Customer;
        }
        catch (MaxioApiException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    private async Task<IReadOnlyList<MaxioSubscription>> GetCustomerSubscriptionsAsync(
        int customerId,
        CancellationToken cancellationToken)
    {
        var response = await SendAsync<MaxioSubscriptionListEnvelope>(
            HttpMethod.Get,
            $"customers/{customerId}/subscriptions.json",
            null,
            cancellationToken);
        return response.Items.Select(item => item.Subscription).ToArray();
    }

    private async Task<string> GetPaymentCollectionMethodAsync(CancellationToken cancellationToken)
    {
        var response = await SendAsync<MaxioSiteEnvelope>(
            HttpMethod.Get,
            "site.json",
            null,
            cancellationToken);
        return response.Site.RelationshipInvoicingEnabled ? "remittance" : "invoice";
    }

    private async Task SaveCustomerMappingAsync(string userId, int customerId, CancellationToken cancellationToken)
    {
        var mapping = await _identityDb.MaxioCustomerMappings.FindAsync(new object[] { userId }, cancellationToken);
        if (mapping is null)
        {
            var newMapping = new MaxioCustomerMapping
            {
                UserId = userId,
                MaxioCustomerId = customerId
            };
            _identityDb.MaxioCustomerMappings.Add(newMapping);
            try
            {
                await _identityDb.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateException)
            {
                _identityDb.Entry(newMapping).State = EntityState.Detached;
                if (!await _identityDb.MaxioCustomerMappings.AnyAsync(existing => existing.UserId == userId, cancellationToken))
                    throw;
            }
        }
        else if (mapping.MaxioCustomerId != customerId)
        {
            _logger.LogWarning("Maxio customer mapping changed for user {UserId}; refreshing local id.", userId);
            mapping.MaxioCustomerId = customerId;
            await _identityDb.SaveChangesAsync(cancellationToken);
        }
    }

    private async Task SaveSubscriptionMappingAsync(
        string userId,
        int customerId,
        MaxioSubscription subscription,
        string productHandle,
        string reference,
        CancellationToken cancellationToken)
    {
        if (subscription.Id == 0)
            throw new InvalidOperationException("Maxio returned a subscription without an id.");

        var existing = await _identityDb.MaxioSubscriptionMappings
            .SingleOrDefaultAsync(mapping => mapping.MaxioSubscriptionId == subscription.Id, cancellationToken);
        if (existing is null)
        {
            var newMapping = new MaxioSubscriptionMapping
            {
                UserId = userId,
                MaxioCustomerId = customerId,
                MaxioSubscriptionId = subscription.Id,
                ProductHandle = productHandle,
                Reference = reference,
                CreatedAt = DateTimeOffset.UtcNow
            };
            _identityDb.MaxioSubscriptionMappings.Add(newMapping);
            try
            {
                await _identityDb.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateException)
            {
                _identityDb.Entry(newMapping).State = EntityState.Detached;
                if (!await _identityDb.MaxioSubscriptionMappings.AnyAsync(
                        saved => saved.MaxioSubscriptionId == subscription.Id ||
                                 (saved.UserId == userId && saved.Reference == reference),
                        cancellationToken))
                    throw;
            }
        }
    }

    private async Task<T> SendAsync<T>(HttpMethod method, string relativePath, object? body, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, BuildUri(relativePath));
        var credentials = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{_options.ApiKey}:X"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", credentials);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        if (body is not null)
            request.Content = JsonContent.Create(body, options: JsonOptions);

        using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var error = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new MaxioApiException(response.StatusCode, error);
        }

        var responseJson = await response.Content.ReadAsStringAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(responseJson))
            throw new MaxioApiException(response.StatusCode, "Maxio returned an empty response.");

        using var document = JsonDocument.Parse(responseJson);
        if (document.RootElement.ValueKind == JsonValueKind.Array && typeof(T) == typeof(MaxioProductListEnvelope))
        {
            var items = JsonSerializer.Deserialize<List<MaxioProductListItem>>(responseJson, JsonOptions) ?? new();
            return (T)(object)new MaxioProductListEnvelope { Items = items };
        }

        if (document.RootElement.ValueKind == JsonValueKind.Array && typeof(T) == typeof(MaxioSubscriptionListEnvelope))
        {
            var items = JsonSerializer.Deserialize<List<MaxioSubscriptionListItem>>(responseJson, JsonOptions) ?? new();
            return (T)(object)new MaxioSubscriptionListEnvelope { Items = items };
        }

        return JsonSerializer.Deserialize<T>(responseJson, JsonOptions)
            ?? throw new MaxioApiException(response.StatusCode, "Maxio returned an empty response.");
    }

    private Uri BuildUri(string relativePath)
    {
        var configuredBaseUrl = string.IsNullOrWhiteSpace(_options.BaseUrl)
            ? $"https://{_options.Subdomain}.chargify.com/"
            : _options.BaseUrl!;
        var baseUrl = configuredBaseUrl.EndsWith('/') ? configuredBaseUrl : $"{configuredBaseUrl}/";
        return new Uri(new Uri(baseUrl, UriKind.Absolute), relativePath);
    }

    private void EnsureConfigured()
    {
        if (string.IsNullOrWhiteSpace(_options.ApiKey) ||
            string.IsNullOrWhiteSpace(_options.ProductFamilyHandle) ||
            (string.IsNullOrWhiteSpace(_options.BaseUrl) && string.IsNullOrWhiteSpace(_options.Subdomain)))
        {
            throw new InvalidOperationException("Maxio configuration is incomplete. Configure Maxio:ApiKey, Maxio:Subdomain, Maxio:ProductFamilyHandle, and optionally Maxio:BaseUrl.");
        }
    }

    private static string CreateCustomerReference(ApplicationUser user) =>
        $"eshop-user:{CreateExternalUserKey(user)}";

    private static string CreateExternalUserKey(ApplicationUser user) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            (user.NormalizedEmail ?? user.Email ?? user.UserName ?? user.Id).Trim().ToUpperInvariant())))
            .ToLowerInvariant();

    private static string CreateSubscriptionReference(string externalUserKey, string planHandle) =>
        $"eshop-subscription:{externalUserKey}:{planHandle}";

    private static SubscriptionDto ToSubscriptionDto(MaxioSubscription subscription, int customerId, SubscriptionPlanDto? plan)
    {
        var product = subscription.Product;
        return new SubscriptionDto(
            subscription.Id,
            customerId,
            product?.Handle ?? string.Empty,
            product?.Name ?? plan?.Name ?? string.Empty,
            subscription.ProductPriceInCents ?? product?.PriceInCents ?? plan?.PriceInCents ?? 0,
            product?.Interval ?? plan?.Interval ?? 0,
            product?.IntervalUnit ?? plan?.IntervalUnit ?? string.Empty,
            subscription.State,
            subscription.CurrentPeriodEndsAt ?? subscription.NextAssessmentAt);
    }
}

public sealed class MaxioApiException : Exception
{
    public MaxioApiException(HttpStatusCode statusCode, string responseBody)
        : base($"Maxio returned HTTP {(int)statusCode} ({statusCode}).")
    {
        StatusCode = statusCode;
        ResponseBody = responseBody;
    }

    public HttpStatusCode StatusCode { get; }
    public string ResponseBody { get; }
}

public sealed class SubscriptionPlanNotFoundException : Exception
{
    public SubscriptionPlanNotFoundException(string handle)
        : base($"Subscription plan '{handle}' was not found in the configured Maxio product family.")
    {
    }
}
