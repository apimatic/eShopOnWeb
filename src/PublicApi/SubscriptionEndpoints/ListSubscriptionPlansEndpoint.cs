using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.ApiEndpoints;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Swashbuckle.AspNetCore.Annotations;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

[Authorize]
public class ListSubscriptionPlansEndpoint : EndpointBaseAsync
    .WithoutRequest
    .WithActionResult<ListSubscriptionPlansResponse>
{
    private readonly IMaxioSubscriptionService _subscriptionService;

    public ListSubscriptionPlansEndpoint(IMaxioSubscriptionService subscriptionService)
    {
        _subscriptionService = subscriptionService;
    }

    [HttpGet("api/subscription-plans")]
    [SwaggerOperation(
        Summary = "List available subscription plans",
        Description = "Returns all available subscription plans from Maxio",
        OperationId = "subscriptions.listPlans",
        Tags = new[] { "Subscriptions" })]
    public override async Task<ActionResult<ListSubscriptionPlansResponse>> HandleAsync(
        CancellationToken cancellationToken = default)
    {
        var plans = await _subscriptionService.GetSubscriptionPlansAsync(cancellationToken);
        var planDtos = plans.ConvertAll(p => PlanDto.FromSubscriptionPlanDto(p));
        return Ok(new ListSubscriptionPlansResponse(planDtos));
    }
}

public record ListSubscriptionPlansResponse(List<PlanDto> Plans);

public record PlanDto(
    int Id,
    string Handle,
    string Name,
    string Description,
    decimal Price,
    int Interval,
    string IntervalUnit)
{
    public static PlanDto FromSubscriptionPlanDto(SubscriptionPlanDto dto)
    {
        var price = dto.PriceInCents / 100m;
        return new PlanDto(
            Id: dto.Id,
            Handle: dto.Handle,
            Name: dto.Name,
            Description: dto.Description,
            Price: price,
            Interval: dto.Interval,
            IntervalUnit: dto.IntervalUnit);
    }
}
