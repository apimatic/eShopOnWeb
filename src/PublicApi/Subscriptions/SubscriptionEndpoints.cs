using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.ApiEndpoints;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.eShopWeb.Infrastructure.Identity;
using Microsoft.Extensions.Logging;
using Swashbuckle.AspNetCore.Annotations;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
public class SubscriptionPlansEndpoint(SubscriptionService service) : EndpointBaseAsync.WithoutRequest.WithActionResult<IReadOnlyList<SubscriptionPlan>>
{
    [HttpGet("api/subscription-plans")]
    [SwaggerOperation(Tags = new[] { "Subscriptions" })]
    public override async Task<ActionResult<IReadOnlyList<SubscriptionPlan>>> HandleAsync(CancellationToken cancellationToken = default)
    {
        try { return Ok(await service.GetPlansAsync(cancellationToken)); }
        catch (BillingException ex) { return Problem(statusCode: ex.StatusCode, title: ex.Message); }
    }
}

[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
public class SubscribeEndpoint(SubscriptionService service, UserManager<ApplicationUser> users, ILogger<SubscribeEndpoint> logger)
    : EndpointBaseAsync.WithRequest<SubscribeRequest>.WithActionResult<SubscriptionEnrollment>
{
    [HttpPost("api/subscriptions")]
    [SwaggerOperation(Tags = new[] { "Subscriptions" })]
    public override async Task<ActionResult<SubscriptionEnrollment>> HandleAsync(SubscribeRequest request, CancellationToken cancellationToken = default)
    {
        var user = User.Identity?.Name == null ? null : await users.FindByNameAsync(User.Identity.Name);
        if (user == null) return Unauthorized();
        try
        {
            var result = await service.SubscribeAsync(user, request, cancellationToken);
            return result.Status == "pending" ? Accepted("/api/my-subscriptions", result) : Ok(result);
        }
        catch (BillingException ex)
        {
            logger.LogWarning("Enrollment HTTP {Status}; trace {TraceId}", ex.StatusCode, HttpContext.TraceIdentifier);
            return Problem(statusCode: ex.StatusCode, title: ex.Message);
        }
    }
}

[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
public class MySubscriptionsEndpoint(SubscriptionService service, UserManager<ApplicationUser> users)
    : EndpointBaseAsync.WithoutRequest.WithActionResult<MySubscriptions>
{
    [HttpGet("api/my-subscriptions")]
    [SwaggerOperation(Tags = new[] { "Subscriptions" })]
    public override async Task<ActionResult<MySubscriptions>> HandleAsync(CancellationToken cancellationToken = default)
    {
        var user = User.Identity?.Name == null ? null : await users.FindByNameAsync(User.Identity.Name);
        if (user == null) return Unauthorized();
        try { return Ok(await service.GetMineAsync(user, cancellationToken)); }
        catch (BillingException ex) { return Problem(statusCode: ex.StatusCode, title: ex.Message); }
    }
}
