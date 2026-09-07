using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.ApiEndpoints;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.PublicApi.Maxio;
using Swashbuckle.AspNetCore.Annotations;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

[Authorize]
public class GetMySubscriptionsEndpoint : EndpointBaseAsync
    .WithoutRequest
    .WithActionResult<IEnumerable<SubscriptionDto>>
{
    private readonly ISubscriptionService _subscriptionService;
    private readonly IHttpContextAccessor _httpContextAccessor;

    public GetMySubscriptionsEndpoint(
        ISubscriptionService subscriptionService,
        IHttpContextAccessor httpContextAccessor)
    {
        _subscriptionService = subscriptionService;
        _httpContextAccessor = httpContextAccessor;
    }

    [HttpGet("api/my-subscriptions")]
    [SwaggerOperation(
        Summary = "Get user's subscriptions",
        Description = "Retrieves the logged-in user's active subscriptions",
        OperationId = "subscriptions.my-subscriptions",
        Tags = new[] { "Subscriptions" })]
    public override async Task<ActionResult<IEnumerable<SubscriptionDto>>> HandleAsync(
        CancellationToken cancellationToken = default)
    {
        var userId = _httpContextAccessor.HttpContext?.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(userId))
        {
            return Unauthorized();
        }

        var subscriptions = await _subscriptionService.GetUserSubscriptionsAsync(userId, cancellationToken);
        return Ok(subscriptions);
    }
}
