using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.eShopWeb.PublicApi.Services;
using Swashbuckle.AspNetCore.Annotations;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

[Authorize]
[ApiController]
[Route("api")]
public class ListSubscriptionPlansEndpoint : ControllerBase
{
    private readonly MaxioSubscriptionService _subscriptionService;

    public ListSubscriptionPlansEndpoint(MaxioSubscriptionService subscriptionService)
    {
        _subscriptionService = subscriptionService;
    }

    [HttpGet("subscription-plans")]
    [SwaggerOperation(
        Summary = "List subscription plans",
        Description = "Get all available subscription plans",
        OperationId = "subscription-plans.list",
        Tags = new[] { "SubscriptionEndpoints" })
    ]
    public async Task<ActionResult<ListSubscriptionPlansResponse>> ListPlans(CancellationToken cancellationToken = default)
    {
        var response = new ListSubscriptionPlansResponse(Guid.NewGuid());

        var plans = await _subscriptionService.ListAvailablePlansAsync(cancellationToken);

        response.Plans = plans
            .Select(p => new SubscriptionPlanResponse
            {
                Id = p.Id,
                Handle = p.Handle,
                Name = p.Name,
                PriceInCents = p.PriceInCents,
                PriceInDollars = p.PriceInCents / 100m,
                Interval = p.Interval,
                IntervalUnit = p.IntervalUnit
            })
            .ToList();

        return Ok(response);
    }
}

public class ListSubscriptionPlansResponse : BaseResponse
{
    public ListSubscriptionPlansResponse(Guid correlationId) : base(correlationId)
    {
    }

    public IList<SubscriptionPlanResponse> Plans { get; set; } = new List<SubscriptionPlanResponse>();
}

public class SubscriptionPlanResponse
{
    public long Id { get; set; }
    public string Handle { get; set; } = "";
    public string Name { get; set; } = "";
    public long PriceInCents { get; set; }
    public decimal PriceInDollars { get; set; }
    public int Interval { get; set; }
    public string IntervalUnit { get; set; } = "";
}
