using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.eShopWeb.Infrastructure.Identity;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

public sealed class SubscriptionBillingService
{
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> SubscriptionLocks = new();
    private readonly IMaxioClient _maxio;
    private readonly CatalogContext _catalogContext;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly MaxioOptions _options;
    private readonly ILogger<SubscriptionBillingService> _logger;

    public SubscriptionBillingService(
        IMaxioClient maxio,
        CatalogContext catalogContext,
        UserManager<ApplicationUser> userManager,
        Microsoft.Extensions.Options.IOptions<MaxioOptions> options,
        ILogger<SubscriptionBillingService> logger)
    {
        _maxio = maxio;
        _catalogContext = catalogContext;
        _userManager = userManager;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<SubscriptionPlanListResponse> ListPlansAsync(Guid correlationId, CancellationToken cancellationToken)
    {
        var products = await _maxio.ListProductsAsync(RequiredProductFamilyHandle(), cancellationToken);
        var response = new SubscriptionPlanListResponse(correlationId);

        response.Plans.AddRange(products
            .Where(product => !string.IsNullOrWhiteSpace(product.Handle) && product.ArchivedAt is null)
            .Select(MapPlan));
        return response;
    }

    public async Task<CreateSubscriptionResponse> SubscribeAsync(
        ClaimsPrincipal principal,
        CreateSubscriptionRequest request,
        Guid correlationId,
        CancellationToken cancellationToken)
    {
        var planHandle = request.PlanHandle?.Trim() ?? string.Empty;
        if (planHandle.Length == 0)
            throw new SubscriptionRequestException("PlanHandle is required.");

        var user = await ResolveUserAsync(principal, cancellationToken);
        var billingIdentity = GetBillingIdentity(principal, user);
        var plan = (await _maxio.ListProductsAsync(RequiredProductFamilyHandle(), cancellationToken))
            .FirstOrDefault(product => string.Equals(product.Handle, planHandle, StringComparison.OrdinalIgnoreCase)
                && product.ArchivedAt is null);
        if (plan is null)
            throw new SubscriptionRequestException("The selected subscription plan is not available.");

        var lockKey = $"{billingIdentity}:{plan.Handle}";
        var gate = SubscriptionLocks.GetOrAdd(lockKey, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            var customerReference = BuildReference("eshop-user", billingIdentity);
            var customer = await FindOrCreateCustomerAsync(user, customerReference, cancellationToken);
            var subscriptionReference = BuildReference("eshop-subscription", $"{billingIdentity}:{plan.Handle}");
            var mapping = await _catalogContext.SubscriptionMappings
                .SingleOrDefaultAsync(x => x.UserId == user.Id && x.ProductHandle == plan.Handle, cancellationToken);

            var subscription = mapping is not null
                ? await _maxio.GetSubscriptionAsync(mapping.MaxioSubscriptionId, cancellationToken)
                : null;
            subscription ??= await _maxio.FindSubscriptionByReferenceAsync(subscriptionReference, cancellationToken);

            if (subscription is null)
            {
                try
                {
                    subscription = await _maxio.CreateSubscriptionAsync(
                        new MaxioSubscriptionCreate
                        {
                            ProductHandle = plan.Handle!,
                            CustomerId = customer.Id,
                            Reference = subscriptionReference
                        },
                        subscriptionReference,
                        cancellationToken);
                }
                catch (MaxioApiException exception) when (exception.StatusCode == System.Net.HttpStatusCode.Conflict)
                {
                    // The request may have completed before the response was lost. The documented
                    // recovery is to locate the resource by its reference before retrying creation.
                    subscription = await FindSubscriptionAfterDuplicateAsync(subscriptionReference, cancellationToken);
                    if (subscription is null)
                        throw;
                }
            }

            await SaveMappingAsync(user.Id, plan.Handle!, customerReference, customer.Id, subscription.Id, subscriptionReference, cancellationToken);
            return new CreateSubscriptionResponse(correlationId)
            {
                Subscription = MapSubscription(subscription, plan.Handle!, plan)
            };
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<MySubscriptionsResponse> ListMySubscriptionsAsync(
        ClaimsPrincipal principal,
        Guid correlationId,
        CancellationToken cancellationToken)
    {
        var user = await ResolveUserAsync(principal, cancellationToken);
        var customer = await _maxio.FindCustomerByReferenceAsync(
            BuildReference("eshop-user", GetBillingIdentity(principal, user)), cancellationToken);
        var response = new MySubscriptionsResponse(correlationId);
        if (customer is null)
            return response;

        var subscriptions = await _maxio.ListCustomerSubscriptionsAsync(customer.Id, cancellationToken);
        response.Subscriptions.AddRange(subscriptions
            .Where(subscription => subscription.Product?.Handle is not null)
            .Select(subscription => MapSubscription(subscription, subscription.Product!.Handle!, subscription.Product)));
        return response;
    }

    private async Task<ApplicationUser> ResolveUserAsync(ClaimsPrincipal principal, CancellationToken cancellationToken)
    {
        var userId = principal.FindFirstValue(ClaimTypes.NameIdentifier);
        var userName = principal.Identity?.Name ?? principal.FindFirstValue(ClaimTypes.Name);
        var user = !string.IsNullOrWhiteSpace(userId)
            ? await _userManager.FindByIdAsync(userId)
            : null;
        user ??= !string.IsNullOrWhiteSpace(userName)
            ? await _userManager.FindByNameAsync(userName)
            : null;

        if (user is null)
            throw new SubscriptionRequestException("The authenticated user could not be found.");

        return user;
    }

    private static string GetBillingIdentity(ClaimsPrincipal principal, ApplicationUser user)
    {
        // Usernames are unique in Identity and remain stable for the in-memory development
        // host, whose generated database IDs are intentionally lost on process restart.
        return user.UserName ?? principal.Identity?.Name
            ?? throw new SubscriptionRequestException("The authenticated user has no stable identity.");
    }

    private async Task<MaxioCustomer> FindOrCreateCustomerAsync(ApplicationUser user, string reference, CancellationToken cancellationToken)
    {
        var existing = await _maxio.FindCustomerByReferenceAsync(reference, cancellationToken);
        if (existing is not null)
            return existing;

        try
        {
            return await _maxio.CreateCustomerAsync(
                new MaxioCustomerCreate
                {
                    FirstName = "eShopOnWeb",
                    LastName = "Shopper",
                    Email = user.Email ?? user.UserName ?? reference,
                    Reference = reference
                },
                reference,
                cancellationToken);
        }
        catch (MaxioApiException exception) when ((int)exception.StatusCode >= 400 && (int)exception.StatusCode < 500)
        {
            // A concurrent request can win the unique customer-reference race.
            existing = await _maxio.FindCustomerByReferenceAsync(reference, cancellationToken);
            if (existing is not null)
                return existing;
            throw;
        }
    }

    private async Task<MaxioSubscription?> FindSubscriptionAfterDuplicateAsync(string reference, CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < 3; attempt++)
        {
            var subscription = await _maxio.FindSubscriptionByReferenceAsync(reference, cancellationToken);
            if (subscription is not null)
                return subscription;
            await Task.Delay(TimeSpan.FromMilliseconds(100 * (attempt + 1)), cancellationToken);
        }

        return null;
    }

    private async Task SaveMappingAsync(
        string userId,
        string productHandle,
        string customerReference,
        int customerId,
        int subscriptionId,
        string subscriptionReference,
        CancellationToken cancellationToken)
    {
        var mapping = await _catalogContext.SubscriptionMappings
            .SingleOrDefaultAsync(x => x.UserId == userId && x.ProductHandle == productHandle, cancellationToken);
        if (mapping is null)
        {
            _catalogContext.SubscriptionMappings.Add(new SubscriptionMapping(
                userId, productHandle, customerReference, customerId, subscriptionId, subscriptionReference));
        }
        else
        {
            mapping.UpdateMaxioIds(customerId, subscriptionId);
        }

        try
        {
            await _catalogContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception)
        {
            // Another application instance may have recorded the same remote subscription.
            // Maxio's idempotency/reference checks are authoritative; the request can still
            // return the confirmed Maxio state in this case.
            _logger.LogWarning(exception, "Could not persist the local mapping for Maxio subscription {SubscriptionId}.", subscriptionId);
        }
    }

    private string RequiredProductFamilyHandle() => !string.IsNullOrWhiteSpace(_options.ProductFamilyHandle)
        ? _options.ProductFamilyHandle
        : throw new MaxioConfigurationException("Maxio:ProductFamilyHandle is required.");

    private static SubscriptionPlanDto MapPlan(MaxioProduct product) => new()
    {
        Handle = product.Handle!,
        Name = product.Name,
        Description = product.Description,
        PriceInCents = product.PriceInCents,
        Price = product.PriceInCents / 100m,
        Interval = product.Interval,
        IntervalUnit = product.IntervalUnit,
        RequiresPaymentMethod = product.RequireCreditCard
    };

    private static SubscriptionDto MapSubscription(MaxioSubscription subscription, string fallbackPlanHandle, MaxioProduct? product) => new()
    {
        Id = subscription.Id,
        PlanHandle = product?.Handle ?? fallbackPlanHandle,
        PlanName = product?.Name ?? fallbackPlanHandle,
        PriceInCents = subscription.ProductPriceInCents != 0
            ? subscription.ProductPriceInCents
            : product?.PriceInCents ?? 0,
        Price = (subscription.ProductPriceInCents != 0
            ? subscription.ProductPriceInCents
            : product?.PriceInCents ?? 0) / 100m,
        State = subscription.State,
        NextBillingDate = subscription.CurrentPeriodEndsAt ?? subscription.NextAssessmentAt
    };

    private static string BuildReference(string prefix, string value)
    {
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
        return $"{prefix}:{hash}";
    }
}

public sealed class SubscriptionRequestException : Exception
{
    public SubscriptionRequestException(string message) : base(message)
    {
    }
}
