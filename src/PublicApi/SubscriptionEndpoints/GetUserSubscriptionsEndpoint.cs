using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.ApiEndpoints;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.eShopWeb.Infrastructure.Identity;
using Microsoft.eShopWeb.Infrastructure.Services;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Swashbuckle.AspNetCore.Annotations;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class GetUserSubscriptionsEndpoint : EndpointBaseAsync.WithoutRequest.WithActionResult<GetUserSubscriptionsResponse>
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IRepository<Subscription> _subscriptionRepository;
    private readonly IMaxioService _maxioService;

    public GetUserSubscriptionsEndpoint(
        UserManager<ApplicationUser> userManager,
        IRepository<Subscription> subscriptionRepository,
        IMaxioService maxioService)
    {
        _userManager = userManager;
        _subscriptionRepository = subscriptionRepository;
        _maxioService = maxioService;
    }

    [HttpGet("api/my-subscriptions")]
    [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
    [SwaggerOperation(
        Summary = "Get user subscriptions",
        Description = "Get all subscriptions for the authenticated user",
        OperationId = "subscriptions.getByUser",
        Tags = new[] { "SubscriptionEndpoints" })
    ]
    public override async Task<ActionResult<GetUserSubscriptionsResponse>> HandleAsync(CancellationToken cancellationToken = default)
    {
        var response = new GetUserSubscriptionsResponse();

        var appUser = await _userManager.GetUserAsync(User);
        if (appUser == null)
        {
            return Unauthorized();
        }

        try
        {
            var subscriptions = await _maxioService.GetSubscriptionsAsync(appUser.Id);

            foreach (var (subscriptionId, productHandle, state, nextBillingAt) in subscriptions)
            {
                var localSub = (await _subscriptionRepository.ListAsync())
                    .FirstOrDefault(s => s.UserId == appUser.Id && s.MaxioSubscriptionId == subscriptionId);

                response.Subscriptions.Add(new UserSubscriptionDto
                {
                    SubscriptionId = subscriptionId,
                    ProductHandle = productHandle,
                    State = state,
                    NextBillingAt = nextBillingAt,
                    PriceInDollars = localSub?.PriceInDollars ?? 0m,
                    PlanName = localSub?.PlanName ?? productHandle
                });
            }

            return Ok(response);
        }
        catch (Exception ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }
}

public class UserSubscriptionDto
{
    public long SubscriptionId { get; set; }
    public string ProductHandle { get; set; } = null!;
    public string State { get; set; } = null!;
    public DateTime? NextBillingAt { get; set; }
    public decimal PriceInDollars { get; set; }
    public string PlanName { get; set; } = null!;
}

public class GetUserSubscriptionsResponse : BaseResponse
{
    public List<UserSubscriptionDto> Subscriptions { get; } = new();
}
