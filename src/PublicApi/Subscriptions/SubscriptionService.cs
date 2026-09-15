using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.Infrastructure.Identity;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

public sealed class SubscriptionService
{
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> UserLocks = new();
    private readonly IMaxioBillingClient _maxio;
    private readonly AppIdentityDbContext _identityDb;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly MaxioOptions _options;
    private readonly ILogger<SubscriptionService> _logger;
    private readonly IHttpContextAccessor _httpContextAccessor;

    public SubscriptionService(
        IMaxioBillingClient maxio,
        AppIdentityDbContext identityDb,
        UserManager<ApplicationUser> userManager,
        IOptions<MaxioOptions> options,
        ILogger<SubscriptionService> logger,
        IHttpContextAccessor httpContextAccessor)
    {
        _maxio = maxio;
        _identityDb = identityDb;
        _userManager = userManager;
        _options = options.Value;
        _logger = logger;
        _httpContextAccessor = httpContextAccessor;
    }

    public async Task<ApplicationUser> GetCurrentUserAsync(CancellationToken cancellationToken)
    {
        var username = _httpContextAccessor.HttpContext?.User.Identity?.Name;
        if (string.IsNullOrWhiteSpace(username))
        {
            throw new CurrentUserNotFoundException();
        }

        var user = await _userManager.FindByNameAsync(username);
        return user ?? throw new CurrentUserNotFoundException();
    }

    public async Task<IReadOnlyList<SubscriptionPlanDto>> GetPlansAsync(CancellationToken cancellationToken)
    {
        var products = await _maxio.ListProductsAsync(RequiredProductFamilyHandle(), cancellationToken);
        return products
            .Where(IsInConfiguredFamily)
            .Where(product => product.ArchivedAt == null)
            .OrderBy(product => product.PriceInCents)
            .Select(ToPlanDto)
            .ToArray();
    }

    public async Task<SubscriptionDto> SubscribeAsync(ApplicationUser user, string planHandle, CancellationToken cancellationToken)
    {
        var normalizedUserId = user.Id;
        var gate = UserLocks.GetOrAdd(normalizedUserId, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            var products = await _maxio.ListProductsAsync(RequiredProductFamilyHandle(), cancellationToken);
            var plan = products.FirstOrDefault(product =>
                product.Handle.Equals(planHandle, StringComparison.OrdinalIgnoreCase) && IsInConfiguredFamily(product) && product.ArchivedAt == null);
            if (plan == null)
            {
                throw new SubscriptionPlanNotFoundException(planHandle);
            }

            var customerReference = CustomerReference(user.Id);
            var customer = await _maxio.FindCustomerByReferenceAsync(customerReference, cancellationToken);
            if (customer == null)
            {
                customer = await CreateCustomerWithRaceRecoveryAsync(user, customerReference, cancellationToken);
            }

            var subscriptionReference = SubscriptionReference(user.Id, plan.Handle);
            var subscription = await _maxio.FindSubscriptionByReferenceAsync(subscriptionReference, cancellationToken);
            if (subscription == null)
            {
                try
                {
                    subscription = await _maxio.CreateSubscriptionAsync(
                        plan.Handle,
                        customer.Id,
                        subscriptionReference,
                        cancellationToken);
                }
                catch (MaxioApiException ex) when (ex.StatusCode == System.Net.HttpStatusCode.UnprocessableEntity)
                {
                    // A second application instance may have won the race. Maxio's
                    // unique subscription reference lets us recover without creating
                    // a second subscription.
                    subscription = await _maxio.FindSubscriptionByReferenceAsync(subscriptionReference, cancellationToken);
                    if (subscription == null)
                    {
                        throw;
                    }
                }
            }

            await SaveLinkAsync(user.Id, customer.Id, subscription.Id, plan.Handle, subscriptionReference, cancellationToken);
            return ToSubscriptionDto(subscription, customer.Id, plan);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<IReadOnlyList<SubscriptionDto>> GetMySubscriptionsAsync(ApplicationUser user, CancellationToken cancellationToken)
    {
        var customerReference = CustomerReference(user.Id);
        var customer = await _maxio.FindCustomerByReferenceAsync(customerReference, cancellationToken);
        if (customer == null)
        {
            return Array.Empty<SubscriptionDto>();
        }

        var links = await _identityDb.MaxioSubscriptionLinks
            .AsNoTracking()
            .Where(link => link.UserId == user.Id)
            .ToListAsync(cancellationToken);

        var subscriptions = await _maxio.ListCustomerSubscriptionsAsync(customer.Id, cancellationToken);
        var knownReferences = links.Select(link => link.SubscriptionReference).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var integrationPrefix = $"eshop-subscription:{user.Id}:";

        var ownedSubscriptions = subscriptions
            .Where(subscription =>
                (!string.IsNullOrWhiteSpace(subscription.Reference) &&
                 (subscription.Reference.StartsWith(integrationPrefix, StringComparison.OrdinalIgnoreCase) ||
                  knownReferences.Contains(subscription.Reference))) ||
                links.Any(link => link.MaxioSubscriptionId == subscription.Id))
            .ToArray();

        var planProducts = await _maxio.ListProductsAsync(RequiredProductFamilyHandle(), cancellationToken);
        var productByHandle = planProducts.ToDictionary(product => product.Handle, StringComparer.OrdinalIgnoreCase);
        var response = new List<SubscriptionDto>(ownedSubscriptions.Length);

        foreach (var subscription in ownedSubscriptions)
        {
            var plan = subscription.Product != null && productByHandle.TryGetValue(subscription.Product.Handle, out var currentPlan)
                ? currentPlan
                : subscription.Product;
            if (plan == null || !IsInConfiguredFamily(plan))
            {
                continue;
            }

            response.Add(ToSubscriptionDto(subscription, customer.Id, plan));

            if (!links.Any(link => link.MaxioSubscriptionId == subscription.Id))
            {
                await SaveLinkAsync(user.Id, customer.Id, subscription.Id, plan.Handle,
                    subscription.Reference ?? SubscriptionReference(user.Id, plan.Handle), cancellationToken);
            }
        }

        return response.OrderBy(subscription => subscription.PlanName).ToArray();
    }

    private async Task<MaxioCustomer> CreateCustomerWithRaceRecoveryAsync(
        ApplicationUser user,
        string reference,
        CancellationToken cancellationToken)
    {
        try
        {
            return await _maxio.CreateCustomerAsync(CreateCustomerAttributes(user, reference), cancellationToken);
        }
        catch (MaxioApiException ex) when (ex.StatusCode == System.Net.HttpStatusCode.UnprocessableEntity)
        {
            var existing = await _maxio.FindCustomerByReferenceAsync(reference, cancellationToken);
            if (existing == null)
            {
                throw;
            }

            return existing;
        }
    }

    private async Task SaveLinkAsync(
        string userId,
        long customerId,
        long subscriptionId,
        string planHandle,
        string subscriptionReference,
        CancellationToken cancellationToken)
    {
        var exists = await _identityDb.MaxioSubscriptionLinks
            .AnyAsync(link => link.UserId == userId && link.PlanHandle == planHandle, cancellationToken);
        if (exists)
        {
            return;
        }

        _identityDb.MaxioSubscriptionLinks.Add(new MaxioSubscriptionLink
        {
            UserId = userId,
            MaxioCustomerId = customerId,
            MaxioSubscriptionId = subscriptionId,
            PlanHandle = planHandle,
            SubscriptionReference = subscriptionReference,
            CreatedAtUtc = DateTime.UtcNow
        });

        try
        {
            await _identityDb.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            // Another request may have persisted the same remote subscription.
            // The remote response is already authoritative, so a duplicate local
            // index write is safe to ignore after detaching the failed entry.
            foreach (var entry in _identityDb.ChangeTracker.Entries<MaxioSubscriptionLink>().Where(entry => entry.State == EntityState.Added))
            {
                entry.State = EntityState.Detached;
            }
            _logger.LogDebug("Maxio subscription link already persisted for user {UserId} and plan {PlanHandle}.", userId, planHandle);
        }
    }

    private bool IsInConfiguredFamily(MaxioProduct product) =>
        product.ProductFamily == null ||
        product.ProductFamily.Handle.Equals(RequiredProductFamilyHandle(), StringComparison.OrdinalIgnoreCase);

    private string RequiredProductFamilyHandle() =>
        string.IsNullOrWhiteSpace(_options.ProductFamilyHandle)
            ? throw new MaxioConfigurationException("Maxio:ProductFamilyHandle is not configured.")
            : _options.ProductFamilyHandle;

    private static MaxioCustomerAttributes CreateCustomerAttributes(ApplicationUser user, string reference)
    {
        var email = user.Email ?? user.UserName ?? reference;
        var localPart = email.Split('@')[0];
        var words = localPart.Split(new[] { '.', '-', '_', ' ' }, StringSplitOptions.RemoveEmptyEntries);
        var firstName = words.Length > 0 ? words[0] : "eShopOnWeb";
        var lastName = words.Length > 1 ? words[^1] : "Customer";

        return new MaxioCustomerAttributes
        {
            FirstName = firstName,
            LastName = lastName,
            Email = email,
            Reference = reference
        };
    }

    private static string CustomerReference(string userId) => $"eshop-user:{userId}";

    private static string SubscriptionReference(string userId, string planHandle) =>
        $"eshop-subscription:{userId}:{planHandle}";

    private static SubscriptionPlanDto ToPlanDto(MaxioProduct product)
    {
        var price = product.PriceInCents ?? 0;
        return new SubscriptionPlanDto
        {
            Id = product.Id,
            Handle = product.Handle,
            Name = product.Name,
            Description = product.Description,
            PriceInCents = price,
            Price = price / 100m,
            Interval = product.Interval ?? 1,
            IntervalUnit = product.IntervalUnit ?? string.Empty
        };
    }

    private static SubscriptionDto ToSubscriptionDto(MaxioSubscription subscription, long customerId, MaxioProduct plan)
    {
        var price = subscription.ProductPriceInCents ?? plan.PriceInCents ?? 0;
        return new SubscriptionDto
        {
            Id = subscription.Id,
            CustomerId = customerId,
            PlanHandle = plan.Handle,
            PlanName = plan.Name,
            PriceInCents = price,
            Price = price / 100m,
            Interval = plan.Interval ?? 1,
            IntervalUnit = plan.IntervalUnit ?? string.Empty,
            State = subscription.State,
            NextBillingDate = subscription.NextAssessmentAt ?? subscription.NextBillingAt ?? subscription.CurrentPeriodEndsAt,
            Reference = subscription.Reference ?? string.Empty
        };
    }
}

public sealed class CurrentUserNotFoundException : Exception
{
    public CurrentUserNotFoundException() : base("The authenticated eShopOnWeb user could not be found.")
    {
    }
}

public sealed class SubscriptionPlanNotFoundException : Exception
{
    public SubscriptionPlanNotFoundException(string handle)
        : base($"Subscription plan '{handle}' was not found in the configured Maxio product family.")
    {
    }
}
