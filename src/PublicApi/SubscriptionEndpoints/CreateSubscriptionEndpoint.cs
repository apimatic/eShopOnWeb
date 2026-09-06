using System;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.ApiEndpoints;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class CreateSubscriptionEndpoint : EndpointBaseAsync
    .WithRequest<CreateSubscriptionInputRequest>
    .WithActionResult<CreateSubscriptionResponse>
{
    private readonly ISubscriptionService _subscriptionService;

    public CreateSubscriptionEndpoint(ISubscriptionService subscriptionService)
    {
        _subscriptionService = subscriptionService;
    }

    [Authorize]
    [HttpPost("api/subscriptions")]
    [SwaggerOperation(
        Summary = "Create a new subscription",
        Description = "Enrolls the authenticated user in a subscription plan",
        OperationId = "subscriptions.create",
        Tags = new[] { "SubscriptionEndpoints" })]
    public override async Task<ActionResult<CreateSubscriptionResponse>> HandleAsync(
        CreateSubscriptionInputRequest request,
        CancellationToken cancellationToken = default)
    {
        var userId = GetUserIdFromToken();
        if (string.IsNullOrEmpty(userId))
        {
            return Unauthorized(new ErrorResponse
            {
                Message = "User ID not found in token",
                CorrelationId = CorrelationIdFromRequest()
            });
        }

        if (string.IsNullOrEmpty(request.PlanHandle))
        {
            return BadRequest(new ErrorResponse
            {
                Message = "Plan handle is required",
                CorrelationId = CorrelationIdFromRequest()
            });
        }

        try
        {
            var subscription = await _subscriptionService.CreateSubscriptionAsync(
                userId,
                request.PlanHandle,
                cancellationToken);

            return Ok(new CreateSubscriptionResponse
            {
                Subscription = subscription,
                CorrelationId = CorrelationIdFromRequest()
            });
        }
        catch (SubscriptionException ex)
        {
            return StatusCode(StatusCodes.Status500InternalServerError, new ErrorResponse
            {
                Message = ex.Message,
                CorrelationId = CorrelationIdFromRequest()
            });
        }
    }

    private string GetUserIdFromToken()
    {
        var userIdClaim = HttpContext.User.FindFirst("sub") ??
                         HttpContext.User.FindFirst(ClaimTypes.NameIdentifier) ??
                         HttpContext.User.FindFirst("id");
        return userIdClaim?.Value ?? string.Empty;
    }

    private string CorrelationIdFromRequest()
    {
        return HttpContext.Request.HttpContext.TraceIdentifier;
    }
}

public class CreateSubscriptionInputRequest
{
    public string PlanHandle { get; set; } = string.Empty;
}

public class CreateSubscriptionResponse
{
    public SubscriptionDto Subscription { get; set; } = new();
    public string CorrelationId { get; set; } = string.Empty;
}
