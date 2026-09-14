using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.ApiEndpoints;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.eShopWeb.Infrastructure.Identity;
using Microsoft.eShopWeb.PublicApi.Maxio;
using Swashbuckle.AspNetCore.Annotations;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Subscribes the authenticated shopper to a plan. Idempotent: if the shopper is
/// already subscribed to the requested plan, the existing subscription is returned.
/// </summary>
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
public class CreateSubscriptionEndpoint : EndpointBaseAsync
    .WithRequest<CreateSubscriptionRequest>
    .WithActionResult<CreateSubscriptionResponse>
{
    private readonly IMaxioBillingService _maxioBillingService;
    private readonly UserManager<ApplicationUser> _userManager;

    public CreateSubscriptionEndpoint(IMaxioBillingService maxioBillingService, UserManager<ApplicationUser> userManager)
    {
        _maxioBillingService = maxioBillingService;
        _userManager = userManager;
    }

    [HttpPost("api/subscriptions")]
    [SwaggerOperation(
        Summary = "Subscribe the current shopper to a plan",
        Description = "Ensures a Maxio customer exists for the authenticated shopper and subscribes them to the given plan. Repeated calls are idempotent.",
        OperationId = "subscriptions.create",
        Tags = new[] { "SubscriptionEndpoints" })
    ]
    public override async Task<ActionResult<CreateSubscriptionResponse>> HandleAsync(
        CreateSubscriptionRequest request,
        CancellationToken cancellationToken = default)
    {
        var response = new CreateSubscriptionResponse(request.CorrelationId());

        if (string.IsNullOrWhiteSpace(request.PlanHandle))
        {
            ModelState.AddModelError(nameof(request.PlanHandle), "A plan handle is required.");
            return ValidationProblem(ModelState);
        }

        var shopper = await ResolveCurrentUserAsync(cancellationToken);
        var customerReference = shopper.UserName ?? shopper.Email;
        if (string.IsNullOrWhiteSpace(customerReference))
        {
            throw new MaxioConfigurationException("The authenticated shopper has no usable identifier to store in Maxio.");
        }

        var profile = BuildCustomerProfile(shopper);
        var subscription = await _maxioBillingService.SubscribeAsync(customerReference, profile, request.PlanHandle!, cancellationToken);

        response.Subscription = SubscriptionMapping.ToSubscriptionDto(subscription);
        return Ok(response);
    }

    private async Task<ApplicationUser> ResolveCurrentUserAsync(CancellationToken cancellationToken)
    {
        var userName = User.Identity?.Name;
        if (string.IsNullOrWhiteSpace(userName))
        {
            throw new UnauthorizedAccessException("A valid bearer token identifying the shopper is required.");
        }

        var shopper = await _userManager.FindByNameAsync(userName);
        if (shopper == null)
        {
            throw new UnauthorizedAccessException($"No shopper account exists for '{userName}'.");
        }

        return shopper;
    }

    private static MaxioCustomerProfile BuildCustomerProfile(ApplicationUser shopper)
    {
        var email = shopper.Email ?? shopper.UserName;
        var (firstName, lastName) = DeriveDisplayName(email);

        return new MaxioCustomerProfile
        {
            FirstName = firstName,
            LastName = lastName,
            Email = email,
            Organization = "eShopOnWeb",
        };
    }

    /// <summary>
    /// eShopOnWeb registers shoppers with an email only, while Maxio requires a
    /// first and last name. Derive a stable display name from the email's local part.
    /// </summary>
    private static (string FirstName, string LastName) DeriveDisplayName(string? email)
    {
        var localPart = (email ?? string.Empty).Split('@')[0].Trim();
        if (string.IsNullOrWhiteSpace(localPart))
        {
            return ("eShopOnWeb", "Shopper");
        }

        var tokens = localPart
            .Split(['.', '_', '-', '+'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(token => token.Any(char.IsLetter))
            .Select(TitleCase)
            .ToArray();

        if (tokens.Length == 0)
        {
            return ("eShopOnWeb", "Shopper");
        }

        var firstName = tokens[0];
        var lastName = tokens.Length > 1
            ? string.Join(" ", tokens.Skip(1))
            : "Shopper";

        return (firstName, lastName);
    }

    private static string TitleCase(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return value;
        }

        return char.ToUpperInvariant(value[0]) + value[1..];
    }
}
