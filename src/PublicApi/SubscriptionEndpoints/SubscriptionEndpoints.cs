using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.ApiEndpoints;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.eShopWeb.ApplicationCore.Billing;
using Microsoft.eShopWeb.Infrastructure.Identity;
using Microsoft.Extensions.Logging;
using Swashbuckle.AspNetCore.Annotations;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class SubscribeRequest
{
    [Required, StringLength(255)] public string ProductHandle { get; set; } = "";
    [Required, StringLength(100)] public string FirstName { get; set; } = "";
    [Required, StringLength(100)] public string LastName { get; set; } = "";
}

internal static class BillingEndpoint
{
    public static async Task<ApplicationUser?> UserAsync(ClaimsPrincipal principal, UserManager<ApplicationUser> users)
    {
        var id = principal.FindFirstValue(ClaimTypes.NameIdentifier);
        if (id is not null) return await users.FindByIdAsync(id);
        return principal.Identity?.Name is { } name ? await users.FindByNameAsync(name) : null;
    }

    public static async Task<ActionResult<T>> ExecuteAsync<T>(Func<Task<ActionResult<T>>> action, ILogger logger)
    {
        try { return await action(); }
        catch (BillingException ex)
        {
            return new ObjectResult(new ProblemDetails { Status = ex.StatusCode, Title = ex.Message,
                Extensions = { ["code"] = ex.Code, ["traceId"] = System.Diagnostics.Activity.Current?.TraceId.ToString() } }) { StatusCode = ex.StatusCode };
        }
        catch (OperationCanceledException)
        {
            return new ObjectResult(new ProblemDetails { Status = 503, Title = "Billing request timed out.", Extensions = { ["code"] = "billing_timeout" } }) { StatusCode = 503 };
        }
        catch (Exception)
        {
            logger.LogError("Billing request could not complete; trace {TraceId}", System.Diagnostics.Activity.Current?.TraceId.ToString());
            return new ObjectResult(new ProblemDetails { Status = 503, Title = "Billing is temporarily unavailable.", Extensions = { ["code"] = "billing_unavailable" } }) { StatusCode = 503 };
        }
    }
}

[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
public class SubscriptionPlansEndpoint(SubscriptionService billing, ILogger<SubscriptionPlansEndpoint> logger)
    : EndpointBaseAsync.WithoutRequest.WithActionResult<IReadOnlyList<SubscriptionPlan>>
{
    [HttpGet("api/subscription-plans")]
    [SwaggerOperation(Summary = "List available recurring plans", Tags = new[] { "Subscriptions" })]
    public override Task<ActionResult<IReadOnlyList<SubscriptionPlan>>> HandleAsync(CancellationToken cancellationToken = default) =>
        BillingEndpoint.ExecuteAsync<IReadOnlyList<SubscriptionPlan>>(async () => Ok(await billing.GetPlansAsync(cancellationToken)), logger);
}

[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
public class SubscribeEndpoint(SubscriptionService billing, UserManager<ApplicationUser> users, ILogger<SubscribeEndpoint> logger)
    : EndpointBaseAsync.WithRequest<SubscribeRequest>.WithActionResult<EnrollmentResult>
{
    [HttpPost("api/subscriptions")]
    [SwaggerOperation(Summary = "Enroll the authenticated shopper in an offered plan", Tags = new[] { "Subscriptions" })]
    [ProducesResponseType(typeof(EnrollmentResult), 200)]
    [ProducesResponseType(typeof(EnrollmentResult), 202)]
    public override Task<ActionResult<EnrollmentResult>> HandleAsync([FromBody] SubscribeRequest request, CancellationToken cancellationToken = default) =>
        BillingEndpoint.ExecuteAsync<EnrollmentResult>(async () =>
        {
            var user = await BillingEndpoint.UserAsync(User, users);
            if (user is null) return Unauthorized();
            if (string.IsNullOrWhiteSpace(user.Email)) return BadRequest("Your account needs an email address.");
            var result = await billing.SubscribeAsync(new BillingAccount(user.Id, user.Email, request.FirstName.Trim(), request.LastName.Trim()), request.ProductHandle, cancellationToken);
            if (result.Status == "pending")
            {
                Response.Headers.RetryAfter = "5";
                return Accepted("/api/my-subscriptions", result);
            }
            return Ok(result);
        }, logger);
}

[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
public class MySubscriptionsEndpoint(SubscriptionService billing, UserManager<ApplicationUser> users, ILogger<MySubscriptionsEndpoint> logger)
    : EndpointBaseAsync.WithoutRequest.WithActionResult<MySubscriptions>
{
    [HttpGet("api/my-subscriptions")]
    [SwaggerOperation(Summary = "Read the authenticated shopper's current subscriptions from Maxio", Tags = new[] { "Subscriptions" })]
    public override Task<ActionResult<MySubscriptions>> HandleAsync(CancellationToken cancellationToken = default) =>
        BillingEndpoint.ExecuteAsync<MySubscriptions>(async () =>
        {
            var user = await BillingEndpoint.UserAsync(User, users);
            return user is null ? Unauthorized() : Ok(await billing.GetMineAsync(user.Id, cancellationToken));
        }, logger);
}
