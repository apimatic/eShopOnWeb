using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.ApiEndpoints;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Logging;
using Swashbuckle.AspNetCore.Annotations;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

[Authorize]
public class ListSubscriptionPlansEndpoint : EndpointBaseAsync
    .WithoutRequest
    .WithActionResult<ListSubscriptionPlansResponse>
{
    private readonly IMaxioService _maxioService;
    private readonly IReadRepository<ApplicationCore.Entities.SubscriptionAggregate.SubscriptionPlan> _planRepository;
    private readonly ApplicationCore.Configuration.MaxioSettings _maxioSettings;
    private readonly ILogger<ListSubscriptionPlansEndpoint> _logger;

    public ListSubscriptionPlansEndpoint(
        IMaxioService maxioService,
        IReadRepository<ApplicationCore.Entities.SubscriptionAggregate.SubscriptionPlan> planRepository,
        ApplicationCore.Configuration.MaxioSettings maxioSettings,
        ILogger<ListSubscriptionPlansEndpoint> logger)
    {
        _maxioService = maxioService;
        _planRepository = planRepository;
        _maxioSettings = maxioSettings;
        _logger = logger;
    }

    [HttpGet("api/subscription-plans")]
    [SwaggerOperation(
        Summary = "List subscription plans",
        Description = "Returns available subscription plans",
        OperationId = "subscriptions.listPlans",
        Tags = new[] { "SubscriptionEndpoints" })
    ]
    public override async Task<ActionResult<ListSubscriptionPlansResponse>> HandleAsync(CancellationToken cancellationToken = default)
    {
        var response = new ListSubscriptionPlansResponse();

        try
        {
            var plans = await _maxioService.ListProductsByFamilyHandleAsync(_maxioSettings.ProductFamilyHandle, cancellationToken);

            response.Plans = plans.Select(p => new SubscriptionPlanDto
            {
                MaxioProductId = p.Id,
                Handle = p.Handle ?? "",
                Name = p.Name,
                Description = p.Description,
                PricePerMonth = p.PriceInCents / 100m,
                IntervalUnit = p.IntervalUnit,
                Interval = p.Interval
            }).ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error listing subscription plans");
            return StatusCode(500, new { error = "Failed to load subscription plans" });
        }

        return Ok(response);
    }
}

public class ListSubscriptionPlansResponse
{
    public List<SubscriptionPlanDto> Plans { get; set; } = new();
}

public class SubscriptionPlanDto
{
    public int MaxioProductId { get; set; }
    public required string Handle { get; set; }
    public required string Name { get; set; }
    public string? Description { get; set; }
    public decimal PricePerMonth { get; set; }
    public string? IntervalUnit { get; set; }
    public int Interval { get; set; }
}
