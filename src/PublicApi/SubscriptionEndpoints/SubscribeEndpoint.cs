using System;
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
public class SubscribeEndpoint : EndpointBaseAsync
    .WithRequest<SubscribeRequest>
    .WithActionResult<SubscribeResponse>
{
    private readonly IMaxioService _maxio;
    public SubscribeEndpoint(IMaxioService maxio) => _maxio = maxio;

    [HttpPost("api/subscriptions")]
    [SwaggerOperation(
        Summary = "Subscribe to a plan",
        Tags = new[] { "SubscriptionEndpoints" })]
    public override async Task<ActionResult<SubscribeResponse>> HandleAsync([FromBody] SubscribeRequest request, CancellationToken cancellationToken = default)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub") ?? "unknown";
        var email = User.FindFirstValue(ClaimTypes.Email) ?? User.FindFirstValue("email") ?? $"{userId}@eshop.local";
        if (string.IsNullOrWhiteSpace(request.PlanHandle))
            return BadRequest("PlanHandle is required");

        var sub = await _maxio.SubscribeAsync(userId, email, request.PlanHandle, cancellationToken);
        return Ok(new SubscribeResponse
        {
            SubscriptionId = sub.Id,
            PlanHandle = sub.PlanHandle,
            PlanName = sub.PlanName,
            State = sub.State,
            NextBillingDate = sub.NextBillingDate,
            Price = sub.PriceInCents / 100m
        });
    }
}
