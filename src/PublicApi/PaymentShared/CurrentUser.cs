using System;
using Microsoft.AspNetCore.Http;

namespace Microsoft.eShopWeb.PublicApi.PaymentShared;

/// <summary>
/// Reads the caller's identity from the validated JWT. The buyer id is the username/email carried in
/// the token's Name claim — the same value the rest of the app uses as <c>Order.BuyerId</c>. Because
/// endpoints are protected by [Authorize], the context and identity are always present here.
/// </summary>
public static class CurrentUser
{
    public static string RequireBuyerId(IHttpContextAccessor accessor)
    {
        var name = accessor.HttpContext?.User?.Identity?.Name;
        if (string.IsNullOrEmpty(name))
            throw new InvalidOperationException("The authenticated caller has no name claim.");
        return name;
    }

    public static System.Threading.CancellationToken RequestAborted(IHttpContextAccessor accessor) =>
        accessor.HttpContext?.RequestAborted ?? System.Threading.CancellationToken.None;
}
