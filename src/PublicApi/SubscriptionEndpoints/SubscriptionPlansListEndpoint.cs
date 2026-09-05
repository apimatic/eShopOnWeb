using Ardalis.ApiEndpoints;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.eShopWeb.PublicApi.Services;
using Swashbuckle.AspNetCore.Annotations;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

[Authorize]
public class SubscriptionPlansListEndpoint : EndpointBaseAsync
    .WithoutRequest
    .WithActionResult<SubscriptionPlansListEndpoint.Response>
{
    private readonly MaxioSubscriptionService _subscriptionService;

    public SubscriptionPlansListEndpoint(MaxioSubscriptionService subscriptionService)
    {
        _subscriptionService = subscriptionService;
    }

    [HttpGet("api/subscription-plans")]
    [SwaggerOperation(
        Summary = "List available subscription plans",
        Description = "Retrieve all available subscription plans that the user can subscribe to",
        OperationId = "subscriptions.listPlans",
        Tags = new[] { "SubscriptionsEndpoints" })]
    public override async Task<ActionResult<Response>> HandleAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var plans = await _subscriptionService.ListSubscriptionPlansAsync(cancellationToken);

            return Ok(new Response
            {
                Plans = plans.Select(p => new PlanDto
                {
                    Id = p.Id,
                    Handle = p.Handle,
                    Name = p.Name,
                    Description = p.Description,
                    PriceInCents = p.PriceInCents,
                    IntervalUnit = p.IntervalUnit
                }).ToList()
            });
        }
        catch (Exception ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    public sealed record Response
    {
        public required List<PlanDto> Plans { get; set; }
    }

    public sealed record PlanDto
    {
        public int Id { get; set; }
        public required string Handle { get; set; }
        public required string Name { get; set; }
        public required string Description { get; set; }
        public long PriceInCents { get; set; }
        public required string IntervalUnit { get; set; }
    }
}
