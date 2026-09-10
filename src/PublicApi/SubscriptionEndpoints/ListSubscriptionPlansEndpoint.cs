using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.ApiEndpoints;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using Swashbuckle.AspNetCore.Annotations;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Lists the subscribable plans (products in the configured Maxio product family).
/// </summary>
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
public class ListSubscriptionPlansEndpoint : EndpointBaseAsync
    .WithoutRequest
    .WithActionResult<ListSubscriptionPlansResponse>
{
    private readonly ISubscriptionBillingService _billingService;

    public ListSubscriptionPlansEndpoint(ISubscriptionBillingService billingService)
    {
        _billingService = billingService;
    }

    [HttpGet("api/subscription-plans")]
    [SwaggerOperation(
        Summary = "Lists available subscription plans",
        Description = "Lists the recurring-subscription plans available in the configured Maxio product family",
        OperationId = "subscriptions.listPlans",
        Tags = new[] { "SubscriptionEndpoints" })
    ]
    public override async Task<ActionResult<ListSubscriptionPlansResponse>> HandleAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var plans = await _billingService.GetPlansAsync(cancellationToken);
            var response = new ListSubscriptionPlansResponse
            {
                Plans = plans.Select(SubscriptionPlanModel.From).ToList()
            };
            return Ok(response);
        }
        catch (SubscriptionBillingException ex)
        {
            return SubscriptionErrorMapper.ToActionResult(ex);
        }
    }
}
