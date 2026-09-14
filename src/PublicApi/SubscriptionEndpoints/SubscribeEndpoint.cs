using System.Threading;
using System.Threading.Tasks;
using Ardalis.ApiEndpoints;
using BlazorShared.Models;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.eShopWeb.PublicApi.Maxio;
using Swashbuckle.AspNetCore.Annotations;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Subscribes the authenticated shopper to a Maxio plan. Idempotent: subscribing to a plan the shopper
/// already holds returns the existing subscription.
/// </summary>
public class SubscribeEndpoint : EndpointBaseAsync
    .WithRequest<SubscribeRequest>
    .WithActionResult<SubscribeResponse>
{
    private readonly MaxioSubscriptionService _maxio;

    public SubscribeEndpoint(MaxioSubscriptionService maxio)
    {
        _maxio = maxio;
    }

    [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
    [HttpPost("api/subscriptions")]
    [SwaggerOperation(
        Summary = "Subscribes the current shopper to a plan",
        Description = "Ensures a Maxio customer exists for the shopper and subscribes them to the requested plan",
        OperationId = "subscriptions.create",
        Tags = new[] { "SubscriptionEndpoints" })
    ]
    public override async Task<ActionResult<SubscribeResponse>> HandleAsync(SubscribeRequest request,
        CancellationToken cancellationToken = default)
    {
        var email = User.Identity?.Name;
        if (string.IsNullOrWhiteSpace(email))
        {
            return Unauthorized();
        }

        if (string.IsNullOrWhiteSpace(request.PlanHandle))
        {
            return BadRequest(new ErrorDetails
            {
                StatusCode = StatusCodes.Status400BadRequest,
                Message = "A plan handle is required."
            });
        }

        var result = await _maxio.SubscribeAsync(email, request.PlanHandle, cancellationToken);

        var response = new SubscribeResponse(request.CorrelationId())
        {
            SubscriptionCreated = result.CreatedNew,
            Subscription = result.Subscription
        };

        return new ObjectResult(response)
        {
            StatusCode = result.CreatedNew ? StatusCodes.Status201Created : StatusCodes.Status200OK
        };
    }
}
