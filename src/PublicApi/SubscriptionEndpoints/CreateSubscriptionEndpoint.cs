using System.Threading;
using System.Threading.Tasks;
using Ardalis.ApiEndpoints;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Subscribes the authenticated user to a plan. Idempotent: subscribing twice to the same
/// plan returns the existing subscription instead of creating a second one.
/// </summary>
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
public class CreateSubscriptionEndpoint : EndpointBaseAsync
    .WithRequest<CreateSubscriptionRequest>
    .WithActionResult<CreateSubscriptionResponse>
{
    private readonly ISubscriptionService _subscriptionService;

    public CreateSubscriptionEndpoint(ISubscriptionService subscriptionService)
    {
        _subscriptionService = subscriptionService;
    }

    [HttpPost("api/subscriptions")]
    [SwaggerOperation(
        Summary = "Subscribes the authenticated user to a plan",
        Description = "Ensures a Maxio customer exists for the authenticated user and subscribes them to the requested plan. " +
                      "The call is idempotent: repeating it for an already-subscribed plan returns the existing subscription.",
        OperationId = "subscriptions.create",
        Tags = new[] { "SubscriptionEndpoints" })
    ]
    public override async Task<ActionResult<CreateSubscriptionResponse>> HandleAsync(
        [FromBody] CreateSubscriptionRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.ProductHandle))
        {
            return BadRequest(new { message = "A non-empty 'productHandle' is required." });
        }

        var userName = User.Identity?.Name;
        if (string.IsNullOrWhiteSpace(userName))
        {
            return Unauthorized();
        }

        var result = await _subscriptionService.SubscribeAsync(userName, request.ProductHandle, cancellationToken);

        switch (result.Failure)
        {
            case SubscribeFailureKind.UserNotFound:
                return NotFound(new { message = "The authenticated user does not exist." });
            case SubscribeFailureKind.PlanNotFound:
                return NotFound(new { message = result.FailureDetail });
            case SubscribeFailureKind.None when result.Subscription is not null:
                var response = new CreateSubscriptionResponse
                {
                    Subscription = result.Subscription,
                    Created = result.Created
                };
                return result.Created
                    ? StatusCode(StatusCodes.Status201Created, response)
                    : Ok(response);
            default:
                return BadRequest(new { message = "Unable to create the subscription." });
        }
    }
}
