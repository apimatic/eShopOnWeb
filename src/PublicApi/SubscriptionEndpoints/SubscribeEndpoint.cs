using System;
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
/// Subscribes the authenticated user to a plan. Idempotent: repeating the same
/// subscription returns the existing one instead of creating a duplicate.
/// </summary>
public class SubscribeEndpoint : EndpointBaseAsync
    .WithRequest<SubscribeRequest>
    .WithActionResult<SubscribeResponse>
{
    private readonly ISubscriptionBillingService _billingService;
    private readonly UserManager<ApplicationUser> _userManager;

    public SubscribeEndpoint(ISubscriptionBillingService billingService,
        UserManager<ApplicationUser> userManager)
    {
        _billingService = billingService;
        _userManager = userManager;
    }

    [HttpPost("api/subscriptions")]
    [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
    [SwaggerOperation(
        Summary = "Subscribes the authenticated user to a plan",
        Description = "Ensures a billing customer exists for the user and enrolls them in the given plan. Idempotent per user and plan.",
        OperationId = "subscriptions.subscribe",
        Tags = new[] { "SubscriptionEndpoints" })
    ]
    public override async Task<ActionResult<SubscribeResponse>> HandleAsync(SubscribeRequest request,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.ProductHandle))
        {
            return BadRequest("productHandle is required");
        }

        var user = await GetCurrentUserAsync();
        if (user is null)
        {
            return Unauthorized();
        }

        var email = user.Email ?? user.UserName!;
        var (firstName, lastName) = DeriveName(email);

        var subscription = await _billingService.SubscribeAsync(
            user.Id, email, firstName, lastName, request.ProductHandle, cancellationToken);

        var response = new SubscribeResponse(request.CorrelationId())
        {
            Subscription = subscription
        };
        return response;
    }

    private async Task<ApplicationUser?> GetCurrentUserAsync()
    {
        var username = User.Identity?.Name;
        return string.IsNullOrEmpty(username) ? null : await _userManager.FindByNameAsync(username);
    }

    // eShopOnWeb identity carries no name fields; derive plausible ones from the email local part.
    internal static (string FirstName, string LastName) DeriveName(string email)
    {
        var local = email.Split('@')[0];
        var parts = local.Split('.', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length switch
        {
            0 => ("Shopper", "Customer"),
            1 => (Capitalize(parts[0]), "Customer"),
            _ => (Capitalize(parts[0]), Capitalize(parts[^1]))
        };
    }

    private static string Capitalize(string value) =>
        string.Concat(value[..1].ToUpperInvariant(), value.AsSpan(1));
}
