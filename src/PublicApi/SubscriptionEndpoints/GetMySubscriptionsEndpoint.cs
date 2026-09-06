using System;
using System.Collections.Generic;
using System.Linq;
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

[Authorize]
public class GetMySubscriptionsEndpoint : EndpointBaseAsync
    .WithRequest<GetMySubscriptionsRequest>
    .WithActionResult<GetMySubscriptionsResponse>
{
    private readonly MaxioSubscriptionService _subscriptionService;
    private readonly IHttpContextAccessor _httpContextAccessor;

    public GetMySubscriptionsEndpoint(
        MaxioSubscriptionService subscriptionService,
        IHttpContextAccessor httpContextAccessor)
    {
        _subscriptionService = subscriptionService;
        _httpContextAccessor = httpContextAccessor;
    }

    [HttpGet("api/my-subscriptions")]
    [SwaggerOperation(
        Summary = "Get my subscriptions",
        Description = "Get all subscriptions for the authenticated user",
        OperationId = "subscriptions.listMine",
        Tags = new[] { "SubscriptionEndpoints" })
    ]
    public override async Task<ActionResult<GetMySubscriptionsResponse>> HandleAsync(
        GetMySubscriptionsRequest request,
        CancellationToken cancellationToken = default)
    {
        var response = new GetMySubscriptionsResponse(request.CorrelationId()); // request.CorrelationId is already a Guid from BaseRequest

        var user = _httpContextAccessor.HttpContext?.User;
        if (user == null)
        {
            return Unauthorized();
        }

        var userId = user.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(userId))
        {
            return BadRequest("User ID not found in token");
        }

        var subscriptions = await _subscriptionService.GetUserSubscriptionsAsync(userId, cancellationToken);

        response.Subscriptions = subscriptions
            .Select(s => new SubscriptionResponse
            {
                Id = s.Id,
                State = s.State,
                ActivatedAt = s.ActivatedAt,
                CurrentPeriodStartsAt = s.CurrentPeriodStartsAt,
                CurrentPeriodEndsAt = s.CurrentPeriodEndsAt,
                NextAssessmentAt = s.NextAssessmentAt
            })
            .ToList();

        return Ok(response);
    }
}

public class GetMySubscriptionsRequest : BaseRequest
{
}

public class GetMySubscriptionsResponse : BaseResponse
{
    public GetMySubscriptionsResponse(Guid correlationId) : base(correlationId)
    {
    }

    public IList<SubscriptionResponse> Subscriptions { get; set; } = new List<SubscriptionResponse>();
}
