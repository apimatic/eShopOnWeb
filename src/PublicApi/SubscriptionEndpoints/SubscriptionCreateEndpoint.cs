using System;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.ApiEndpoints;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.eShopWeb.Infrastructure.Identity;
using Swashbuckle.AspNetCore.Annotations;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
public class SubscriptionCreateEndpoint : EndpointBaseAsync
    .WithRequest<SubscriptionCreateRequest>
    .WithActionResult<SubscriptionCreateResponse>
{
    private readonly IMaxioSubscriptionService _maxioService;
    private readonly IHttpContextAccessor _httpContextAccessor;

    public SubscriptionCreateEndpoint(
        IMaxioSubscriptionService maxioService,
        IHttpContextAccessor httpContextAccessor)
    {
        _maxioService = maxioService;
        _httpContextAccessor = httpContextAccessor;
    }

    [HttpPost("api/subscriptions")]
    [SwaggerOperation(
        Summary = "Create a subscription",
        Description = "Creates a new subscription for the current user",
        OperationId = "subscriptions.create",
        Tags = new[] { "SubscriptionEndpoints" }
    )]
    public override async Task<ActionResult<SubscriptionCreateResponse>> HandleAsync(
        SubscriptionCreateRequest request,
        CancellationToken cancellationToken = default)
    {
        var response = new SubscriptionCreateResponse(request.CorrelationId());

        try
        {
            var userId = _httpContextAccessor.HttpContext?.User?.FindFirst("sub")?.Value
                ?? throw new InvalidOperationException("User ID not found in token");

            var subscription = await _maxioService.CreateSubscriptionAsync(userId, request.PlanHandle, cancellationToken);

            if (subscription == null)
            {
                return StatusCode(StatusCodes.Status500InternalServerError,
                    new { error = "Failed to create subscription" });
            }

            response.Subscription = new SubscriptionDto
            {
                Id = subscription.Id,
                State = subscription.State,
                ProductPrice = subscription.ProductPrice,
                NextBillingDate = subscription.NextBillingDate,
                CreatedAt = subscription.CreatedAt,
                CurrentPeriodEndsAt = subscription.CurrentPeriodEndsAt
            };

            return CreatedAtAction(nameof(SubscriptionGetEndpoint), new { subscriptionId = subscription.Id }, response);
        }
        catch (Exception ex)
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable,
                new { error = "Failed to create subscription", details = ex.Message });
        }
    }
}

public class SubscriptionCreateRequest : BaseRequest
{
    public string PlanHandle { get; set; } = string.Empty;
}

public class SubscriptionCreateResponse : BaseResponse
{
    public SubscriptionCreateResponse(Guid correlationId) : base(correlationId)
    {
    }

    public SubscriptionCreateResponse()
    {
    }

    public SubscriptionDto? Subscription { get; set; }
}

public class SubscriptionDto
{
    public int Id { get; set; }
    public string State { get; set; } = string.Empty;
    public decimal ProductPrice { get; set; }
    public DateTimeOffset? NextBillingDate { get; set; }
    public DateTimeOffset? CreatedAt { get; set; }
    public DateTimeOffset? CurrentPeriodEndsAt { get; set; }
}
