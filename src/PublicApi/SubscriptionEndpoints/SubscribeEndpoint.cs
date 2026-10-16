using System.ComponentModel.DataAnnotations;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.ApiEndpoints;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Swashbuckle.AspNetCore.Annotations;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public sealed class SubscribeRequest
{
    [Required, StringLength(255)]
    public string ProductHandle { get; set; } = "";
}

[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
public class SubscribeEndpoint : EndpointBaseAsync.WithRequest<SubscribeRequest>.WithActionResult<SubscribeResult>
{
    private readonly ISubscriptionBilling _billing;
    private readonly SubscriptionEndpointBoundary _boundary;
    public SubscribeEndpoint(ISubscriptionBilling billing, SubscriptionEndpointBoundary boundary)
    { _billing = billing; _boundary = boundary; }

    [HttpPost("api/subscriptions")]
    [ProducesResponseType(typeof(SubscribeResult), 200)]
    [ProducesResponseType(typeof(SubscribeResult), 202)]
    [SwaggerOperation(Summary = "Subscribe the authenticated shopper to an offered plan", Tags = new[] { "Subscriptions" })]
    public override Task<ActionResult<SubscribeResult>> HandleAsync([FromBody] SubscribeRequest request, CancellationToken cancellationToken = default) =>
        _boundary.RunAsync<SubscribeResult>(HttpContext, async shopper =>
        {
            var result = await _billing.SubscribeAsync(shopper, request.ProductHandle, cancellationToken);
            if (result.IsPending)
            {
                Response.Headers.RetryAfter = "3";
                return Accepted("/api/my-subscriptions", result);
            }
            return Ok(result);
        });
}
