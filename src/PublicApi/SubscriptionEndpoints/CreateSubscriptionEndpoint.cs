using System.Threading;
using System.Threading.Tasks;
using Ardalis.ApiEndpoints;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.eShopWeb.PublicApi.Subscriptions;
using Swashbuckle.AspNetCore.Annotations;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>Subscribes the authenticated shopper to a plan.</summary>
public class CreateSubscriptionEndpoint : EndpointBaseAsync
    .WithRequest<CreateSubscriptionRequest>
    .WithActionResult<CreateSubscriptionResponse>
{
    private readonly SubscriptionService _subscriptionService;

    public CreateSubscriptionEndpoint(SubscriptionService subscriptionService)
    {
        _subscriptionService = subscriptionService;
    }

    [HttpPost("api/subscriptions")]
    [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
    [SwaggerOperation(
        Summary = "Subscribes the current user to a plan",
        Description = "Ensures a Maxio customer exists for the authenticated user, then enrolls them in the " +
                      "requested plan. Idempotent: subscribing twice to the same plan returns the existing " +
                      "subscription instead of creating a second one.",
        OperationId = "subscriptions.create",
        Tags = new[] { "SubscriptionEndpoints" })
    ]
    public override async Task<ActionResult<CreateSubscriptionResponse>> HandleAsync(
        CreateSubscriptionRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request is null)
        {
            ModelState.AddModelError("request", "A JSON request body with a planHandle is required.");
            return ValidationProblem();
        }

        var response = new CreateSubscriptionResponse(request.CorrelationId());

        string? userReference = User.Identity?.Name;
        if (string.IsNullOrWhiteSpace(userReference))
        {
            return Unauthorized();
        }

        if (string.IsNullOrWhiteSpace(request.PlanHandle))
        {
            ModelState.AddModelError(nameof(request.PlanHandle), "A plan handle is required.");
            return ValidationProblem();
        }

        SubscriptionEnrollmentResult result;
        try
        {
            result = await _subscriptionService.SubscribeAsync(userReference, request.PlanHandle, cancellationToken);
        }
        catch (SubscriptionPlanNotFoundException)
        {
            ModelState.AddModelError(nameof(request.PlanHandle),
                $"The plan '{request.PlanHandle}' is not available for subscription.");
            return ValidationProblem();
        }

        response.Created = result.Created;
        response.Subscription = result.Subscription;
        return Ok(response);
    }
}
