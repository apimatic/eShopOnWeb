using System;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Identity;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

internal static class SubscriptionEndpointSupport
{
    public static async Task<ActionResult<T>> Execute<T>(ControllerBase endpoint, UserManager<ApplicationUser> users, Func<ApplicationUser, Task<ActionResult<T>>> action)
    {
        var id = endpoint.User.FindFirstValue(ClaimTypes.NameIdentifier);
        var user = id != null ? await users.FindByIdAsync(id) :
            endpoint.User.Identity?.Name is string name ? await users.FindByNameAsync(name) : null;
        if (user == null) return endpoint.Unauthorized();
        try { return await action(user); }
        catch (BillingException ex)
        {
            return endpoint.StatusCode(ex.StatusCode, new ProblemDetails
            { Status = ex.StatusCode, Title = ex.Message, Extensions = { ["traceId"] = endpoint.HttpContext.TraceIdentifier } });
        }
    }
}
