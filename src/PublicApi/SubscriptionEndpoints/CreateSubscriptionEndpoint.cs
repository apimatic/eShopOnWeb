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
/// Subscribes the authenticated user to a plan. Idempotent: subscribing twice to the same
/// plan returns the existing subscription instead of creating a second one.
/// </summary>
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
public class CreateSubscriptionEndpoint : EndpointBaseAsync
    .WithRequest<CreateSubscriptionRequest>
    .WithActionResult<CreateSubscriptionResponse>
{
    private readonly IMaxioBillingService _billingService;
    private readonly UserManager<ApplicationUser> _userManager;

    public CreateSubscriptionEndpoint(IMaxioBillingService billingService, UserManager<ApplicationUser> userManager)
    {
        _billingService = billingService;
        _userManager = userManager;
    }

    [HttpPost("api/subscriptions")]
    [SwaggerOperation(
        Summary = "Subscribes the authenticated user to a plan",
        Description = "Subscribes the authenticated user to a plan identified by its handle",
        OperationId = "subscriptions.create",
        Tags = new[] { "SubscriptionEndpoints" })
    ]
    public override async Task<ActionResult<CreateSubscriptionResponse>> HandleAsync(CreateSubscriptionRequest request,
        CancellationToken cancellationToken = default)
    {
        var user = await _userManager.FindByPrincipalAsync(User);
        if (user is null)
        {
            return Unauthorized();
        }

        if (string.IsNullOrWhiteSpace(request?.PlanHandle))
        {
            return BadRequest(new { title = "A planHandle is required." });
        }

        SubscriptionInfo subscription;
        try
        {
            subscription = await _billingService.SubscribeAsync(user.Id, user.Email ?? user.UserName!, request.PlanHandle, cancellationToken);
        }
        catch (MaxioBillingException ex)
        {
            return SubscriptionEndpointHelpers.BillingProblem(ex);
        }

        return new CreateSubscriptionResponse(request.CorrelationId())
        {
            Subscription = new SubscriptionDto
            {
                SubscriptionId = subscription.SubscriptionId,
                Reference = subscription.Reference,
                PlanHandle = subscription.PlanHandle,
                PlanName = subscription.PlanName,
                State = subscription.State,
                Price = subscription.Price,
                NextBillingDate = subscription.NextBillingDate
            }
        };
    }
}
