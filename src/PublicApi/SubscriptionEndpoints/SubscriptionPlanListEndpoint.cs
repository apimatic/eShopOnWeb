using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.ApiEndpoints;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Swashbuckle.AspNetCore.Annotations;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class SubscriptionPlanListEndpoint : EndpointBaseAsync
    .WithoutRequest
    .WithActionResult<ListSubscriptionPlansResponse>
{
    private readonly IMaxioService _maxio;

    public SubscriptionPlanListEndpoint(IMaxioService maxio)
    {
        _maxio = maxio;
    }

    [HttpGet("api/subscription-plans")]
    [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
    [SwaggerOperation(
        Summary = "List subscription plans",
        Description = "Returns available subscription plans",
        OperationId = "subscriptions.plans",
        Tags = new[] { "SubscriptionEndpoints" })]
    public override async Task<ActionResult<ListSubscriptionPlansResponse>> HandleAsync(CancellationToken cancellationToken = default)
    {
        var response = new ListSubscriptionPlansResponse(Guid.NewGuid());
        var plans = await _maxio.GetSubscriptionPlansAsync();
        response.Plans = plans.Select(p => new SubscriptionPlanDto
        {
            Handle = p.Handle,
            Name = p.Name,
            PriceInCents = p.PriceInCents,
            Currency = p.Currency,
            Interval = p.Interval,
            IntervalUnit = p.IntervalUnit
        }).ToList();
        return Ok(response);
    }
}

public class ListSubscriptionPlansResponse : BaseResponse
{
    public ListSubscriptionPlansResponse(Guid correlationId) : base(correlationId) { }
    public System.Collections.Generic.List<SubscriptionPlanDto> Plans { get; set; } = new();
}

public class SubscriptionPlanDto
{
    public string Handle { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public int PriceInCents { get; set; }
    public string Currency { get; set; } = "USD";
    public int Interval { get; set; } = 1;
    public string IntervalUnit { get; set; } = "month";
}
