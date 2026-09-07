using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.ApiEndpoints;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.JsonWebTokens;
using Swashbuckle.AspNetCore.Annotations;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Lists subscriptions for the authenticated user
/// </summary>
[Authorize]
public class ListMySubscriptionsEndpoint : EndpointBaseAsync
    .WithoutRequest
    .WithActionResult<ListMySubscriptionsResponse>
{
    private readonly ISubscriptionService _subscriptionService;

    public ListMySubscriptionsEndpoint(ISubscriptionService subscriptionService)
    {
        _subscriptionService = subscriptionService;
    }

    [HttpGet("api/my-subscriptions")]
    [SwaggerOperation(
        Summary = "Lists authenticated user's subscriptions",
        Description = "Returns all subscriptions for the authenticated user",
        OperationId = "subscription.listMy",
        Tags = new[] { "SubscriptionEndpoints" })]
    public override async Task<ActionResult<ListMySubscriptionsResponse>> HandleAsync(
        CancellationToken cancellationToken = default)
    {
        var response = new ListMySubscriptionsResponse(Guid.NewGuid());

        try
        {
            // Extract user ID from JWT token
            var userIdClaim = HttpContext.User.FindFirst(JwtRegisteredClaimNames.Sub);
            if (userIdClaim == null)
            {
                return Unauthorized(new { error = "User ID not found in token" });
            }

            var userId = userIdClaim.Value;
            var subscriptions = await _subscriptionService.ListUserSubscriptionsAsync(userId, cancellationToken);
            response.Subscriptions = subscriptions;
            return Ok(response);
        }
        catch (Exception ex)
        {
            response.ErrorMessage = ex.Message;
            return StatusCode(500, response);
        }
    }
}

public class ListMySubscriptionsResponse : BaseResponse
{
    public ListMySubscriptionsResponse(Guid correlationId) : base(correlationId)
    {
    }

    public IReadOnlyList<SubscriptionDto> Subscriptions { get; set; } = new List<SubscriptionDto>();
    public string? ErrorMessage { get; set; }
}
