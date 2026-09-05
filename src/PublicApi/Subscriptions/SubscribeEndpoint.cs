using System.Threading;
using System.Threading.Tasks;
using Ardalis.ApiEndpoints;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

public sealed class SubscribeEndpoint : EndpointBaseAsync
    .WithRequest<SubscribeRequest>
    .WithActionResult<SubscribeResponse>
{
    private readonly MaxioSubscriptionService _service;

    public SubscribeEndpoint(MaxioSubscriptionService service) => _service = service;

    [HttpPost("api/subscriptions")]
    [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
    [SwaggerOperation(Summary = "Enrolls the authenticated shopper in a recurring plan", Tags = new[] { "Subscriptions" })]
    public override async Task<ActionResult<SubscribeResponse>> HandleAsync(
        SubscribeRequest request,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var subscription = await _service.SubscribeAsync(request.ProductHandle, cancellationToken);
            return Ok(new SubscribeResponse(request.CorrelationId()) { Subscription = subscription });
        }
        catch (MaxioSubscriptionException exception)
        {
            return StatusCode(exception.StatusCode, new { message = exception.Message });
        }
    }
}
