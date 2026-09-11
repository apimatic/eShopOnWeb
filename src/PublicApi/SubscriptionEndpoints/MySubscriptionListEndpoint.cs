using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.ApiEndpoints;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
public class MySubscriptionListEndpoint : EndpointBaseAsync
    .WithoutRequest
    .WithActionResult<List<MySubscriptionDto>>
{
    private readonly IMaxioService _maxioService;

    public MySubscriptionListEndpoint(IMaxioService maxioService)
    {
        _maxioService = maxioService;
    }

    [HttpGet("api/my-subscriptions")]
    [SwaggerOperation(
        Summary = "Lists the current user's subscriptions",
        Description = "Lists subscriptions belonging to the authenticated user in Maxio Advanced Billing",
        OperationId = "subscriptions.listMine",
        Tags = new[] { "SubscriptionEndpoints" })]
    public override async Task<ActionResult<List<MySubscriptionDto>>> HandleAsync(
        CancellationToken cancellationToken = default)
    {
        var userReference = User.FindFirstValue(ClaimTypes.Name);
        if (string.IsNullOrEmpty(userReference))
        {
            return Unauthorized();
        }

        var subscriptions = await _maxioService.ListMySubscriptionsAsync(userReference, cancellationToken);
        return Ok(subscriptions);
    }
}
