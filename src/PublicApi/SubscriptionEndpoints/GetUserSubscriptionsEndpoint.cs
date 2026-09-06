using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.ApiEndpoints;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.eShopWeb.PublicApi.Services;
using Swashbuckle.AspNetCore.Annotations;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class GetUserSubscriptionsEndpoint : EndpointBaseAsync
    .WithoutRequest
    .WithActionResult<GetUserSubscriptionsResponse>
{
    private readonly IMaxioService _maxioService;
    private readonly IHttpContextAccessor _httpContextAccessor;

    public GetUserSubscriptionsEndpoint(IMaxioService maxioService, IHttpContextAccessor httpContextAccessor)
    {
        _maxioService = maxioService;
        _httpContextAccessor = httpContextAccessor;
    }

    [HttpGet("api/my-subscriptions")]
    [Authorize]
    [SwaggerOperation(
        Summary = "Get the authenticated user's subscriptions",
        Description = "Get the authenticated user's subscriptions",
        OperationId = "subscriptions.list",
        Tags = new[] { "SubscriptionEndpoints" }
    )]
    public override async Task<ActionResult<GetUserSubscriptionsResponse>> HandleAsync(CancellationToken cancellationToken = default)
    {
        var context = _httpContextAccessor.HttpContext;
        if (context == null)
        {
            return BadRequest(new { message = "HTTP context not available" });
        }

        var userId = context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;

        if (string.IsNullOrEmpty(userId))
        {
            return BadRequest(new { message = "User information is missing from token" });
        }

        try
        {
            var response = await _maxioService.GetUserSubscriptionsAsync(userId, cancellationToken);
            return Ok(response);
        }
        catch (Exception ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }
}

public class GetUserSubscriptionsResponse
{
    public List<SubscriptionDetailDto> Subscriptions { get; set; } = new();
}

public class SubscriptionDetailDto
{
    public long Id { get; set; }
    public long CustomerId { get; set; }
    public string State { get; set; } = string.Empty;
    public DateTime ActivatedAt { get; set; }
    public DateTime? CurrentPeriodEndsAt { get; set; }
    public DateTime? TrialEndsAt { get; set; }
    public decimal Price { get; set; }
    public string PlanHandle { get; set; } = string.Empty;
}
