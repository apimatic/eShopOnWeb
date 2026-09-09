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
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Models;
using Microsoft.eShopWeb.Infrastructure.Identity;
using Swashbuckle.AspNetCore.Annotations;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Lists the Maxio subscriptions of the authenticated user.
/// </summary>
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
public class ListMySubscriptionsEndpoint : EndpointBaseAsync
    .WithoutRequest
    .WithActionResult<ListMySubscriptionsResponse>
{
    private readonly IMaxioBillingService _billingService;
    private readonly UserManager<ApplicationUser> _userManager;

    public ListMySubscriptionsEndpoint(IMaxioBillingService billingService, UserManager<ApplicationUser> userManager)
    {
        _billingService = billingService;
        _userManager = userManager;
    }

    [HttpGet("api/my-subscriptions")]
    [SwaggerOperation(
        Summary = "Lists the authenticated user's subscriptions",
        Description = "Lists the Maxio subscriptions of the authenticated user",
        OperationId = "subscriptions.listMine",
        Tags = new[] { "SubscriptionEndpoints" })
    ]
    public override async Task<ActionResult<ListMySubscriptionsResponse>> HandleAsync(CancellationToken cancellationToken = default)
    {
        var user = await _userManager.FindByPrincipalAsync(User);
        if (user is null)
        {
            return Unauthorized();
        }

        IReadOnlyList<SubscriptionInfo> subscriptions;
        try
        {
            subscriptions = await _billingService.GetUserSubscriptionsAsync(user.Id, cancellationToken);
        }
        catch (MaxioBillingException ex)
        {
            return SubscriptionEndpointHelpers.BillingProblem(ex);
        }

        return new ListMySubscriptionsResponse(Guid.NewGuid())
        {
            Subscriptions = subscriptions.Select(s => new SubscriptionDto
            {
                SubscriptionId = s.SubscriptionId,
                Reference = s.Reference,
                PlanHandle = s.PlanHandle,
                PlanName = s.PlanName,
                State = s.State,
                Price = s.Price,
                NextBillingDate = s.NextBillingDate
            }).ToList()
        };
    }
}
