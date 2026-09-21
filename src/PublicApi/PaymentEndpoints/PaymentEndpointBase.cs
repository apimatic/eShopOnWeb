using System;
using System.Threading;
using Microsoft.AspNetCore.Http;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

/// <summary>
/// Shared helpers for the payment endpoints: the authenticated caller's identity (which is the
/// order/basket BuyerId) and the request-abort cancellation token, read from the current
/// <see cref="HttpContext"/>.
/// </summary>
public abstract class PaymentEndpointBase
{
    protected readonly IHttpContextAccessor HttpContextAccessor;

    protected PaymentEndpointBase(IHttpContextAccessor httpContextAccessor)
    {
        HttpContextAccessor = httpContextAccessor;
    }

    /// <summary>The signed-in shopper's identity (JWT ClaimTypes.Name == the BuyerId).</summary>
    protected string BuyerId =>
        HttpContextAccessor.HttpContext?.User?.Identity?.Name
        ?? throw new UnauthorizedAccessException("No authenticated user on the request.");

    protected CancellationToken RequestAborted =>
        HttpContextAccessor.HttpContext?.RequestAborted ?? CancellationToken.None;
}
