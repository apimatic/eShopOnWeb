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
using Swashbuckle.AspNetCore.Annotations;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Lists the authenticated user's subscriptions with live billing-system state.
/// </summary>
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
public class MySubscriptionsEndpoint : EndpointBaseAsync
    .WithoutRequest
    .WithActionResult<MySubscriptionsResponse>
{
    private readonly IMaxioSubscriptionService _subscriptionService;
    private readonly UserManager<ApplicationUser> _userManager;

    public MySubscriptionsEndpoint(IMaxioSubscriptionService subscriptionService, UserManager<ApplicationUser> userManager)
    {
        _subscriptionService = subscriptionService;
        _userManager = userManager;
    }

    [HttpGet("api/my-subscriptions")]
    [SwaggerOperation(
        Summary = "Lists the authenticated user's subscriptions",
        Description = "Lists the user's subscriptions with plan, price, state and next billing date from Maxio",
        OperationId = "subscriptions.mySubscriptions",
        Tags = new[] { "SubscriptionEndpoints" })
    ]
    public override async Task<ActionResult<MySubscriptionsResponse>> HandleAsync(CancellationToken cancellationToken = default)
    {
        var userName = User.FindFirstValue(ClaimTypes.Name);
        if (string.IsNullOrEmpty(userName))
        {
            return Unauthorized();
        }

        var user = await _userManager.FindByNameAsync(userName);
        if (user is null)
        {
            return Unauthorized();
        }

        var response = new MySubscriptionsResponse(Guid.NewGuid());

        var subscriptions = await _subscriptionService.GetSubscriptionsForUserAsync(user.Id, userName, cancellationToken);
        response.Subscriptions.AddRange(subscriptions.Select(s => new SubscriptionDto
        {
            SubscriptionId = s.SubscriptionId,
            ProductHandle = s.ProductHandle,
            ProductName = s.ProductName,
            Price = s.Price,
            State = s.State,
            NextBillingDate = s.NextBillingDate,
            ActivatedAt = s.ActivatedAt,
            MaxioCustomerId = s.MaxioCustomerId,
            MaxioReference = s.MaxioReference
        }));

        return Ok(response);
    }
}
