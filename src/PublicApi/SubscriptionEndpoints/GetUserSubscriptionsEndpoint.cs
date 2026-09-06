using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.ApiEndpoints;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class GetUserSubscriptionsEndpoint : EndpointBaseAsync
    .WithoutRequest
    .WithActionResult<GetUserSubscriptionsResponse>
{
    private readonly ISubscriptionService _subscriptionService;

    public GetUserSubscriptionsEndpoint(ISubscriptionService subscriptionService)
    {
        _subscriptionService = subscriptionService;
    }

    [Authorize]
    [HttpGet("api/my-subscriptions")]
    [SwaggerOperation(
        Summary = "Get user's subscriptions",
        Description = "Returns all active subscriptions for the authenticated user",
        OperationId = "subscriptions.list",
        Tags = new[] { "SubscriptionEndpoints" })]
    public override async Task<ActionResult<GetUserSubscriptionsResponse>> HandleAsync(
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

        try
        {
            var subscriptions = await _subscriptionService.GetUserSubscriptionsAsync(userId, cancellationToken);
            return Ok(new GetUserSubscriptionsResponse
            {
                Subscriptions = subscriptions,
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

public class GetUserSubscriptionsResponse
{
    public List<SubscriptionDto> Subscriptions { get; set; } = new();
    public string CorrelationId { get; set; } = string.Empty;
}
