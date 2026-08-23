using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.ApiEndpoints;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using Swashbuckle.AspNetCore.Annotations;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public sealed class SubscriptionPlansEndpoint : EndpointBaseAsync
    .WithoutRequest
    .WithActionResult<SubscriptionPlansResponse>
{
    private readonly IRecurringSubscriptionService _subscriptions;

    public SubscriptionPlansEndpoint(IRecurringSubscriptionService subscriptions) => _subscriptions = subscriptions;

    [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
    [HttpGet("api/subscription-plans")]
    [SwaggerOperation(
        Summary = "Lists recurring subscription plans",
        OperationId = "subscriptions.listPlans",
        Tags = new[] { "SubscriptionEndpoints" })]
    public override async Task<ActionResult<SubscriptionPlansResponse>> HandleAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var plans = await _subscriptions.ListPlansAsync(cancellationToken);
            return Ok(new SubscriptionPlansResponse { Plans = plans.Select(SubscriptionPlanDto.From).ToArray() });
        }
        catch (BillingProviderException exception)
        {
            return SubscriptionEndpointErrors.ToActionResult<SubscriptionPlansResponse>(this, exception);
        }
    }
}
