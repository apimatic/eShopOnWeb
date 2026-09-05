using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.eShopWeb.Infrastructure.Identity;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

public sealed class SubscriptionEnrollmentInProgressException : Exception
{
    public SubscriptionEnrollmentInProgressException() : base("A subscription request is already being processed. Please retry shortly.") { }
}

public sealed class SubscriptionService
{
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> EnrollmentLocks = new();
    private readonly MaxioClient _maxio;
    private readonly CatalogContext _catalogContext;
    private readonly UserManager<ApplicationUser> _userManager;

    public SubscriptionService(MaxioClient maxio, CatalogContext catalogContext, UserManager<ApplicationUser> userManager)
    {
        _maxio = maxio;
        _catalogContext = catalogContext;
        _userManager = userManager;
    }

    public async Task<IReadOnlyList<SubscriptionPlanDto>> GetPlansAsync(CancellationToken cancellationToken)
    {
        var products = await _maxio.ListProductsAsync(cancellationToken);
        return products
            .Where(p => p.ArchivedAt is null && !string.IsNullOrWhiteSpace(p.Handle))
            .Select(ToPlan)
            .OrderBy(p => p.PriceInCents)
            .ToList();
    }

    public async Task<SubscriptionDto> SubscribeAsync(System.Security.Claims.ClaimsPrincipal principal, string productHandle, CancellationToken cancellationToken)
    {
        var user = await GetUserAsync(principal);
        var plans = await GetPlansAsync(cancellationToken);
        var selectedPlan = plans.SingleOrDefault(p => string.Equals(p.Handle, productHandle, StringComparison.Ordinal));
        if (selectedPlan is null)
            throw new ArgumentException("The requested subscription plan is not available.", nameof(productHandle));

        // EF's in-memory provider does not enforce unique indexes. This process-level lock protects
        // development and a browser double-click; the persisted unique claim protects separate instances.
        var enrollmentLock = EnrollmentLocks.GetOrAdd($"{user.Id}:{productHandle}", _ => new SemaphoreSlim(1, 1));
        await enrollmentLock.WaitAsync(cancellationToken);
        try
        {
            var customer = await GetOrCreateCustomerAsync(user, cancellationToken);
            var subscriptions = await _maxio.ListCustomerSubscriptionsAsync(customer.Id, cancellationToken);
            var existingSubscription = subscriptions.FirstOrDefault(s =>
                string.Equals(s.Product.Handle, productHandle, StringComparison.Ordinal) && IsEnrolledState(s.State));

            if (existingSubscription is not null)
            {
                await CompleteEnrollmentAsync(user.Id, productHandle, customer.Id, existingSubscription.Id, cancellationToken);
                return ToSubscription(existingSubscription);
            }

            if (!await TryClaimEnrollmentAsync(user.Id, productHandle, cancellationToken))
            {
                // A second request (including a request on another server) must not send another create.
                // The original request will either finish and be visible in Maxio or its one-minute claim will expire.
                throw new SubscriptionEnrollmentInProgressException();
            }

            var subscription = await _maxio.CreateSubscriptionAsync(customer.Id, productHandle, cancellationToken);
            await CompleteEnrollmentAsync(user.Id, productHandle, customer.Id, subscription.Id, cancellationToken);
            return ToSubscription(subscription);
        }
        finally
        {
            enrollmentLock.Release();
        }
    }

    public async Task<IReadOnlyList<SubscriptionDto>> GetMySubscriptionsAsync(System.Security.Claims.ClaimsPrincipal principal, CancellationToken cancellationToken)
    {
        var user = await GetUserAsync(principal);
        var customer = await GetCustomerAsync(user.Id, cancellationToken);
        if (customer is null)
            return Array.Empty<SubscriptionDto>();

        var subscriptions = await _maxio.ListCustomerSubscriptionsAsync(customer.Id, cancellationToken);
        return subscriptions.Select(ToSubscription).OrderByDescending(s => s.NextBillingAt).ToList();
    }

    private async Task<ApplicationUser> GetUserAsync(System.Security.Claims.ClaimsPrincipal principal)
    {
        var userName = principal.Identity?.Name;
        var user = string.IsNullOrWhiteSpace(userName) ? null : await _userManager.FindByNameAsync(userName);
        return user ?? throw new UnauthorizedAccessException("The bearer token does not identify an eShopOnWeb user.");
    }

    private async Task<MaxioCustomer> GetOrCreateCustomerAsync(ApplicationUser user, CancellationToken cancellationToken)
    {
        var reference = CustomerReference(user.Id);
        var existing = await GetCustomerAsync(user.Id, cancellationToken);
        if (existing is not null)
            return existing;

        var email = user.Email ?? user.UserName;
        if (string.IsNullOrWhiteSpace(email))
            throw new InvalidOperationException("The signed-in user does not have an email address required by Maxio.");

        var (firstName, lastName) = CustomerName(user.UserName);
        try
        {
            return await _maxio.CreateCustomerAsync(firstName, lastName, email, reference, cancellationToken);
        }
        catch (MaxioApiException ex) when ((int)ex.StatusCode == 422)
        {
            // The OpenAPI contract guarantees customer references are unique. A concurrent create can therefore
            // only be reconciled by its reference instead of creating another customer.
            var concurrentlyCreated = await GetCustomerAsync(user.Id, cancellationToken);
            if (concurrentlyCreated is not null)
                return concurrentlyCreated;

            throw;
        }
    }

    private Task<MaxioCustomer?> GetCustomerAsync(string userId, CancellationToken cancellationToken) =>
        _maxio.FindCustomerByReferenceAsync(CustomerReference(userId), cancellationToken);

    private async Task<bool> TryClaimEnrollmentAsync(string userId, string productHandle, CancellationToken cancellationToken)
    {
        var enrollment = await _catalogContext.SubscriptionEnrollments
            .SingleOrDefaultAsync(x => x.UserId == userId && x.ProductHandle == productHandle, cancellationToken);

        if (enrollment is null)
        {
            _catalogContext.SubscriptionEnrollments.Add(new SubscriptionEnrollment(userId, productHandle));
            try
            {
                await _catalogContext.SaveChangesAsync(cancellationToken);
                return true;
            }
            catch (DbUpdateException)
            {
                _catalogContext.ChangeTracker.Clear();
                return false;
            }
        }

        if (enrollment.MaxioSubscriptionId is not null || !enrollment.IsClaimExpired(DateTimeOffset.UtcNow))
            return false;

        enrollment.RenewClaim();
        await _catalogContext.SaveChangesAsync(cancellationToken);
        return true;
    }

    private async Task CompleteEnrollmentAsync(string userId, string productHandle, int customerId, int subscriptionId, CancellationToken cancellationToken)
    {
        var enrollment = await _catalogContext.SubscriptionEnrollments
            .SingleOrDefaultAsync(x => x.UserId == userId && x.ProductHandle == productHandle, cancellationToken);

        if (enrollment is null)
        {
            enrollment = new SubscriptionEnrollment(userId, productHandle);
            _catalogContext.SubscriptionEnrollments.Add(enrollment);
        }

        enrollment.Complete(customerId, subscriptionId);
        try
        {
            await _catalogContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            // Another process finished the same idempotent enrollment first.
            _catalogContext.ChangeTracker.Clear();
        }
    }

    private static bool IsEnrolledState(string state) =>
        !string.Equals(state, "canceled", StringComparison.OrdinalIgnoreCase) &&
        !string.Equals(state, "expired", StringComparison.OrdinalIgnoreCase);

    private static string CustomerReference(string userId) => $"eshopweb:{userId}";

    private static (string FirstName, string LastName) CustomerName(string? userName)
    {
        var name = string.IsNullOrWhiteSpace(userName) ? "Shopper" : userName.Split('@')[0];
        var parts = name.Split(new[] { '.', '_', '-', ' ' }, StringSplitOptions.RemoveEmptyEntries);
        return (parts.ElementAtOrDefault(0) ?? "Shopper", parts.ElementAtOrDefault(1) ?? "Customer");
    }

    private static SubscriptionPlanDto ToPlan(MaxioProduct product) =>
        new(product.Handle!, product.Name, product.Description, product.PriceInCents, product.Interval, product.IntervalUnit);

    private static SubscriptionDto ToSubscription(MaxioSubscription subscription) =>
        new(subscription.Id, subscription.Product.Handle ?? string.Empty, subscription.Product.Name,
            subscription.ProductPriceInCents, subscription.State,
            subscription.NextAssessmentAt ?? subscription.CurrentPeriodEndsAt);
}

public sealed record SubscriptionPlanDto(string Handle, string Name, string? Description, long PriceInCents, int Interval, string IntervalUnit);
public sealed record SubscriptionDto(int Id, string ProductHandle, string PlanName, long PriceInCents, string State, DateTimeOffset? NextBillingAt);
