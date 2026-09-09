using System;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.ApiEndpoints;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Identity;
using Swashbuckle.AspNetCore.Annotations;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Subscribes the authenticated user to a plan. Idempotent: subscribing twice
/// to the same plan returns the existing subscription.
/// </summary>
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
public class CreateSubscriptionEndpoint : EndpointBaseAsync
    .WithRequest<CreateSubscriptionRequest>
    .WithActionResult<CreateSubscriptionResponse>
{
    private readonly IMaxioSubscriptionService _subscriptionService;
    private readonly UserManager<ApplicationUser> _userManager;

    public CreateSubscriptionEndpoint(IMaxioSubscriptionService subscriptionService, UserManager<ApplicationUser> userManager)
    {
        _subscriptionService = subscriptionService;
        _userManager = userManager;
    }

    [HttpPost("api/subscriptions")]
    [SwaggerOperation(
        Summary = "Subscribes the authenticated user to a plan",
        Description = "Ensures a Maxio customer exists for the user and enrolls them in the requested plan. Idempotent per user and plan.",
        OperationId = "subscriptions.create",
        Tags = new[] { "SubscriptionEndpoints" })
    ]
    public override async Task<ActionResult<CreateSubscriptionResponse>> HandleAsync(CreateSubscriptionRequest request, CancellationToken cancellationToken = default)
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

        var summary = await _subscriptionService.SubscribeAsync(user.Id, userName, user.Email, request.ProductHandle, cancellationToken);

        return Created($"api/my-subscriptions", new CreateSubscriptionResponse(Guid.NewGuid())
        {
            Subscription = new SubscriptionDto
            {
                SubscriptionId = summary.SubscriptionId,
                ProductHandle = summary.ProductHandle,
                ProductName = summary.ProductName,
                Price = summary.Price,
                State = summary.State,
                NextBillingDate = summary.NextBillingDate,
                ActivatedAt = summary.ActivatedAt,
                MaxioCustomerId = summary.MaxioCustomerId,
                MaxioReference = summary.MaxioReference
            }
        });
    }
}
