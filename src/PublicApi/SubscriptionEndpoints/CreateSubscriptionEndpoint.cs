using System;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.ApiEndpoints;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Swashbuckle.AspNetCore.Annotations;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Creates a new subscription for the authenticated user
/// </summary>
[Authorize]
public class CreateSubscriptionEndpoint : EndpointBaseAsync
    .WithRequest<CreateSubscriptionRequest>
    .WithActionResult<CreateSubscriptionResponse>
{
    private readonly IMaxioSubscriptionService _subscriptionService;

    public CreateSubscriptionEndpoint(IMaxioSubscriptionService subscriptionService)
    {
        _subscriptionService = subscriptionService ?? throw new ArgumentNullException(nameof(subscriptionService));
    }

    [HttpPost("api/subscriptions")]
    [SwaggerOperation(
        Summary = "Creates a new subscription",
        Description = "Creates a new subscription for the authenticated user",
        OperationId = "subscriptions.create",
        Tags = new[] { "SubscriptionEndpoints" })
    ]
    [SwaggerResponse(200, "Subscription created successfully", typeof(CreateSubscriptionResponse))]
    [SwaggerResponse(400, "Invalid request")]
    [SwaggerResponse(401, "Unauthorized")]
    [SwaggerResponse(422, "Unprocessable entity")]
    [SwaggerResponse(500, "Internal server error")]
    public override async Task<ActionResult<CreateSubscriptionResponse>> HandleAsync(
        CreateSubscriptionRequest request,
        CancellationToken cancellationToken = default)
    {
        try
        {
            if (string.IsNullOrEmpty(request.ProductHandle))
            {
                return BadRequest();
            }

            var userReference = User.FindFirst(ClaimTypes.NameIdentifier)?.Value
                ?? User.FindFirst("sub")?.Value
                ?? User.FindFirst("user_id")?.Value;

            if (string.IsNullOrEmpty(userReference))
            {
                return Unauthorized();
            }

            var subscription = await _subscriptionService.CreateSubscriptionAsync(
                userReference,
                request.ProductHandle,
                cancellationToken);

            if (subscription == null)
            {
                return StatusCode(500);
            }

            var response = new CreateSubscriptionResponse
            {
                Subscription = new SubscriptionDto
                {
                    Id = subscription.Id,
                    State = subscription.State,
                    NextBillingAt = subscription.NextBillingAt,
                    Balance = subscription.Balance,
                    ProductHandle = subscription.ProductHandle
                }
            };

            return Ok(response);
        }
        catch (InvalidOperationException)
        {
            return StatusCode(422);
        }
        catch (Exception)
        {
            return StatusCode(500);
        }
    }
}
