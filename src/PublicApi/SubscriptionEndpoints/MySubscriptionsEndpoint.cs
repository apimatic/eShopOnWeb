using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.ApiEndpoints;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public sealed class MySubscriptionsEndpoint : EndpointBaseAsync
    .WithoutRequest
    .WithActionResult<IReadOnlyList<SubscriptionDto>>
{
    private readonly ISubscriptionBillingService _billing;

    public MySubscriptionsEndpoint(ISubscriptionBillingService billing) => _billing = billing;

    [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
    [HttpGet("api/my-subscriptions")]
    [SwaggerOperation(
        Summary = "Lists the current user's subscriptions",
        Description = "Returns current subscription state directly from Maxio.",
        OperationId = "subscriptions.listMine",
        Tags = new[] { "SubscriptionEndpoints" })]
    public override async Task<ActionResult<IReadOnlyList<SubscriptionDto>>> HandleAsync(
        CancellationToken cancellationToken = default)
    {
        var user = BillingIdentity.FromPrincipal(User);
        return Ok(await _billing.ListMySubscriptionsAsync(user, cancellationToken));
    }
}
