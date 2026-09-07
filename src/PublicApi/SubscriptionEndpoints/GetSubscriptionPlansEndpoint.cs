using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.ApiEndpoints;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.eShopWeb.Infrastructure.Services;
using Swashbuckle.AspNetCore.Annotations;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

[Authorize]
public class GetSubscriptionPlansEndpoint : EndpointBaseAsync
    .WithoutRequest
    .WithActionResult<GetSubscriptionPlansResponse>
{
    private readonly IMaxioSubscriptionService _subscriptionService;

    public GetSubscriptionPlansEndpoint(IMaxioSubscriptionService subscriptionService)
    {
        _subscriptionService = subscriptionService;
    }

    [HttpGet("api/subscription-plans")]
    [SwaggerOperation(
        Summary = "Get available subscription plans",
        Description = "Returns the list of available subscription plans",
        OperationId = "subscription.plans",
        Tags = new[] { "SubscriptionEndpoints" })]
    public override async Task<ActionResult<GetSubscriptionPlansResponse>> HandleAsync(
        CancellationToken cancellationToken = default)
    {
        try
        {
            var plans = await _subscriptionService.GetAvailablePlansAsync(cancellationToken);
            return new GetSubscriptionPlansResponse
            {
                Plans = plans.ConvertAll(p => new SubscriptionPlanResponse
                {
                    Id = p.Id,
                    Name = p.Name,
                    Handle = p.Handle,
                    Description = p.Description,
                    PriceInCents = p.PriceInCents,
                    Interval = p.Interval,
                    IntervalUnit = p.IntervalUnit.ToString()
                })
            };
        }
        catch (Exception ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }
}

public class GetSubscriptionPlansResponse
{
    public List<SubscriptionPlanResponse> Plans { get; set; } = new();
}

public class SubscriptionPlanResponse
{
    public int Id { get; set; }
    public string Name { get; set; } = null!;
    public string Handle { get; set; } = null!;
    public string Description { get; set; } = null!;
    public long PriceInCents { get; set; }
    public int Interval { get; set; }
    public string IntervalUnit { get; set; } = null!;
}
