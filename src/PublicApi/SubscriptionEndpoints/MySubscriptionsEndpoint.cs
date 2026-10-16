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
public class MySubscriptionsEndpoint : EndpointBaseAsync.WithoutRequest.WithActionResult<MySubscriptions>
{
    private readonly ISubscriptionBilling _billing;
    private readonly SubscriptionEndpointBoundary _boundary;
    public MySubscriptionsEndpoint(ISubscriptionBilling billing, SubscriptionEndpointBoundary boundary)
    { _billing = billing; _boundary = boundary; }

    [HttpGet("api/my-subscriptions")]
    [SwaggerOperation(Summary = "Read the authenticated shopper's subscriptions from Maxio", Tags = new[] { "Subscriptions" })]
    public override Task<ActionResult<MySubscriptions>> HandleAsync(CancellationToken cancellationToken = default) =>
        _boundary.RunAsync<MySubscriptions>(HttpContext, async shopper => Ok(await _billing.GetMineAsync(shopper, cancellationToken)));
}
