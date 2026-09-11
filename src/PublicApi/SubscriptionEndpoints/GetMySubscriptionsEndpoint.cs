using System.Collections.Generic;
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
    .WithActionResult<List<object>>
{
    private readonly MaxioService _maxio;
    public GetMySubscriptionsEndpoint(MaxioService maxio) => _maxio = maxio;

    [HttpGet("api/my-subscriptions")]
    [SwaggerOperation(Summary = "My subscriptions", Tags = new[] { "Subscriptions" })]
    public override async Task<ActionResult<List<object>>> HandleAsync(CancellationToken cancellationToken = default)
    {
        var userId = User.FindFirst(ClaimTypes.Name)?.Value ?? User.Identity?.Name ?? "anonymous";
        var result = await _maxio.GetMySubscriptionsAsync(userId, cancellationToken);
        return Ok(result);
    }
}
