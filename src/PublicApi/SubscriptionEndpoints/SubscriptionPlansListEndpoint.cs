using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.ApiEndpoints;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Swashbuckle.AspNetCore.Annotations;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
public class SubscriptionPlansListEndpoint : EndpointBaseAsync
    .WithoutRequest
    .WithActionResult<SubscriptionPlansListResponse>
{
    private readonly IMaxioSubscriptionService _maxioService;

    public SubscriptionPlansListEndpoint(IMaxioSubscriptionService maxioService)
    {
        _maxioService = maxioService;
    }

    [HttpGet("api/subscription-plans")]
    [AllowAnonymous]
    [SwaggerOperation(
        Summary = "Get available subscription plans",
        Description = "Retrieves the list of available subscription plans from Maxio",
        OperationId = "subscriptions.listPlans",
        Tags = new[] { "SubscriptionEndpoints" }
    )]
    public override async Task<ActionResult<SubscriptionPlansListResponse>> HandleAsync(
        CancellationToken cancellationToken = default)
    {
        var response = new SubscriptionPlansListResponse();

        try
        {
            var plans = await _maxioService.GetSubscriptionPlansAsync(cancellationToken);
            response.Plans = plans?.ConvertAll(p => new SubscriptionPlanDto
            {
                Id = p.Id,
                Name = p.Name,
                Handle = p.Handle,
                Price = p.Price,
                Interval = p.Interval,
                IntervalUnit = p.IntervalUnit
            }) ?? new List<SubscriptionPlanDto>();
        }
        catch (Exception ex)
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable,
                new { error = "Failed to fetch subscription plans", details = ex.Message });
        }

        return Ok(response);
    }
}

public class SubscriptionPlansListResponse : BaseResponse
{
    public SubscriptionPlansListResponse(Guid correlationId) : base(correlationId)
    {
    }

    public SubscriptionPlansListResponse()
    {
    }

    public List<SubscriptionPlanDto> Plans { get; set; } = new();
}

public class SubscriptionPlanDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Handle { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public int Interval { get; set; }
    public string IntervalUnit { get; set; } = string.Empty;
}
