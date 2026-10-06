using System.Threading;
using System.Threading.Tasks;
using Ardalis.ApiEndpoints;
using Maxio.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Subscribes the current user to a plan. Idempotent: a repeated request for the same plan never
/// creates a second customer or subscription.
/// </summary>
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
    [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
    [SwaggerOperation(
        Summary = "Subscribes the current user to a plan",
        Description = "Subscribes the current user to a plan. Idempotent: a repeated request for the same plan never creates a second customer or subscription.",
        OperationId = "subscriptions.create",
        Tags = new[] { "SubscriptionEndpoints" })
    ]
    public override async Task<ActionResult<CreateSubscriptionResponse>> HandleAsync(CreateSubscriptionRequest request,
        CancellationToken cancellationToken = default)
    {
        var userEmail = User.Identity?.Name;
        if (string.IsNullOrWhiteSpace(userEmail))
        {
            return Unauthorized();
        }

        var subscription = await _subscriptionService.SubscribeAsync(userEmail, request.PlanHandle, cancellationToken);

        var response = new CreateSubscriptionResponse();
        response.Subscription = SubscriptionDto.FromSubscription(subscription);

        return Ok(response);
    }
}
