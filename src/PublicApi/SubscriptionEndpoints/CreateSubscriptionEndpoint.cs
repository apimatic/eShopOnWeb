using System;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.ApiEndpoints;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.eShopWeb.Infrastructure.Identity;
using Microsoft.eShopWeb.PublicApi.Services;
using Swashbuckle.AspNetCore.Annotations;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Subscribes the authenticated caller to a plan. Idempotent: the caller's Maxio customer
/// and the subscription are found-or-created on a deterministic reference, so a repeated
/// call returns the existing subscription instead of creating a duplicate.
/// </summary>
public class CreateSubscriptionEndpoint : EndpointBaseAsync
    .WithRequest<CreateSubscriptionRequest>
    .WithActionResult<CreateSubscriptionResponse>
{
    private readonly IMaxioSubscriptionService _subscriptionService;
    private readonly UserManager<ApplicationUser> _userManager;

    public CreateSubscriptionEndpoint(
        IMaxioSubscriptionService subscriptionService,
        UserManager<ApplicationUser> userManager)
    {
        _subscriptionService = subscriptionService;
        _userManager = userManager;
    }

    [HttpPost("api/subscriptions")]
    [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
    [SwaggerOperation(
        Summary = "Subscribes the caller to a plan",
        Description = "Ensures a Maxio customer exists for the caller and enrolls them in the given plan",
        OperationId = "subscriptions.create",
        Tags = new[] { "SubscriptionEndpoints" })
    ]
    public override async Task<ActionResult<CreateSubscriptionResponse>> HandleAsync(CreateSubscriptionRequest request,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.ProductHandle))
        {
            return BadRequest("ProductHandle is required.");
        }

        var username = User.Identity?.Name;
        if (string.IsNullOrEmpty(username))
        {
            return Unauthorized();
        }

        var user = await _userManager.FindByNameAsync(username);
        if (user is null)
        {
            return Unauthorized();
        }

        var email = string.IsNullOrWhiteSpace(user.Email) ? username : user.Email;
        var (firstName, lastName) = DeriveName(email);

        var command = new SubscribeCommand
        {
            CustomerReference = username,
            Email = email,
            FirstName = firstName,
            LastName = lastName,
            ProductHandle = request.ProductHandle,
        };

        var subscription = await _subscriptionService.SubscribeAsync(command, cancellationToken);

        var response = new CreateSubscriptionResponse(request.CorrelationId())
        {
            Subscription = subscription,
        };
        return Ok(response);
    }

    private static (string FirstName, string LastName) DeriveName(string email)
    {
        var localPart = email.Split('@')[0];
        var parts = localPart.Split('.', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length switch
        {
            0 => ("eShop", "Shopper"),
            1 => (parts[0], parts[0]),
            _ => (parts[0], parts[^1]),
        };
    }
}
