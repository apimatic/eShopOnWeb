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
public class SubscriptionPlansListEndpoint : EndpointBaseAsync
    .WithoutRequest
    .WithActionResult<SubscriptionPlansListResponse>
{
    private readonly IMaxioService _maxioService;

    public SubscriptionPlansListEndpoint(IMaxioService maxioService)
    {
        _maxioService = maxioService;
    }

    [HttpGet("api/subscription-plans")]
    [SwaggerOperation(
        Summary = "List available subscription plans",
        Description = "Retrieves all available subscription plans from the billing system",
        OperationId = "subscriptions.list-plans",
        Tags = new[] { "SubscriptionEndpoints" })
    ]
    public override async Task<ActionResult<SubscriptionPlansListResponse>> HandleAsync(CancellationToken cancellationToken = default)
    {
        var plans = await _maxioService.GetSubscriptionPlansAsync();

        var response = new SubscriptionPlansListResponse
        {
            Plans = plans.ConvertAll(p => new SubscriptionPlanDto
            {
                Id = p.Id,
                Handle = p.Handle,
                Name = p.Name,
                Price = p.Price,
                PricingScheme = p.PricingScheme,
                TrialDays = p.TrialDays,
            })
        };

        return Ok(response);
    }
}

public class SubscriptionPlansListResponse : BaseResponse
{
    public SubscriptionPlansListResponse() { }
    public SubscriptionPlansListResponse(Guid correlationId) : base(correlationId) { }

    public List<SubscriptionPlanDto> Plans { get; set; } = new();
}
