using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.ApiEndpoints;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
public class SubscriptionGetEndpoint : EndpointBaseAsync
    .WithoutRequest
    .WithActionResult<SubscriptionListResponse>
{
    private readonly IMaxioSubscriptionService _maxioService;
    private readonly IHttpContextAccessor _httpContextAccessor;

    public SubscriptionGetEndpoint(
        IMaxioSubscriptionService maxioService,
        IHttpContextAccessor httpContextAccessor)
    {
        _maxioService = maxioService;
        _httpContextAccessor = httpContextAccessor;
    }

    [HttpGet("api/my-subscriptions")]
    [SwaggerOperation(
        Summary = "Get user subscriptions",
        Description = "Retrieves all subscriptions for the current user",
        OperationId = "subscriptions.list",
        Tags = new[] { "SubscriptionEndpoints" }
    )]
    public override async Task<ActionResult<SubscriptionListResponse>> HandleAsync(
        CancellationToken cancellationToken = default)
    {
        var response = new SubscriptionListResponse();

        try
        {
            var userId = _httpContextAccessor.HttpContext?.User?.FindFirst("sub")?.Value
                ?? throw new InvalidOperationException("User ID not found in token");

            var subscriptions = await _maxioService.GetUserSubscriptionsAsync(userId, cancellationToken);

            response.Subscriptions = subscriptions?.ConvertAll(s => new SubscriptionDto
            {
                Id = s.Id,
                State = s.State,
                ProductPrice = s.ProductPrice,
                NextBillingDate = s.NextBillingDate,
                CreatedAt = s.CreatedAt,
                CurrentPeriodEndsAt = s.CurrentPeriodEndsAt
            }) ?? new List<SubscriptionDto>();
        }
        catch (Exception ex)
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable,
                new { error = "Failed to fetch subscriptions", details = ex.Message });
        }

        return Ok(response);
    }
}

public class SubscriptionListResponse : BaseResponse
{
    public SubscriptionListResponse(Guid correlationId) : base(correlationId)
    {
    }

    public SubscriptionListResponse()
    {
    }

    public List<SubscriptionDto> Subscriptions { get; set; } = new();
}
