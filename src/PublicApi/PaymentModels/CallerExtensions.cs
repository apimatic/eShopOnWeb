using System.Security.Claims;

namespace Microsoft.eShopWeb.PublicApi.PaymentModels;

/// <summary>Reads the caller's identity from the JWT so every shopper-scoped endpoint acts only on its own data.</summary>
public static class CallerExtensions
{
    /// <summary>The caller's username/email, which is what <c>Order.BuyerId</c> holds.</summary>
    public static string BuyerId(this ClaimsPrincipal user) =>
        user.Identity?.Name
        ?? user.FindFirstValue(ClaimTypes.Name)
        ?? user.FindFirstValue(ClaimTypes.NameIdentifier)
        ?? string.Empty;

    public static bool IsAdministrator(this ClaimsPrincipal user) =>
        user.IsInRole(BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS);
}
