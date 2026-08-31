using System.Security.Claims;
using System.Threading;
using Microsoft.AspNetCore.Http;

namespace Microsoft.eShopWeb.PublicApi.InvoiceEndpoints;

/// <summary>
/// Shared plumbing for the invoicing endpoints: the caller's validated identity and the request's
/// cancellation token, read from the current <see cref="HttpContext"/>. The endpoints implement the
/// two-argument <c>IEndpoint.HandleAsync(request, service)</c> contract, so per-request context comes
/// from here rather than from extra handler parameters.
/// </summary>
public abstract class InvoiceEndpointBase
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    protected InvoiceEndpointBase(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    protected ClaimsPrincipal? User => _httpContextAccessor.HttpContext?.User;

    protected string? CurrentUserName => User.GetUserName();

    protected CancellationToken RequestAborted =>
        _httpContextAccessor.HttpContext?.RequestAborted ?? CancellationToken.None;
}
