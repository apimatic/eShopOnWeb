using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.ApiEndpoints;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.eShopWeb.ApplicationCore.Services;
using Swashbuckle.AspNetCore.Annotations;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class SubscriptionPlansEndpoint : EndpointBaseAsync
    .WithoutRequest
    .WithActionResult<SubscriptionPlansResponse>
{
    private readonly ISubscriptionService _subscriptionService;

    public SubscriptionPlansEndpoint(ISubscriptionService subscriptionService)
    {
        _subscriptionService = subscriptionService;
    }

    [HttpGet("api/subscription-plans")]
    [SwaggerOperation(
        Summary = "List available subscription plans",
        Description = "Returns the list of available subscription plans",
        OperationId = "subscriptions.listPlans",
        Tags = new[] { "Subscriptions" }
    )]
    [AllowAnonymous]
    public override async Task<ActionResult<SubscriptionPlansResponse>> HandleAsync(
        CancellationToken cancellationToken = default)
    {
        var response = new SubscriptionPlansResponse { CorrelationId = Guid.NewGuid().ToString() };

        var plans = await _subscriptionService.GetSubscriptionPlans();
        if (plans == null)
        {
            return Ok(new SubscriptionPlansResponse
            {
                CorrelationId = response.CorrelationId,
                Plans = new List<SubscriptionPlanDto>(),
                Success = false,
                Message = "Failed to fetch subscription plans"
            });
        }

        response.Plans = new List<SubscriptionPlanDto>();
        foreach (var plan in plans)
        {
            response.Plans.Add(new SubscriptionPlanDto
            {
                Handle = plan.Handle,
                Name = plan.Name,
                Description = plan.Description,
                PriceInCents = plan.PriceInCents,
                Interval = plan.Interval,
                IntervalUnit = plan.IntervalUnit
            });
        }
        response.Success = true;

        return Ok(response);
    }
}

public class SubscriptionPlansResponse
{
    public string CorrelationId { get; set; } = string.Empty;
    public List<SubscriptionPlanDto> Plans { get; set; } = new();
    public bool Success { get; set; }
    public string? Message { get; set; }
}

public class SubscriptionPlanDto
{
    public string? Handle { get; set; }
    public string? Name { get; set; }
    public string? Description { get; set; }
    public long PriceInCents { get; set; }
    public int Interval { get; set; }
    public string? IntervalUnit { get; set; }
}
