using System;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.ApiEndpoints;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Swashbuckle.AspNetCore.Annotations;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

[Authorize]
public class ListUserSubscriptionsEndpoint : EndpointBaseAsync
    .WithRequest<object>
    .WithActionResult<ListUserSubscriptionsResponse>
{
    private readonly ISubscriptionService _subscriptionService;

    public ListUserSubscriptionsEndpoint(ISubscriptionService subscriptionService)
    {
        _subscriptionService = subscriptionService;
    }

    [HttpGet("api/my-subscriptions")]
    [SwaggerOperation(
        Summary = "Get authenticated user's subscriptions",
        Description = "Lists all active subscriptions for the authenticated user",
        OperationId = "subscriptions.list",
        Tags = new[] { "SubscriptionEndpoints" })]
    public override async Task<ActionResult<ListUserSubscriptionsResponse>> HandleAsync(
        object request, CancellationToken cancellationToken = default)
    {
        var response = new ListUserSubscriptionsResponse(Guid.NewGuid());

        var userIdClaim = HttpContext.User.FindFirst(ClaimTypes.NameIdentifier);
        if (userIdClaim == null)
        {
            return Unauthorized();
        }

        var userId = userIdClaim.Value;

        try
        {
            var subscriptions = await _subscriptionService.GetUserSubscriptionsAsync(userId, cancellationToken);
            response.Subscriptions.AddRange(subscriptions.Select(SubscriptionDto.FromModel));
            return Ok(response);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { error = "An error occurred retrieving subscriptions" });
        }
    }
}
