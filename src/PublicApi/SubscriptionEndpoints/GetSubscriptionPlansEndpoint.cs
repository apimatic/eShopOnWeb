using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.ApiEndpoints;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

[ApiController]
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
public sealed class GetSubscriptionPlansEndpoint : EndpointBaseAsync
    .WithoutRequest
    .WithActionResult<IReadOnlyList<SubscriptionPlanResponse>>
{
    private readonly ISubscriptionBillingService _billing;

    public GetSubscriptionPlansEndpoint(ISubscriptionBillingService billing) => _billing = billing;

    [HttpGet("api/subscription-plans")]
    [SwaggerOperation(
        Summary = "Lists the subscription plans available from Maxio",
        OperationId = "subscriptions.listPlans",
        Tags = new[] { "SubscriptionEndpoints" })]
    public override async Task<ActionResult<IReadOnlyList<SubscriptionPlanResponse>>> HandleAsync(
        CancellationToken cancellationToken = default) =>
        Ok(await _billing.ListPlansAsync(cancellationToken));
}
