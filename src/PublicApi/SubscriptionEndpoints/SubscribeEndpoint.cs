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
    private readonly MaxioService _maxio;
    public SubscribeEndpoint(MaxioService maxio) => _maxio = maxio;

    [HttpPost("api/subscriptions")]
    [SwaggerOperation(Summary = "Subscribe to a plan", Tags = new[] { "Subscriptions" })]
    public override async Task<ActionResult<SubscribeResponse>> HandleAsync([FromBody] SubscribeRequest request, CancellationToken cancellationToken = default)
    {
        var userId = User.FindFirst(ClaimTypes.Name)?.Value ?? User.Identity?.Name ?? "anonymous";
        await _maxio.SubscribeAsync(userId, request.PlanHandle, cancellationToken);
        return Ok(new SubscribeResponse { CustomerReference = userId, PlanHandle = request.PlanHandle, State = "active" });
    }
}
