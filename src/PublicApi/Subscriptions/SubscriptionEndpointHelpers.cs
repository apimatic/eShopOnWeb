using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.Infrastructure.Identity;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

internal static class SubscriptionEndpointHelpers
{
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> EnrollmentGates = new();

    public static async Task<ApplicationUser?> GetCurrentUserAsync(HttpContext context, UserManager<ApplicationUser> userManager)
    {
        var userId = context.User.FindFirstValue(ClaimTypes.NameIdentifier);
        return string.IsNullOrWhiteSpace(userId) ? null : await userManager.FindByIdAsync(userId);
    }

    public static string CustomerReference(ApplicationUser user) => $"eshop-user-{user.Id}";

    public static MaxioCustomerInput CustomerInput(ApplicationUser user) => new(
        CustomerReference(user),
        user.Email ?? user.UserName ?? $"{user.Id}@invalid.local",
        "eShop",
        "Shopper");

    public static SubscriptionResponse ToResponse(MaxioSubscription subscription) => new(
        subscription.Id,
        subscription.ProductHandle,
        subscription.ProductName,
        subscription.PriceInCents,
        subscription.State,
        subscription.NextBillingAt);

    public static async Task<SemaphoreSlim> EnterEnrollmentGateAsync(string userId, string productHandle, CancellationToken cancellationToken)
    {
        var gate = EnrollmentGates.GetOrAdd($"{userId}:{productHandle}", _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        return gate;
    }

    public static async Task<(SubscriptionEnrollment Enrollment, bool WasCreated)> GetOrCreateEnrollmentAsync(
        AppIdentityDbContext identityContext,
        string userId,
        string productHandle,
        CancellationToken cancellationToken)
    {
        var enrollment = await identityContext.SubscriptionEnrollments
            .SingleOrDefaultAsync(x => x.UserId == userId && x.ProductHandle == productHandle, cancellationToken);
        if (enrollment is not null)
        {
            return (enrollment, false);
        }

        var now = DateTimeOffset.UtcNow;
        enrollment = new SubscriptionEnrollment
        {
            UserId = userId,
            ProductHandle = productHandle,
            Status = "Pending",
            CreatedAt = now,
            UpdatedAt = now
        };
        identityContext.SubscriptionEnrollments.Add(enrollment);

        try
        {
            await identityContext.SaveChangesAsync(cancellationToken);
            return (enrollment, true);
        }
        catch (DbUpdateException)
        {
            identityContext.Entry(enrollment).State = EntityState.Detached;
            var existing = await identityContext.SubscriptionEnrollments
                .SingleAsync(x => x.UserId == userId && x.ProductHandle == productHandle, cancellationToken);
            return (existing, false);
        }
    }

    public static async Task MarkCompleteAsync(AppIdentityDbContext context, SubscriptionEnrollment enrollment, long maxioSubscriptionId, CancellationToken cancellationToken)
    {
        enrollment.Status = "Complete";
        enrollment.MaxioSubscriptionId = maxioSubscriptionId;
        enrollment.UpdatedAt = DateTimeOffset.UtcNow;
        await context.SaveChangesAsync(cancellationToken);
    }

    public static async Task MarkFailedAsync(AppIdentityDbContext context, SubscriptionEnrollment enrollment, CancellationToken cancellationToken)
    {
        enrollment.Status = "Failed";
        enrollment.UpdatedAt = DateTimeOffset.UtcNow;
        await context.SaveChangesAsync(cancellationToken);
    }

    public static MaxioSubscription? FindPlanSubscription(System.Collections.Generic.IReadOnlyList<MaxioSubscription> subscriptions, string productHandle) =>
        subscriptions.FirstOrDefault(x => string.Equals(x.ProductHandle, productHandle, StringComparison.Ordinal));
}
