using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.ApiEndpoints;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Swashbuckle.AspNetCore.Annotations;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Lists the subscription plans a shopper can enroll in.
///
/// These endpoints follow the Ardalis.ApiEndpoints convention already used by
/// <see cref="AuthEndpoints.AuthenticateEndpoint"/> rather than the minimal-API convention used by
/// the catalog endpoints: the subscription flow needs per-request services (the billing client and
/// the Identity user manager) plus the request's cancellation token, which constructor injection
/// on a per-request endpoint gives directly.
/// </summary>
public class SubscriptionPlanListEndpoint : EndpointBaseAsync
    .WithoutRequest
    .WithActionResult<ListSubscriptionPlansResponse>
{
    private readonly ISubscriptionBillingService _subscriptionBillingService;

    public SubscriptionPlanListEndpoint(ISubscriptionBillingService subscriptionBillingService)
    {
        _subscriptionBillingService = subscriptionBillingService;
    }

    [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
    [HttpGet("api/subscription-plans")]
    [SwaggerOperation(
        Summary = "Lists available subscription plans",
        Description = "Returns the recurring plans published by the billing system of record.",
        OperationId = "subscriptions.listPlans",
        Tags = new[] { "SubscriptionEndpoints" })
    ]
    public override async Task<ActionResult<ListSubscriptionPlansResponse>> HandleAsync(CancellationToken cancellationToken = default)
    {
        var response = new ListSubscriptionPlansResponse();

        var plans = await _subscriptionBillingService.ListPlansAsync(cancellationToken);
        response.SubscriptionPlans.AddRange(plans.Select(p => p.ToDto()));

        return Ok(response);
    }
}
