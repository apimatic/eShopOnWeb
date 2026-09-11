using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.ApiEndpoints;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.eShopWeb.PublicApi.Services;
using Swashbuckle.AspNetCore.Annotations;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

[Authorize]
public class GetMySubscriptionsEndpoint : EndpointBaseAsync
    .WithoutRequest
    .WithActionResult<List<SubscribeResponse>>
{
    private readonly IMaxioService _maxio;
    public GetMySubscriptionsEndpoint(IMaxioService maxio) => _maxio = maxio;

    [HttpGet("api/my-subscriptions")]
    [SwaggerOperation(
        Summary = "Get current user's subscriptions",
        Tags = new[] { "SubscriptionEndpoints" })]
    public override async Task<ActionResult<List<SubscribeResponse>>> HandleAsync(CancellationToken cancellationToken = default)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub") ?? "unknown";
        var subs = await _maxio.GetMySubscriptionsAsync(userId, cancellationToken);
        return Ok(subs.Select(s => new SubscribeResponse
        {
            SubscriptionId = s.Id,
            PlanHandle = s.PlanHandle,
            PlanName = s.PlanName,
            State = s.State,
            NextBillingDate = s.NextBillingDate,
            Price = s.PriceInCents / 100m
        }).ToList());
    }
}
