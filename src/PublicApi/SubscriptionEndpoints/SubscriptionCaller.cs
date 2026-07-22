using Microsoft.AspNetCore.Http;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Resolves the calling identity for the subscription endpoints. The customer reference is the
/// authenticated user's name claim — the same value the storefront uses — so a customer resolves to
/// the same billing-provider record whichever host they arrive through (plan.md §4.4).
/// </summary>
internal static class SubscriptionCaller
{
    public static string? CurrentUserReference(this IHttpContextAccessor httpContextAccessor)
    {
        var name = httpContextAccessor.HttpContext?.User?.Identity?.Name;
        return string.IsNullOrWhiteSpace(name) ? null : name;
    }

    public static bool IsAdministrator(this IHttpContextAccessor httpContextAccessor) =>
        httpContextAccessor.HttpContext?.User?.IsInRole(BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS) ?? false;
}
