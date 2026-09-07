using System;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.ApiEndpoints;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.JsonWebTokens;
using Swashbuckle.AspNetCore.Annotations;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Enrolls the authenticated user in a subscription plan
/// </summary>
[Authorize]
public class CreateSubscriptionEndpoint : EndpointBaseAsync
    .WithRequest<CreateSubscriptionEndpointRequest>
    .WithActionResult<CreateSubscriptionResponse>
{
    private readonly ISubscriptionService _subscriptionService;

    public CreateSubscriptionEndpoint(ISubscriptionService subscriptionService)
    {
        _subscriptionService = subscriptionService;
    }

    [HttpPost("api/subscriptions")]
    [SwaggerOperation(
        Summary = "Enrolls user in a subscription plan",
        Description = "Creates a subscription for the authenticated user in the specified plan",
        OperationId = "subscription.create",
        Tags = new[] { "SubscriptionEndpoints" })]
    public override async Task<ActionResult<CreateSubscriptionResponse>> HandleAsync(
        CreateSubscriptionEndpointRequest request,
        CancellationToken cancellationToken = default)
    {
        var response = new CreateSubscriptionResponse(Guid.NewGuid());

        try
        {
            // Extract user ID from JWT token
            var userIdClaim = HttpContext.User.FindFirst(JwtRegisteredClaimNames.Sub);
            if (userIdClaim == null)
            {
                return Unauthorized(new { error = "User ID not found in token" });
            }

            var userId = userIdClaim.Value;
            var subscription = await _subscriptionService.EnrollAsync(userId, request.PlanHandle, cancellationToken);

            response.Subscription = subscription;
            return Created($"/api/subscriptions/{subscription.Id}", response);
        }
        catch (Exception ex)
        {
            response.ErrorMessage = ex.Message;
            return StatusCode(500, response);
        }
    }
}

public class CreateSubscriptionEndpointRequest : BaseRequest
{
    public string PlanHandle { get; set; } = string.Empty;
}

public class CreateSubscriptionResponse : BaseResponse
{
    public CreateSubscriptionResponse(Guid correlationId) : base(correlationId)
    {
    }

    public SubscriptionDto? Subscription { get; set; }
    public string? ErrorMessage { get; set; }
}
