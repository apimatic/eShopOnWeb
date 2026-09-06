using System;
using System.Linq;
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
/// Gets subscriptions for the authenticated user
/// </summary>
[Authorize]
public class ListUserSubscriptionsEndpoint : EndpointBaseAsync
    .WithoutRequest
    .WithActionResult<ListSubscriptionsResponse>
{
    private readonly IMaxioSubscriptionService _subscriptionService;

    public ListUserSubscriptionsEndpoint(IMaxioSubscriptionService subscriptionService)
    {
        _subscriptionService = subscriptionService ?? throw new ArgumentNullException(nameof(subscriptionService));
    }

    [HttpGet("api/my-subscriptions")]
    [SwaggerOperation(
        Summary = "Lists user's subscriptions",
        Description = "Retrieves all subscriptions for the authenticated user",
        OperationId = "subscriptions.listUser",
        Tags = new[] { "SubscriptionEndpoints" })
    ]
    [SwaggerResponse(200, "Subscriptions retrieved successfully", typeof(ListSubscriptionsResponse))]
    [SwaggerResponse(401, "Unauthorized")]
    [SwaggerResponse(500, "Internal server error")]
    public override async Task<ActionResult<ListSubscriptionsResponse>> HandleAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var userReference = User.FindFirst(ClaimTypes.NameIdentifier)?.Value
                ?? User.FindFirst("sub")?.Value
                ?? User.FindFirst("user_id")?.Value;

            if (string.IsNullOrEmpty(userReference))
            {
                return Unauthorized();
            }

            var subscriptions = await _subscriptionService.GetUserSubscriptionsAsync(
                userReference,
                cancellationToken);

            var response = new ListSubscriptionsResponse();
            response.Subscriptions = subscriptions.Select(s => new SubscriptionDto
            {
                Id = s.Id,
                State = s.State,
                NextBillingAt = s.NextBillingAt,
                Balance = s.Balance,
                ProductHandle = s.ProductHandle
            }).ToArray();

            return Ok(response);
        }
        catch (Exception)
        {
            return StatusCode(500);
        }
    }
}
