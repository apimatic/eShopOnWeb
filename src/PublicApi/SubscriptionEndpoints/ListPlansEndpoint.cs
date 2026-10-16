using System.Threading;
using System.Threading.Tasks;
using Ardalis.ApiEndpoints;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Swashbuckle.AspNetCore.Annotations;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
public class ListPlansEndpoint : EndpointBaseAsync.WithoutRequest.WithActionResult<SubscriptionPlans>
{
    private readonly ISubscriptionBilling _billing;
    private readonly SubscriptionEndpointBoundary _boundary;
    public ListPlansEndpoint(ISubscriptionBilling billing, SubscriptionEndpointBoundary boundary)
    { _billing = billing; _boundary = boundary; }

    [HttpGet("api/subscription-plans")]
    [SwaggerOperation(Summary = "List available subscription plans", Tags = new[] { "Subscriptions" })]
    public override Task<ActionResult<SubscriptionPlans>> HandleAsync(CancellationToken cancellationToken = default) =>
        _boundary.RunAsync<SubscriptionPlans>(HttpContext, async _ => Ok(await _billing.GetPlansAsync(cancellationToken)));
}
