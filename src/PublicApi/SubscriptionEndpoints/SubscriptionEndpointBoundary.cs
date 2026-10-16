using System;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Identity;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public sealed class SubscriptionEndpointBoundary
{
    private readonly UserManager<ApplicationUser> _users;
    private readonly ILogger<SubscriptionEndpointBoundary> _logger;
    public SubscriptionEndpointBoundary(UserManager<ApplicationUser> users, ILogger<SubscriptionEndpointBoundary> logger)
    { _users = users; _logger = logger; }

    public async Task<ActionResult<T>> RunAsync<T>(HttpContext context, Func<BillingShopper, Task<ActionResult<T>>> action)
    {
        try
        {
            var name = context.User.Identity?.Name;
            var id = context.User.FindFirstValue(ClaimTypes.NameIdentifier);
            var user = id != null ? await _users.FindByIdAsync(id) : name == null ? null : await _users.FindByNameAsync(name);
            if (user == null || string.IsNullOrWhiteSpace(user.Email)) return new UnauthorizedResult();
            return await action(new BillingShopper(user.Id, user.Email));
        }
        catch (BillingException ex)
        {
            return new ObjectResult(new ProblemDetails { Status = ex.StatusCode, Title = ex.Message,
                Extensions = { ["traceId"] = context.TraceIdentifier } }) { StatusCode = ex.StatusCode };
        }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested) { return new StatusCodeResult(499); }
        catch (Exception)
        {
            _logger.LogError("Subscription request could not complete. Trace {TraceId}", context.TraceIdentifier);
            return new ObjectResult(new ProblemDetails { Status = 500, Title = "The subscription request could not be completed.",
                Extensions = { ["traceId"] = context.TraceIdentifier } }) { StatusCode = 500 };
        }
    }
}
