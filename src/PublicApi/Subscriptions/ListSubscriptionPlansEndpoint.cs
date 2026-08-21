using System.Threading;
using System.Threading.Tasks;
using Ardalis.ApiEndpoints;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

public sealed class ListSubscriptionPlansEndpoint : EndpointBaseAsync
    .WithoutRequest
    .WithActionResult<ListSubscriptionPlansResponse>
{
    private readonly ISubscriptionBillingService _subscriptionBillingService;

    public ListSubscriptionPlansEndpoint(ISubscriptionBillingService subscriptionBillingService)
    {
        _subscriptionBillingService = subscriptionBillingService;
    }

    [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
    [HttpGet("api/subscription-plans")]
    [SwaggerOperation(
        Summary = "Lists subscription plans",
        Description = "Lists active products in the configured Maxio product family.",
        OperationId = "subscriptions.listPlans",
        Tags = new[] { "SubscriptionEndpoints" })]
    [ProducesResponseType(typeof(ListSubscriptionPlansResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public override async Task<ActionResult<ListSubscriptionPlansResponse>> HandleAsync(
        CancellationToken cancellationToken = default)
    {
        var plans = await _subscriptionBillingService.ListPlansAsync(cancellationToken);
        return Ok(new ListSubscriptionPlansResponse { Plans = plans });
    }
}
