using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.ApiEndpoints;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Models.Subscription;
using Microsoft.eShopWeb.Infrastructure.Identity;
using Microsoft.eShopWeb.Infrastructure.Maxio;
using Swashbuckle.AspNetCore.Annotations;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Lists the live subscriptions the authenticated user holds with the billing system.
/// </summary>
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
public class ListMySubscriptionsEndpoint : EndpointBaseAsync
    .WithoutRequest
    .WithActionResult<ListMySubscriptionsResponse>
{
    private readonly ISubscriptionBillingService _billingService;
    private readonly UserManager<ApplicationUser> _userManager;

    public ListMySubscriptionsEndpoint(ISubscriptionBillingService billingService, UserManager<ApplicationUser> userManager)
    {
        _billingService = billingService;
        _userManager = userManager;
    }

    [HttpGet("api/my-subscriptions")]
    [SwaggerOperation(
        Summary = "List My Subscriptions",
        Description = "Lists the subscriptions the authenticated user holds with the billing system",
        OperationId = "subscriptions.listMine",
        Tags = new[] { "SubscriptionEndpoints" })
    ]
    public override async Task<ActionResult<ListMySubscriptionsResponse>> HandleAsync(CancellationToken cancellationToken = default)
    {
        var username = HttpContext.User.Identity?.Name;
        if (string.IsNullOrWhiteSpace(username))
        {
            return Unauthorized();
        }

        var user = await _userManager.FindByNameAsync(username);
        if (user is null)
        {
            return Unauthorized();
        }

        IReadOnlyList<SubscriptionSummary> subscriptions;
        try
        {
            subscriptions = await _billingService.ListUserSubscriptionsAsync(
                user.UserName!,
                user.Email ?? user.UserName!,
                cancellationToken);
        }
        catch (MaxioApiException ex) when (ex.StatusCode >= 400 && ex.StatusCode < 500)
        {
            return StatusCode((int)HttpStatusCode.UnprocessableEntity,
                new { errors = ex.Errors });
        }

        var response = new ListMySubscriptionsResponse
        {
            Subscriptions = subscriptions.Select(s => new SubscriptionDto
            {
                Id = s.Id,
                State = s.State,
                PlanHandle = s.PlanHandle,
                PlanName = s.PlanName,
                PriceInCents = s.PriceInCents,
                Price = ListSubscriptionPlansEndpoint.FormatPrice(s.PriceInCents),
                NextBillingDate = s.NextBillingDate,
                ActivatedAt = s.ActivatedAt
            }).ToList()
        };

        return Ok(response);
    }
}
