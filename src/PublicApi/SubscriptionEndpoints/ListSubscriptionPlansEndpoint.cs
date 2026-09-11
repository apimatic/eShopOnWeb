using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.ApiEndpoints;
using Microsoft.AspNetCore.Mvc;
using Microsoft.eShopWeb.PublicApi;
using Swashbuckle.AspNetCore.Annotations;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class ListSubscriptionPlansEndpoint : EndpointBaseAsync
    .WithoutRequest
    .WithActionResult<ListSubscriptionPlansResponse>
{
    private readonly IMaxioBillingService _billing;

    public ListSubscriptionPlansEndpoint(IMaxioBillingService billing) => _billing = billing;

    [HttpGet("api/subscription-plans")]
    [SwaggerOperation(Summary = "List subscription plans", Description = "Available Maxio plans", OperationId = "subscription.listPlans", Tags = new[] { "SubscriptionEndpoints" })]
    public override async Task<ActionResult<ListSubscriptionPlansResponse>> HandleAsync(CancellationToken cancellationToken = default)
    {
        var plans = await _billing.GetPlansAsync(cancellationToken);
        return new ListSubscriptionPlansResponse(plans);
    }
}

public class ListSubscriptionPlansResponse
{
    public ListSubscriptionPlansResponse(IEnumerable<MaxioAdvancedBilling.Models.Product> plans) => Plans = plans.ToList();
    public List<MaxioAdvancedBilling.Models.Product> Plans { get; set; }
}
