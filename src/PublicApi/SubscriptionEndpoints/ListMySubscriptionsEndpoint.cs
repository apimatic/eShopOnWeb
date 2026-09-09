using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.ApiEndpoints;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Identity;
using Microsoft.eShopWeb.Infrastructure.Maxio;
using Swashbuckle.AspNetCore.Annotations;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Lists the Maxio Advanced Billing subscriptions held by the authenticated user.
/// </summary>
[Authorize]
public class ListMySubscriptionsEndpoint : EndpointBaseAsync
    .WithoutRequest
    .WithActionResult<ListMySubscriptionsResponse>
{
    private readonly ISubscriptionService _subscriptionService;
    private readonly UserManager<ApplicationUser> _userManager;

    public ListMySubscriptionsEndpoint(ISubscriptionService subscriptionService, UserManager<ApplicationUser> userManager)
    {
        _subscriptionService = subscriptionService;
        _userManager = userManager;
    }

    [HttpGet("api/my-subscriptions")]
    [SwaggerOperation(
        Summary = "Lists my subscriptions",
        Description = "Lists the subscriptions the authenticated user holds in Maxio Advanced Billing.",
        OperationId = "subscriptions.listMine",
        Tags = new[] { "SubscriptionEndpoints" })
    ]
    public override async Task<ActionResult<ListMySubscriptionsResponse>> HandleAsync(CancellationToken cancellationToken = default)
    {
        var userName = User.Identity?.Name;
        if (string.IsNullOrWhiteSpace(userName))
        {
            return Unauthorized();
        }

        var user = await _userManager.FindByNameAsync(userName);
        if (user is null)
        {
            return Unauthorized();
        }

        var response = new ListMySubscriptionsResponse();

        IReadOnlyList<SubscriptionSummary> subscriptions;
        try
        {
            subscriptions = await _subscriptionService.GetSubscriptionsForUserAsync(user.Id, cancellationToken);
        }
        catch (MaxioApiException ex)
        {
            return StatusCode(StatusCodes.Status502BadGateway,
                new { message = "The billing service returned an error.", detail = ex.Message });
        }

        response.Subscriptions.AddRange(subscriptions.Select(s => new SubscriptionDto
        {
            MaxioSubscriptionId = s.MaxioSubscriptionId,
            PlanHandle = s.PlanHandle,
            PlanName = s.PlanName,
            Price = s.Price,
            Interval = s.Interval,
            IntervalUnit = s.IntervalUnit,
            State = s.State,
            NextBillingDate = s.NextBillingDate,
            CreatedAt = s.CreatedAt
        }));

        return response;
    }
}
