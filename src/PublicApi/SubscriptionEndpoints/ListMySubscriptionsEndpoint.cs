using System.Threading;
using System.Threading.Tasks;
using Ardalis.ApiEndpoints;
using BlazorShared.Models;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.eShopWeb.ApplicationCore.SubscriptionBilling;
using Microsoft.eShopWeb.Infrastructure.Identity;
using Swashbuckle.AspNetCore.Annotations;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Lists the subscriptions of the signed-in shopper.
/// </summary>
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
public class ListMySubscriptionsEndpoint : EndpointBaseAsync
    .WithoutRequest
    .WithActionResult<ListMySubscriptionsResponse>
{
    private readonly ISubscriptionBillingService _subscriptionBillingService;
    private readonly UserManager<ApplicationUser> _userManager;

    public ListMySubscriptionsEndpoint(ISubscriptionBillingService subscriptionBillingService, UserManager<ApplicationUser> userManager)
    {
        _subscriptionBillingService = subscriptionBillingService;
        _userManager = userManager;
    }

    [HttpGet("api/my-subscriptions")]
    [SwaggerOperation(
        Summary = "Lists the subscriptions of the current user",
        Description = "Lists the subscriptions the signed-in user holds in Maxio",
        OperationId = "subscriptions.my",
        Tags = new[] { "SubscriptionEndpoints" })
    ]
    public override async Task<ActionResult<ListMySubscriptionsResponse>> HandleAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            string? userName = User.Identity?.Name;
            if (string.IsNullOrWhiteSpace(userName))
            {
                return Unauthorized();
            }

            var user = await _userManager.FindByNameAsync(userName);
            if (user == null)
            {
                return Unauthorized();
            }

            var response = new ListMySubscriptionsResponse();
            var subscriptions = await _subscriptionBillingService.ListSubscriptionsAsync(user.Id, cancellationToken);
            foreach (var subscription in subscriptions)
            {
                response.Subscriptions.Add(SubscriptionDtoMapper.ToDto(subscription));
            }

            return response;
        }
        catch (SubscriptionBillingException billingException)
        {
            var details = new ErrorDetails
            {
                StatusCode = billingException.StatusCode,
                Message = billingException.Message
            };
            return StatusCode(billingException.StatusCode, details);
        }
    }
}
