using System;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.ApiEndpoints;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.eShopWeb.PublicApi.Services;
using Swashbuckle.AspNetCore.Annotations;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class CreateSubscriptionEndpoint : EndpointBaseAsync
    .WithRequest<CreateSubscriptionRequest>
    .WithActionResult<CreateSubscriptionResponse>
{
    private readonly IMaxioService _maxioService;
    private readonly IHttpContextAccessor _httpContextAccessor;

    public CreateSubscriptionEndpoint(IMaxioService maxioService, IHttpContextAccessor httpContextAccessor)
    {
        _maxioService = maxioService;
        _httpContextAccessor = httpContextAccessor;
    }

    [HttpPost("api/subscriptions")]
    [Authorize]
    [SwaggerOperation(
        Summary = "Create a subscription for the authenticated user",
        Description = "Create a subscription for the authenticated user",
        OperationId = "subscriptions.create",
        Tags = new[] { "SubscriptionEndpoints" }
    )]
    public override async Task<ActionResult<CreateSubscriptionResponse>> HandleAsync(
        CreateSubscriptionRequest request,
        CancellationToken cancellationToken = default)
    {
        var context = _httpContextAccessor.HttpContext;
        if (context == null)
        {
            return BadRequest(new { message = "HTTP context not available" });
        }

        var userId = context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        var email = context.User.FindFirst(ClaimTypes.Email)?.Value;
        var firstName = context.User.FindFirst("FirstName")?.Value ?? "User";
        var lastName = context.User.FindFirst("LastName")?.Value ?? context.User.Identity?.Name ?? "Account";

        if (string.IsNullOrEmpty(userId) || string.IsNullOrEmpty(email))
        {
            return BadRequest(new { message = "User information is incomplete in token" });
        }

        try
        {
            var response = await _maxioService.CreateSubscriptionAsync(
                userId,
                email,
                firstName,
                lastName,
                request.PlanHandle,
                cancellationToken);

            return CreatedAtAction(nameof(HandleAsync), new { id = response.SubscriptionId }, response);
        }
        catch (Exception ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }
}

public class CreateSubscriptionRequest
{
    public string PlanHandle { get; set; } = string.Empty;
}

public class CreateSubscriptionResponse
{
    public long SubscriptionId { get; set; }
    public long CustomerId { get; set; }
    public string State { get; set; } = string.Empty;
    public DateTime ActivatedAt { get; set; }
    public DateTime? CurrentPeriodEndsAt { get; set; }
    public decimal Price { get; set; }
    public string PlanHandle { get; set; } = string.Empty;
}
