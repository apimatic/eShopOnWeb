using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.ApiEndpoints;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.eShopWeb.Infrastructure.Maxio;
using Microsoft.Extensions.Options;
using Swashbuckle.AspNetCore.Annotations;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Lists the subscription plans (Maxio products) available for sign-up.
/// </summary>
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
public class ListSubscriptionPlansEndpoint : EndpointBaseAsync
    .WithRequest<ListSubscriptionPlansRequest>
    .WithActionResult<ListSubscriptionPlansResponse>
{
    private readonly ISubscriptionManager _subscriptionManager;
    private readonly MaxioOptions _maxioOptions;

    public ListSubscriptionPlansEndpoint(ISubscriptionManager subscriptionManager,
        IOptions<MaxioOptions> maxioOptions)
    {
        _subscriptionManager = subscriptionManager;
        _maxioOptions = maxioOptions.Value;
    }

    [HttpGet("api/subscription-plans")]
    [SwaggerOperation(
        Summary = "Lists the available subscription plans",
        Description = "Lists the available subscription plans",
        OperationId = "subscriptions.listPlans",
        Tags = new[] { "SubscriptionEndpoints" })
    ]
    public override async Task<ActionResult<ListSubscriptionPlansResponse>> HandleAsync(
        [FromQuery] ListSubscriptionPlansRequest request, CancellationToken cancellationToken = default)
    {
        var response = new ListSubscriptionPlansResponse(request.CorrelationId())
        {
            ProductFamilyHandle = _maxioOptions.ProductFamilyHandle
        };

        var plans = await _subscriptionManager.ListPlansAsync(cancellationToken);
        response.Plans.AddRange(plans.Select(p => new SubscriptionPlanDto
        {
            Handle = p.Handle,
            Name = p.Name,
            PriceInCents = p.PriceInCents,
            Price = p.PriceInCents / 100m,
            Interval = p.Interval,
            IntervalUnit = p.IntervalUnit,
            RequiresPaymentMethod = p.RequireCreditCard
        }));

        return Ok(response);
    }
}
