using System;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.ApiEndpoints;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Identity;
using Microsoft.eShopWeb.Infrastructure.Maxio;
using Swashbuckle.AspNetCore.Annotations;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Lists the authenticated user's subscriptions as held in Maxio.
/// </summary>
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
public class MySubscriptionListEndpoint : EndpointBaseAsync
    .WithoutRequest
    .WithActionResult<MySubscriptionListResponse>
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly ISubscriptionBillingService _billingService;

    public MySubscriptionListEndpoint(UserManager<ApplicationUser> userManager,
        ISubscriptionBillingService billingService)
    {
        _userManager = userManager;
        _billingService = billingService;
    }

    [HttpGet("api/my-subscriptions")]
    [SwaggerOperation(
        Summary = "Lists the authenticated user's subscriptions",
        Description = "Returns the user's subscriptions as recorded in Maxio, including plan, price, state and next billing date.",
        OperationId = "subscriptions.listMine",
        Tags = new[] { "SubscriptionEndpoints" })
    ]
    public override async Task<ActionResult<MySubscriptionListResponse>> HandleAsync(CancellationToken cancellationToken = default)
    {
        var username = User.FindFirstValue(ClaimTypes.Name) ?? User.FindFirstValue("unique_name") ?? User.Identity?.Name;
        if (string.IsNullOrWhiteSpace(username))
        {
            return Unauthorized();
        }

        var appUser = await _userManager.FindByNameAsync(username);
        if (appUser is null)
        {
            return Unauthorized();
        }

        try
        {
            var subscriptions = await _billingService.ListSubscriptionsForUserAsync(appUser.Id, cancellationToken);

            var response = new MySubscriptionListResponse(Guid.NewGuid());
            response.Subscriptions.AddRange(subscriptions.Select(s => new SubscriptionDto
            {
                Id = s.SubscriptionId,
                State = s.State,
                PlanHandle = s.ProductHandle,
                PlanName = s.ProductName,
                Price = s.Price,
                Currency = s.Currency,
                NextBillingDate = s.NextBillingDate
            }));

            return response;
        }
        catch (MaxioApiException ex)
        {
            return SubscriptionCreateEndpoint.MapMaxioError(ex);
        }
    }
}