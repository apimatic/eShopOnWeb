using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.ApiEndpoints;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.eShopWeb.Infrastructure.Services.Maxio;
using Swashbuckle.AspNetCore.Annotations;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

[Authorize]
public class GetSubscriptionPlansEndpoint : EndpointBaseAsync
    .WithoutRequest
    .WithActionResult<GetSubscriptionPlansResponse>
{
    private readonly MaxioSubscriptionService _subscriptionService;

    public GetSubscriptionPlansEndpoint(MaxioSubscriptionService subscriptionService)
    {
        _subscriptionService = subscriptionService;
    }

    [HttpGet("api/subscription-plans")]
    [SwaggerOperation(
        Summary = "Get available subscription plans",
        Description = "Returns a list of available subscription plans",
        OperationId = "subscriptions.list-plans",
        Tags = new[] { "SubscriptionEndpoints" })
    ]
    public override async Task<ActionResult<GetSubscriptionPlansResponse>> HandleAsync(
        CancellationToken cancellationToken = default)
    {
        var response = new GetSubscriptionPlansResponse();

        try
        {
            var plans = await _subscriptionService.GetSubscriptionPlansAsync();
            response.Plans = plans.ConvertAll(p => new SubscriptionPlanDto
            {
                Id = p.Id,
                Name = p.Name,
                Handle = p.Handle,
                Description = p.Description,
                PriceInCents = p.PriceInCents,
                Interval = p.Interval,
                IntervalUnit = p.IntervalUnit
            });
            response.Success = true;
        }
        catch (Exception ex)
        {
            response.Success = false;
            response.Message = $"Error retrieving subscription plans: {ex.Message}";
            return StatusCode(500, response);
        }

        return Ok(response);
    }
}

public class SubscriptionPlanDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Handle { get; set; }
    public string? Description { get; set; }
    public long PriceInCents { get; set; }
    public int Interval { get; set; }
    public string IntervalUnit { get; set; } = string.Empty;

    public decimal Price => PriceInCents / 100m;
}

public class GetSubscriptionPlansResponse
{
    public List<SubscriptionPlanDto> Plans { get; set; } = new();
    public bool Success { get; set; }
    public string? Message { get; set; }
}
