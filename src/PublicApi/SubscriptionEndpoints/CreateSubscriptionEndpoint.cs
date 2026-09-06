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

[Authorize]
public class CreateSubscriptionEndpoint : EndpointBaseAsync
    .WithRequest<CreateSubscriptionApiRequest>
    .WithActionResult<CreateSubscriptionApiResponse>
{
    private readonly ISubscriptionService _subscriptionService;

    public CreateSubscriptionEndpoint(ISubscriptionService subscriptionService)
    {
        _subscriptionService = subscriptionService;
    }

    [HttpPost("api/subscriptions")]
    [SwaggerOperation(
        Summary = "Create a new subscription",
        Description = "Creates a new subscription for the authenticated user",
        OperationId = "subscriptions.create",
        Tags = new[] { "SubscriptionEndpoints" })]
    public override async Task<ActionResult<CreateSubscriptionApiResponse>> HandleAsync(
        CreateSubscriptionApiRequest request,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userId))
            {
                return Unauthorized();
            }

            var subscription = await _subscriptionService.CreateSubscriptionAsync(
                userId,
                request.PlanHandle,
                cancellationToken);

            var response = new CreateSubscriptionApiResponse
            {
                Id = subscription.Id,
                State = subscription.State,
                ProductHandle = subscription.ProductHandle,
                PricePointHandle = subscription.PricePointHandle,
                NextAssessmentAt = subscription.NextAssessmentAt
            };

            return CreatedAtAction(nameof(CreateSubscriptionEndpoint), response);
        }
        catch (Exception ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }
}

public sealed class CreateSubscriptionApiRequest
{
    [FromBody]
    public string PlanHandle { get; set; } = "";
}

public sealed class CreateSubscriptionApiResponse
{
    public int Id { get; set; }
    public string State { get; set; } = "";
    public string ProductHandle { get; set; } = "";
    public string PricePointHandle { get; set; } = "";
    public DateTimeOffset? NextAssessmentAt { get; set; }
}
