using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.ApiEndpoints;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.eShopWeb.PublicApi.Services;
using Swashbuckle.AspNetCore.Annotations;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

[Authorize]
public sealed class ListSubscriptionPlansEndpoint : EndpointBaseAsync.WithoutRequest.WithActionResult<ListSubscriptionPlansResponse>
{
    private readonly MaxioSubscriptionService _service;

    public ListSubscriptionPlansEndpoint(MaxioSubscriptionService service)
    {
        _service = service;
    }

    [HttpGet("api/subscription-plans")]
    [SwaggerOperation(
        Summary = "Lists available subscription plans",
        Description = "Gets all available subscription plans from Maxio",
        OperationId = "subscriptions.list-plans",
        Tags = new[] { "Subscriptions" })]
    [ProducesResponseType(typeof(ListSubscriptionPlansResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public override async Task<ActionResult<ListSubscriptionPlansResponse>> HandleAsync(CancellationToken cancellationToken = default)
    {
        var plans = await _service.ListPlansAsync(cancellationToken);
        var planResponses = plans.Select(p => new SubscriptionPlanResponse
        {
            Handle = p.Handle,
            Name = p.Name,
            Description = p.Description,
            PriceInCents = p.PriceInCents
        });
        return Ok(new ListSubscriptionPlansResponse(planResponses));
    }
}

public sealed class SubscriptionPlanResponse
{
    public string Handle { get; set; } = null!;
    public string Name { get; set; } = null!;
    public string? Description { get; set; }
    public long PriceInCents { get; set; }
}

public sealed class ListSubscriptionPlansResponse
{
    public ListSubscriptionPlansResponse(IEnumerable<SubscriptionPlanResponse> plans)
    {
        Plans = plans;
    }

    public IEnumerable<SubscriptionPlanResponse> Plans { get; }
}
