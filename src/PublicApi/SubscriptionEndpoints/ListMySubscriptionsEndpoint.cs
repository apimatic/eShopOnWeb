using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.ApiEndpoints;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.eShopWeb.ApplicationCore.Subscription;
using Microsoft.eShopWeb.Infrastructure.Identity;
using Swashbuckle.AspNetCore.Annotations;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Lists the subscriptions held by the authenticated caller in the billing system.
/// </summary>
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
public class ListMySubscriptionsEndpoint : EndpointBaseAsync
    .WithoutRequest
    .WithActionResult<ListMySubscriptionsResponse>
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly ISubscriptionBillingService _subscriptionBillingService;

    public ListMySubscriptionsEndpoint(
        UserManager<ApplicationUser> userManager,
        ISubscriptionBillingService subscriptionBillingService)
    {
        _userManager = userManager;
        _subscriptionBillingService = subscriptionBillingService;
    }

    [HttpGet("api/my-subscriptions")]
    [SwaggerOperation(
        Summary = "Lists the caller's subscriptions",
        Description = "Lists the recurring subscriptions the authenticated caller holds in the billing system",
        OperationId = "subscriptions.mySubscriptions",
        Tags = new[] { "SubscriptionEndpoints" })
    ]
    public override async Task<ActionResult<ListMySubscriptionsResponse>> HandleAsync(CancellationToken cancellationToken = default)
    {
        var response = new ListMySubscriptionsResponse(Guid.NewGuid());

        var user = await SubscriptionUserResolver.ResolveUserAsync(User, _userManager);
        if (user is null)
        {
            return Unauthorized();
        }

        var subscriber = new SubscriberInfo(user.Id, user.Email ?? user.UserName ?? string.Empty);
        var subscriptions = await _subscriptionBillingService.ListForUserAsync(subscriber, cancellationToken);
        response.Subscriptions.AddRange(subscriptions.Select(s => new SubscriptionDto
        {
            Id = s.Id,
            Reference = s.Reference,
            State = s.State,
            PlanHandle = s.PlanHandle,
            PlanName = s.PlanName,
            Price = s.Price,
            Currency = s.Currency,
            NextBillingDate = s.NextBillingDate,
            ActivatedAt = s.ActivatedAt,
            CreatedAt = s.CreatedAt
        }));

        return Ok(response);
    }
}
