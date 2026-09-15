using System.Threading;
using System.Threading.Tasks;
using System.Linq;
using System;
using Ardalis.ApiEndpoints;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

public sealed class SubscriptionPlansEndpoint : EndpointBaseAsync
    .WithoutRequest
    .WithActionResult<SubscriptionPlansResponse>
{
    private readonly MaxioSubscriptionService _service;

    public SubscriptionPlansEndpoint(MaxioSubscriptionService service) => _service = service;

    [HttpGet("api/subscription-plans")]
    [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
    [SwaggerOperation(Summary = "Lists available recurring subscription plans", Tags = new[] { "Subscriptions" })]
    public override async Task<ActionResult<SubscriptionPlansResponse>> HandleAsync(
        CancellationToken cancellationToken = default)
    {
        try
        {
            return Ok(new SubscriptionPlansResponse(Guid.NewGuid())
            {
                Plans = (await _service.GetPlansAsync(cancellationToken)).ToList()
            });
        }
        catch (MaxioSubscriptionException exception)
        {
            return StatusCode(exception.StatusCode, new { message = exception.Message });
        }
    }
}
