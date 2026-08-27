using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.ApiEndpoints;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public sealed class MySubscriptionListEndpoint : EndpointBaseAsync
    .WithoutRequest
    .WithActionResult<IReadOnlyList<SubscriptionDto>>
{
    private readonly ISubscriptionBillingService _billingService;

    public MySubscriptionListEndpoint(ISubscriptionBillingService billingService)
    {
        _billingService = billingService;
    }

    [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
    [HttpGet("api/my-subscriptions")]
    [SwaggerOperation(
        Summary = "Lists the authenticated shopper's subscriptions",
        OperationId = "subscriptions.listMine",
        Tags = new[] { "SubscriptionEndpoints" })]
    public override async Task<ActionResult<IReadOnlyList<SubscriptionDto>>> HandleAsync(
        CancellationToken cancellationToken = default)
    {
        return Ok(await _billingService.ListMySubscriptionsAsync(User, cancellationToken));
    }
}
