using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.ApiEndpoints;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.eShopWeb.ApplicationCore.Billing;
using Microsoft.eShopWeb.Infrastructure.Billing;
using Swashbuckle.AspNetCore.Annotations;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public sealed class SubscriptionPlanListEndpoint : EndpointBaseAsync
    .WithoutRequest
    .WithActionResult<SubscriptionPlansResponse>
{
    private readonly ISubscriptionBillingService _billingService;

    public SubscriptionPlanListEndpoint(ISubscriptionBillingService billingService) =>
        _billingService = billingService;

    [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
    [HttpGet("api/subscription-plans")]
    [SwaggerOperation(Summary = "Lists available subscription plans",
        OperationId = "subscriptions.listPlans", Tags = new[] { "SubscriptionEndpoints" })]
    public override async Task<ActionResult<SubscriptionPlansResponse>> HandleAsync(
        CancellationToken cancellationToken = default)
    {
        try
        {
            var plans = await _billingService.GetPlansAsync(cancellationToken);
            return Ok(new SubscriptionPlansResponse { Plans = plans.Select(SubscriptionPlanDto.From).ToList() });
        }
        catch (MaxioApiException ex)
        {
            return StatusCode(ex.IsTransient ? 503 : 502,
                new ProblemDetails { Title = "Billing service unavailable", Detail = ex.Message });
        }
    }
}
