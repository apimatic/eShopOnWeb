using System;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.ApiEndpoints;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Identity;
using Microsoft.Extensions.Logging;
using Swashbuckle.AspNetCore.Annotations;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public record SubscribeRequest(string ProductHandle);

[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
public class SubscriptionPlansEndpoint(ISubscriptionBilling billing, ILogger<SubscriptionPlansEndpoint> logger)
    : EndpointBaseAsync.WithoutRequest.WithActionResult<BillingPage<SubscriptionPlan>>
{
    [HttpGet("api/subscription-plans")]
    [SwaggerOperation(Summary = "Browse available recurring plans", Tags = new[] { "Subscriptions" })]
    public override Task<ActionResult<BillingPage<SubscriptionPlan>>> HandleAsync(CancellationToken cancellationToken = default) =>
        BillingEndpointBoundary.Run(this, () => billing.GetPlansAsync(cancellationToken), logger);
}

[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
public class SubscribeEndpoint(ISubscriptionBilling billing, UserManager<ApplicationUser> users, ILogger<SubscribeEndpoint> logger)
    : EndpointBaseAsync.WithRequest<SubscribeRequest>.WithActionResult<EnrollmentResult>
{
    [HttpPost("api/subscriptions")]
    [SwaggerOperation(Summary = "Subscribe to an offered plan (repeat requests return the existing subscription)", Tags = new[] { "Subscriptions" })]
    [ProducesResponseType(typeof(EnrollmentResult), 201)]
    [ProducesResponseType(typeof(EnrollmentResult), 200)]
    [ProducesResponseType(typeof(EnrollmentResult), 202)]
    public override async Task<ActionResult<EnrollmentResult>> HandleAsync(SubscribeRequest request, CancellationToken cancellationToken = default)
    {
        var user = await BillingEndpointBoundary.UserAsync(User, users);
        if (user == null) return Unauthorized();
        var result = await BillingEndpointBoundary.Run(this,
            () => billing.SubscribeAsync(user.Id, user.Email ?? "", request.ProductHandle, cancellationToken), logger);
        if (result.Value == null) return result;
        if (result.Value.Status == "pending")
        {
            Response.Headers.RetryAfter = "3";
            return Accepted("/api/my-subscriptions", result.Value);
        }
        return result.Value.Created ? Created("/api/my-subscriptions", result.Value) : Ok(result.Value);
    }
}

[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
public class MySubscriptionsEndpoint(ISubscriptionBilling billing, UserManager<ApplicationUser> users, ILogger<MySubscriptionsEndpoint> logger)
    : EndpointBaseAsync.WithoutRequest.WithActionResult<AccountSubscriptions>
{
    [HttpGet("api/my-subscriptions")]
    [SwaggerOperation(Summary = "Read your current subscriptions from Maxio", Tags = new[] { "Subscriptions" })]
    public override async Task<ActionResult<AccountSubscriptions>> HandleAsync(CancellationToken cancellationToken = default)
    {
        var user = await BillingEndpointBoundary.UserAsync(User, users);
        if (user == null) return Unauthorized();
        return await BillingEndpointBoundary.Run(this, () => billing.GetMineAsync(user.Id, cancellationToken), logger);
    }
}

internal static class BillingEndpointBoundary
{
    public static async Task<ApplicationUser?> UserAsync(ClaimsPrincipal principal, UserManager<ApplicationUser> users)
    {
        var id = principal.FindFirstValue(ClaimTypes.NameIdentifier);
        if (id != null) return await users.FindByIdAsync(id);
        return principal.Identity?.Name is { } name ? await users.FindByNameAsync(name) : null;
    }

    public static async Task<ActionResult<T>> Run<T>(ControllerBase endpoint, Func<Task<T>> action, ILogger logger)
    {
        try { return await action(); }
        catch (BillingException ex)
        {
            logger.LogWarning("Billing request returned {Status}; trace {TraceId}.", ex.StatusCode, endpoint.HttpContext.TraceIdentifier);
            return endpoint.Problem(statusCode: ex.StatusCode, title: ex.Message);
        }
        catch (OperationCanceledException) when (endpoint.HttpContext.RequestAborted.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            // The app's legacy middleware echoes exception messages; keep billing data inside this boundary.
            logger.LogError("Billing request failed with {ErrorType}; trace {TraceId}.", ex.GetType().Name, endpoint.HttpContext.TraceIdentifier);
            return endpoint.Problem(statusCode: 500, title: "Billing could not complete the request.");
        }
    }
}
