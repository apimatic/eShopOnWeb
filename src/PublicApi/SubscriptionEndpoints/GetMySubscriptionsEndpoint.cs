using System.Collections.Generic;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.ApiEndpoints;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.eShopWeb.ApplicationCore.Services;
using Swashbuckle.AspNetCore.Annotations;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class GetMySubscriptionsEndpoint : EndpointBaseAsync
    .WithoutRequest
    .WithActionResult<List<MySubscriptionResponse>>
{
    private readonly ISubscriptionService _service;
    public GetMySubscriptionsEndpoint(ISubscriptionService service) => _service = service;

    [Authorize]
    [HttpGet("api/my-subscriptions")]
    [SwaggerOperation(Summary = "Get current user's subscriptions", Tags = new[] { "SubscriptionEndpoints" })]
    public override async Task<ActionResult<List<MySubscriptionResponse>>> HandleAsync(CancellationToken cancellationToken = default)
    {
        var userName = User.Identity?.Name;
        if (string.IsNullOrEmpty(userName))
            return Unauthorized();

        var subs = await _service.GetMySubscriptionsAsync(userName);
        return Ok(subs.ConvertAll(s => new MySubscriptionResponse(s.PlanHandle, s.PlanName, s.State, s.NextBillingDate)));
    }
}

public record MySubscriptionResponse(string PlanHandle, string PlanName, string State, string NextBillingDate);
