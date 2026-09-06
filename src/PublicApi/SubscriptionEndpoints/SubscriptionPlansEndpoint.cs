using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.ApiEndpoints;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.eShopWeb.PublicApi.Services;
using Microsoft.Extensions.Options;
using Swashbuckle.AspNetCore.Annotations;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// List available subscription plans
/// </summary>
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
public class SubscriptionPlansEndpoint : EndpointBaseAsync
    .WithoutRequest
    .WithActionResult<SubscriptionPlansResponse>
{
    private readonly IMaxioClient _maxioClient;
    private readonly IOptions<MaxioConfiguration> _config;

    public SubscriptionPlansEndpoint(IMaxioClient maxioClient, IOptions<MaxioConfiguration> config)
    {
        _maxioClient = maxioClient;
        _config = config;
    }

    [HttpGet("api/subscription-plans")]
    [SwaggerOperation(
        Summary = "List subscription plans",
        Description = "Returns available subscription plans from the configured product family",
        OperationId = "subscriptions.listPlans",
        Tags = new[] { "SubscriptionEndpoints" })
    ]
    public override async Task<ActionResult<SubscriptionPlansResponse>> HandleAsync(CancellationToken cancellationToken = default)
    {
        var response = new SubscriptionPlansResponse();

        try
        {
            var products = await _maxioClient.GetProductsByFamilyHandleAsync(_config.Value.ProductFamilyHandle ?? "");

            response.Plans = products.Select(p => new SubscriptionPlanDto
            {
                Id = p.Id,
                Name = p.Name,
                Handle = p.Handle,
                Description = p.Description,
                PriceInCents = p.PriceInCents,
                Price = p.PriceInCents / 100m,
                Interval = p.Interval,
                IntervalUnit = p.IntervalUnit
            }).ToList();

            response.Success = true;
        }
        catch (Exception ex)
        {
            response.Success = false;
            response.Message = $"Failed to retrieve subscription plans: {ex.Message}";
            return BadRequest(response);
        }

        return Ok(response);
    }
}

public class SubscriptionPlansResponse : BaseResponse
{
    public bool Success { get; set; }
    public string Message { get; set; } = "";
    public List<SubscriptionPlanDto> Plans { get; set; } = new();
}

public class SubscriptionPlanDto
{
    public int Id { get; set; }
    public string? Name { get; set; }
    public string? Handle { get; set; }
    public string? Description { get; set; }
    public long PriceInCents { get; set; }
    public decimal Price { get; set; }
    public int Interval { get; set; }
    public string? IntervalUnit { get; set; }
}
