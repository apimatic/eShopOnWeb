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

public sealed class MySubscriptionsEndpoint : EndpointBaseAsync
    .WithoutRequest
    .WithActionResult<MySubscriptionsResponse>
{
    private readonly MaxioSubscriptionService _service;

    public MySubscriptionsEndpoint(MaxioSubscriptionService service) => _service = service;

    [HttpGet("api/my-subscriptions")]
    [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
    [SwaggerOperation(Summary = "Lists subscriptions for the authenticated shopper", Tags = new[] { "Subscriptions" })]
    public override async Task<ActionResult<MySubscriptionsResponse>> HandleAsync(
        CancellationToken cancellationToken = default)
    {
        try
        {
            return Ok(new MySubscriptionsResponse(Guid.NewGuid())
            {
                Subscriptions = (await _service.GetMySubscriptionsAsync(cancellationToken)).ToList()
            });
        }
        catch (MaxioSubscriptionException exception)
        {
            return StatusCode(exception.StatusCode, new { message = exception.Message });
        }
    }
}
