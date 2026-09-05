using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.ApiEndpoints;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Identity;
using Microsoft.eShopWeb.PublicApi.Services;
using Microsoft.AspNetCore.Identity;
using Swashbuckle.AspNetCore.Annotations;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class GetMySubscriptionsEndpoint : EndpointBaseAsync
    .WithoutRequest
    .WithActionResult<GetMySubscriptionsResponse>
{
    private readonly IMaxioService _maxioService;
    private readonly IRepository<Subscription> _subscriptionRepository;
    private readonly UserManager<ApplicationUser> _userManager;

    public GetMySubscriptionsEndpoint(
        IMaxioService maxioService,
        IRepository<Subscription> subscriptionRepository,
        UserManager<ApplicationUser> userManager)
    {
        _maxioService = maxioService;
        _subscriptionRepository = subscriptionRepository;
        _userManager = userManager;
    }

    [HttpGet("api/my-subscriptions")]
    [Authorize]
    [SwaggerOperation(
        Summary = "Get user's subscriptions",
        Description = "Returns the authenticated user's active subscriptions",
        OperationId = "subscriptions.get-mine",
        Tags = new[] { "SubscriptionEndpoints" }
    )]
    public override async Task<ActionResult<GetMySubscriptionsResponse>> HandleAsync(CancellationToken cancellationToken = default)
    {
        var response = new GetMySubscriptionsResponse();

        try
        {
            var userIdClaim = (HttpContext.User.FindFirst(ClaimTypes.NameIdentifier)?.Value
                ?? HttpContext.User.FindFirst("sub")?.Value);

            if (string.IsNullOrEmpty(userIdClaim))
            {
                response.Success = false;
                response.Error = "User not found in token";
                return Unauthorized(response);
            }

            var user = await _userManager.FindByIdAsync(userIdClaim);
            if (user == null)
            {
                response.Success = false;
                response.Error = "User not found";
                return NotFound(response);
            }

            var dbSubscriptions = await _subscriptionRepository.ListAsync(
                new Microsoft.eShopWeb.ApplicationCore.Specifications.UserSubscriptionsSpec(user.Id));

            if (!dbSubscriptions.Any())
            {
                response.Success = true;
                response.Subscriptions = new List<UserSubscriptionDto>();
                return Ok(response);
            }

            var subscriptions = new List<UserSubscriptionDto>();
            foreach (var sub in dbSubscriptions)
            {
                subscriptions.Add(new UserSubscriptionDto
                {
                    Id = sub.MaxioSubscriptionId,
                    PlanHandle = sub.PlanHandle,
                    State = sub.State,
                    CreatedAt = sub.CreatedAt,
                    CurrentPeriodEndsAt = sub.CurrentPeriodEndsAt
                });
            }

            response.Success = true;
            response.Subscriptions = subscriptions;
            return Ok(response);
        }
        catch (Exception ex)
        {
            response.Success = false;
            response.Error = ex.Message;
            return BadRequest(response);
        }
    }
}

public class UserSubscriptionDto
{
    public int Id { get; set; }
    public string PlanHandle { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime? CurrentPeriodEndsAt { get; set; }
}

public class GetMySubscriptionsResponse : BaseResponse
{
    public GetMySubscriptionsResponse() { }
    public GetMySubscriptionsResponse(Guid correlationId) : base(correlationId) { }

    public bool Success { get; set; }
    public string Error { get; set; } = string.Empty;
    public List<UserSubscriptionDto> Subscriptions { get; set; } = new();
}
