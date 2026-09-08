using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.ApiEndpoints;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Identity;
using Swashbuckle.AspNetCore.Annotations;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Lists the subscriptions belonging to the authenticated shopper.
/// </summary>
public sealed class MySubscriptionsEndpoint : EndpointBaseAsync
    .WithoutRequest
    .WithActionResult<ListSubscriptionsResponse>
{
    private readonly ISubscriptionBillingService _billingService;
    private readonly UserManager<ApplicationUser> _userManager;

    public MySubscriptionsEndpoint(ISubscriptionBillingService billingService, UserManager<ApplicationUser> userManager)
    {
        _billingService = billingService;
        _userManager = userManager;
    }

    [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
    [HttpGet("api/my-subscriptions")]
    [SwaggerOperation(
        Summary = "Lists the current shopper's subscriptions",
        Description = "Lists the subscriptions belonging to the authenticated shopper in the billing system of record.",
        OperationId = "subscriptions.listMine",
        Tags = new[] { "SubscriptionEndpoints" })
    ]
    public override async Task<ActionResult<ListSubscriptionsResponse>> HandleAsync(CancellationToken cancellationToken = default)
    {
        var userName = User.Identity?.Name;
        if (string.IsNullOrWhiteSpace(userName))
        {
            return ErrorResult.Create(StatusCodes.Status401Unauthorized, "A valid bearer token is required.");
        }

        var user = await _userManager.FindByNameAsync(userName);
        if (user is null)
        {
            return ErrorResult.Create(StatusCodes.Status404NotFound, $"The authenticated user '{userName}' could not be found.");
        }

        var subscriptions = await _billingService.ListSubscriptionsAsync(user.Id, cancellationToken);
        var response = new ListSubscriptionsResponse
        {
            Subscriptions = subscriptions.Select(SubscriptionDto.From).ToList()
        };

        return response;
    }
}
