using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.ApiEndpoints;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.eShopWeb.Infrastructure.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
public sealed class ListSubscriptionPlansEndpoint(SubscriptionService service) : EndpointBaseAsync.WithoutRequest.WithActionResult<PlanList>
{
    [HttpGet("api/subscription-plans")]
    public override Task<ActionResult<PlanList>> HandleAsync(CancellationToken cancellationToken = default) =>
        BillingEndpoint.RunAsync(() => service.PlansAsync(cancellationToken), HttpContext);
}

[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
public sealed class SubscribeEndpoint(SubscriptionService service, UserManager<ApplicationUser> users) : EndpointBaseAsync.WithRequest<SubscribeRequest>.WithActionResult<SubscribeResult>
{
    [HttpPost("api/subscriptions")]
    [ProducesResponseType(typeof(SubscribeResult), 200)]
    [ProducesResponseType(typeof(SubscribeResult), 202)]
    public override async Task<ActionResult<SubscribeResult>> HandleAsync(SubscribeRequest request, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.ProductHandle) || string.IsNullOrWhiteSpace(request.FirstName) || string.IsNullOrWhiteSpace(request.LastName))
            return BadRequest(new ProblemDetails { Title = "Product handle, first name and last name are required.", Status = 400 });
        var user = await users.GetUserAsync(User);
        if (user == null) return Unauthorized();
        var result = await BillingEndpoint.RunAsync(() => service.SubscribeAsync(user, request, cancellationToken), HttpContext);
        if (result.Value?.Status == "pending")
        {
            Response.Headers.RetryAfter = "5";
            return Accepted("/api/my-subscriptions", result.Value);
        }
        return result;
    }
}

[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
public sealed class MySubscriptionsEndpoint(SubscriptionService service, UserManager<ApplicationUser> users) : EndpointBaseAsync.WithoutRequest.WithActionResult<IReadOnlyList<SubscriptionView>>
{
    [HttpGet("api/my-subscriptions")]
    public override async Task<ActionResult<IReadOnlyList<SubscriptionView>>> HandleAsync(CancellationToken cancellationToken = default)
    {
        var user = await users.GetUserAsync(User);
        if (user == null) return Unauthorized();
        return await BillingEndpoint.RunAsync(() => service.MineAsync(user, cancellationToken), HttpContext);
    }
}

internal static class BillingEndpoint
{
    public static async Task<ActionResult<T>> RunAsync<T>(Func<Task<T>> work, HttpContext context)
    {
        try { return new ActionResult<T>(await work()); }
        catch (BillingException ex)
        {
            return Problem<T>(context, ex.StatusCode, ex.Message);
        }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
        { return Problem<T>(context, 499, "The request was canceled. Enrollment may still be pending."); }
        catch (Exception ex)
        {
            context.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("BillingEndpoint")
                .LogError("Billing request could not complete; kind {Kind}, trace {TraceId}", ex.GetType().Name, context.TraceIdentifier);
            return Problem<T>(context, 500, "Subscription billing could not complete. Check your account before retrying.");
        }
    }

    private static ActionResult<T> Problem<T>(HttpContext context, int status, string message)
    {
        var problem = new ProblemDetails { Status = status, Title = message };
        problem.Extensions["traceId"] = context.TraceIdentifier;
        return new ObjectResult(problem) { StatusCode = status };
    }
}
