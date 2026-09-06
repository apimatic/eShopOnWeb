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
public class ListSubscriptionPlansEndpoint : EndpointBaseAsync
    .WithoutRequest
    .WithActionResult<ListSubscriptionPlansResponse>
{
    private readonly IMaxioApiService _maxioApi;

    public ListSubscriptionPlansEndpoint(IMaxioApiService maxioApi)
    {
        _maxioApi = maxioApi;
    }

    [HttpGet("api/subscription-plans")]
    [SwaggerOperation(
        Summary = "List available subscription plans",
        Description = "Returns a list of subscription plans available for purchase",
        OperationId = "subscriptions.listPlans",
        Tags = new[] { "SubscriptionEndpoints" })
    ]
    [ProducesResponseType(typeof(ListSubscriptionPlansResponse), 200)]
    public override async Task<ActionResult<ListSubscriptionPlansResponse>> HandleAsync(CancellationToken cancellationToken = default)
    {
        var response = new ListSubscriptionPlansResponse(Guid.NewGuid());
        response.Plans = new List<SubscriptionPlanDto>();

        var proPlan = await _maxioApi.GetProductByHandleAsync("eshop-pro");
        if (proPlan != null)
        {
            response.Plans.Add(new SubscriptionPlanDto
            {
                Id = proPlan.Id,
                Handle = proPlan.Handle,
                Name = proPlan.Name,
                Description = proPlan.Description,
                PricePerMonth = proPlan.PriceInCents / 100m,
                BillingInterval = $"Every {proPlan.Interval} {proPlan.IntervalUnit}",
                HasTrial = proPlan.TrialInterval.HasValue && proPlan.TrialInterval > 0,
                TrialDays = proPlan.TrialInterval
            });
        }

        var basicPlan = await _maxioApi.GetProductByHandleAsync("basic-plan");
        if (basicPlan != null)
        {
            response.Plans.Add(new SubscriptionPlanDto
            {
                Id = basicPlan.Id,
                Handle = basicPlan.Handle,
                Name = basicPlan.Name,
                Description = basicPlan.Description,
                PricePerMonth = basicPlan.PriceInCents / 100m,
                BillingInterval = $"Every {basicPlan.Interval} {basicPlan.IntervalUnit}",
                HasTrial = basicPlan.TrialInterval.HasValue && basicPlan.TrialInterval > 0,
                TrialDays = basicPlan.TrialInterval
            });
        }

        return response;
    }
}

public class ListSubscriptionPlansResponse : BaseResponse
{
    public ListSubscriptionPlansResponse(Guid correlationId) : base(correlationId)
    {
    }

    public List<SubscriptionPlanDto> Plans { get; set; } = new();
}
