using System.Collections.Concurrent;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.Infrastructure.Identity;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

public sealed class SubscriptionService
{
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> SignupLocks = new();
    private readonly IMaxioBillingClient _maxio;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly AppIdentityDbContext _identityDb;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly IOptions<MaxioOptions> _options;

    public SubscriptionService(
        IMaxioBillingClient maxio,
        UserManager<ApplicationUser> userManager,
        AppIdentityDbContext identityDb,
        IHttpContextAccessor httpContextAccessor,
        IOptions<MaxioOptions> options)
    {
        _maxio = maxio;
        _userManager = userManager;
        _identityDb = identityDb;
        _httpContextAccessor = httpContextAccessor;
        _options = options;
    }

    public async Task<IReadOnlyList<SubscriptionPlanDto>> GetPlansAsync(CancellationToken cancellationToken)
    {
        var familyHandle = RequireProductFamilyHandle();
        var products = await _maxio.ListProductsAsync(familyHandle, cancellationToken);
        return products
            .Where(product => !string.IsNullOrWhiteSpace(product.Handle) && product.ArchivedAt is null)
            .Select(ToPlan)
            .ToArray();
    }

    public async Task<SubscriptionSignupResult> SubscribeAsync(SubscribeRequest request, CancellationToken cancellationToken)
    {
        var planHandle = request.PlanHandle?.Trim();
        if (string.IsNullOrWhiteSpace(planHandle))
        {
            throw new ArgumentException("planHandle is required.");
        }

        var products = await _maxio.ListProductsAsync(RequireProductFamilyHandle(), cancellationToken);
        var product = products.FirstOrDefault(x =>
            x.ArchivedAt is null && string.Equals(x.Handle, planHandle, StringComparison.Ordinal));
        if (product is null || string.IsNullOrWhiteSpace(product.Handle))
        {
            throw new KeyNotFoundException($"The subscription plan '{planHandle}' was not found.");
        }

        var user = await GetCurrentUserAsync();
        var userId = user.Id;
        var customerReference = CustomerReference(userId);
        var customer = await GetOrCreateCustomerAsync(user, customerReference, cancellationToken);
        var subscriptionReference = SubscriptionReference(userId, product.Handle);
        var signupLock = SignupLocks.GetOrAdd(userId, _ => new SemaphoreSlim(1, 1));

        await signupLock.WaitAsync(cancellationToken);
        try
        {
            var mapping = await _identityDb.SubscriptionMappings.SingleOrDefaultAsync(
                x => x.UserId == userId && x.ProductHandle == product.Handle,
                cancellationToken);

            if (mapping?.MaxioSubscriptionId is int mappedSubscriptionId)
            {
                var mapped = await ReadMappedSubscriptionAsync(mappedSubscriptionId, customer.Id, product.Handle, mapping, cancellationToken);
                if (mapped is not null)
                {
                    return new SubscriptionSignupResult { Subscription = ToSubscription(mapped), Created = false };
                }
            }

            var customerSubscriptions = await _maxio.ListCustomerSubscriptionsAsync(customer.Id, cancellationToken);
            var existing = customerSubscriptions.FirstOrDefault(subscription =>
                string.Equals(subscription.Reference, subscriptionReference, StringComparison.Ordinal) ||
                (string.Equals(subscription.Product?.Handle, product.Handle, StringComparison.Ordinal) && IsLive(subscription.State)));
            if (existing is not null)
            {
                await SaveMappingAsync(mapping ?? NewMapping(userId, product.Handle, subscriptionReference, customer.Id), existing.Id, cancellationToken);
                return new SubscriptionSignupResult { Subscription = ToSubscription(existing), Created = false };
            }

            if (mapping is not null)
            {
                // A pending row is a cross-process idempotency reservation. Do not issue
                // another remote create while its first request may still be in flight.
                throw new DuplicateException("A subscription signup for this plan is already being processed.");
            }

            mapping = NewMapping(userId, product.Handle, subscriptionReference, customer.Id);
            _identityDb.SubscriptionMappings.Add(mapping);
            await _identityDb.SaveChangesAsync(cancellationToken);

            MaxioSubscription created;
            try
            {
                created = await _maxio.CreateSubscriptionAsync(product.Handle, customer.Id, subscriptionReference, cancellationToken);
            }
            catch (MaxioApiException ex) when ((int)ex.StatusCode >= 400 && (int)ex.StatusCode < 500 && (int)ex.StatusCode != 409)
            {
                // A rejected request cannot have created a subscription. Releasing the
                // reservation lets the shopper correct the request and retry.
                _identityDb.SubscriptionMappings.Remove(mapping);
                await _identityDb.SaveChangesAsync(cancellationToken);
                throw;
            }
            mapping.MaxioSubscriptionId = created.Id;
            mapping.UpdatedAtUtc = DateTime.UtcNow;
            await _identityDb.SaveChangesAsync(cancellationToken);
            return new SubscriptionSignupResult { Subscription = ToSubscription(created), Created = true };
        }
        finally
        {
            signupLock.Release();
        }
    }

    public async Task<IReadOnlyList<MySubscriptionDto>> GetMySubscriptionsAsync(CancellationToken cancellationToken)
    {
        var user = await GetCurrentUserAsync();
        var customer = await _maxio.GetCustomerByReferenceAsync(CustomerReference(user.Id), cancellationToken);
        if (customer is null)
        {
            return Array.Empty<MySubscriptionDto>();
        }

        var subscriptions = await _maxio.ListCustomerSubscriptionsAsync(customer.Id, cancellationToken);
        return subscriptions.Select(ToSubscription).ToArray();
    }

    private async Task<MaxioCustomer> GetOrCreateCustomerAsync(
        ApplicationUser user,
        string reference,
        CancellationToken cancellationToken)
    {
        var existing = await _maxio.GetCustomerByReferenceAsync(reference, cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        var email = user.Email ?? user.UserName;
        if (string.IsNullOrWhiteSpace(email))
        {
            throw new InvalidOperationException("The authenticated user does not have an email address.");
        }

        try
        {
            return await _maxio.CreateCustomerAsync(new MaxioCustomerAttributes
            {
                FirstName = "eShopOnWeb",
                LastName = user.UserName ?? user.Id,
                Email = email,
                Reference = reference
            }, cancellationToken);
        }
        catch (MaxioApiException ex) when ((int)ex.StatusCode == 422)
        {
            // Maxio enforces reference uniqueness. A second request that raced the
            // first create recovers the customer rather than creating another one.
            var racedCustomer = await _maxio.GetCustomerByReferenceAsync(reference, cancellationToken);
            if (racedCustomer is not null)
            {
                return racedCustomer;
            }

            throw;
        }
    }

    private async Task<MaxioSubscription?> ReadMappedSubscriptionAsync(
        int subscriptionId,
        int customerId,
        string productHandle,
        SubscriptionMapping mapping,
        CancellationToken cancellationToken)
    {
        try
        {
            var subscription = await _maxio.GetSubscriptionAsync(subscriptionId, cancellationToken);
            if (subscription.Id == subscriptionId &&
                (subscription.Customer?.Id is null || subscription.Customer.Id == customerId) &&
                (subscription.Product?.Handle is null || subscription.Product.Handle == productHandle))
            {
                return subscription;
            }
        }
        catch (MaxioApiException ex) when ((int)ex.StatusCode == 404)
        {
            mapping.MaxioSubscriptionId = null;
            mapping.UpdatedAtUtc = DateTime.UtcNow;
            await _identityDb.SaveChangesAsync(cancellationToken);
        }

        return null;
    }

    private async Task SaveMappingAsync(SubscriptionMapping mapping, int subscriptionId, CancellationToken cancellationToken)
    {
        mapping.MaxioSubscriptionId = subscriptionId;
        mapping.UpdatedAtUtc = DateTime.UtcNow;
        if (mapping.Id == 0)
        {
            _identityDb.SubscriptionMappings.Add(mapping);
        }

        await _identityDb.SaveChangesAsync(cancellationToken);
    }

    private SubscriptionMapping NewMapping(string userId, string productHandle, string subscriptionReference, int customerId) =>
        new()
        {
            UserId = userId,
            ProductHandle = productHandle,
            SubscriptionReference = subscriptionReference,
            MaxioCustomerId = customerId,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        };

    private async Task<ApplicationUser> GetCurrentUserAsync()
    {
        var principal = _httpContextAccessor.HttpContext?.User;
        var userName = principal?.FindFirstValue(ClaimTypes.Name) ?? principal?.Identity?.Name;
        if (string.IsNullOrWhiteSpace(userName))
        {
            throw new SubscriptionIdentityException();
        }

        return await _userManager.FindByNameAsync(userName) ?? throw new SubscriptionIdentityException();
    }

    private string RequireProductFamilyHandle() =>
        !string.IsNullOrWhiteSpace(_options.Value.ProductFamilyHandle)
            ? _options.Value.ProductFamilyHandle!
            : throw new InvalidOperationException("Maxio:ProductFamilyHandle is not configured.");

    private static string CustomerReference(string userId) => $"eshop-user:{userId}";

    private static string SubscriptionReference(string userId, string productHandle) =>
        $"eshop-subscription:{userId}:{productHandle}";

    private static bool IsLive(string? state) => state is "pending" or "trialing" or "assessing" or "active" or
        "soft_failure" or "past_due" or "suspended" or "paused" or "unpaid" or "awaiting_signup" or "on_hold";

    private static SubscriptionPlanDto ToPlan(MaxioProduct product) => new()
    {
        Handle = product.Handle!,
        Name = product.Name,
        Description = product.Description,
        PriceInCents = product.PriceInCents,
        Interval = product.Interval,
        IntervalUnit = product.IntervalUnit,
        RequiresPaymentMethod = product.RequireCreditCard
    };

    private static MySubscriptionDto ToSubscription(MaxioSubscription subscription) => new()
    {
        Id = subscription.Id,
        PlanHandle = subscription.Product?.Handle ?? string.Empty,
        PlanName = subscription.Product?.Name ?? string.Empty,
        PriceInCents = subscription.ProductPriceInCents != 0
            ? subscription.ProductPriceInCents
            : subscription.Product?.PriceInCents ?? 0,
        State = subscription.State,
        NextBillingAt = subscription.NextAssessmentAt ?? subscription.CurrentPeriodEndsAt,
        CurrentPeriodEndsAt = subscription.CurrentPeriodEndsAt
    };
}

public sealed class SubscriptionIdentityException : Exception
{
}
