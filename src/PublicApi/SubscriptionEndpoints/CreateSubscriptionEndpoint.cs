using System;
using System.Linq;
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
/// Subscribes the signed-in shopper to a plan. Ensures a Maxio customer exists for the user
/// (idempotent) and never creates a duplicate subscription.
/// </summary>
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
public class CreateSubscriptionEndpoint : EndpointBaseAsync
    .WithRequest<CreateSubscriptionRequest>
    .WithActionResult<CreateSubscriptionResponse>
{
    private readonly ISubscriptionBillingService _subscriptionBillingService;
    private readonly UserManager<ApplicationUser> _userManager;

    public CreateSubscriptionEndpoint(ISubscriptionBillingService subscriptionBillingService, UserManager<ApplicationUser> userManager)
    {
        _subscriptionBillingService = subscriptionBillingService;
        _userManager = userManager;
    }

    [HttpPost("api/subscriptions")]
    [SwaggerOperation(
        Summary = "Subscribes the current user to a plan",
        Description = "Ensures a Maxio customer exists for the signed-in user and subscribes them to the requested plan",
        OperationId = "subscriptions.create",
        Tags = new[] { "SubscriptionEndpoints" })
    ]
    public override async Task<ActionResult<CreateSubscriptionResponse>> HandleAsync(CreateSubscriptionRequest request,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var user = await ResolveCurrentUserAsync();
            if (user == null)
            {
                return Unauthorized();
            }

            var signup = await _subscriptionBillingService.SubscribeAsync(ToSubscriptionCustomer(user), request.PlanHandle, cancellationToken);

            if (signup.Status == SubscriptionSignupStatus.PlanNotFound)
            {
                var details = new ErrorDetails
                {
                    StatusCode = StatusCodes.Status404NotFound,
                    Message = $"No subscription plan with handle '{request.PlanHandle}' is available."
                };
                return NotFound(details);
            }

            var response = new CreateSubscriptionResponse(request.CorrelationId())
            {
                Created = signup.Status == SubscriptionSignupStatus.Created,
                CustomerId = signup.CustomerId,
                Subscription = SubscriptionDtoMapper.ToDto(signup.Subscription!)
            };
            return signup.Status == SubscriptionSignupStatus.Created
                ? StatusCode(StatusCodes.Status201Created, response)
                : Ok(response);
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

    private async Task<ApplicationUser?> ResolveCurrentUserAsync()
    {
        string? userName = User.Identity?.Name;
        if (string.IsNullOrWhiteSpace(userName))
        {
            return null;
        }
        return await _userManager.FindByNameAsync(userName);
    }

    private static SubscriptionCustomer ToSubscriptionCustomer(ApplicationUser user)
    {
        string email = string.IsNullOrWhiteSpace(user.Email) ? user.UserName ?? string.Empty : user.Email!;
        (string firstName, string lastName) = DeriveName(email);
        return new SubscriptionCustomer
        {
            Reference = user.Id,
            Email = email,
            FirstName = firstName,
            LastName = lastName
        };
    }

    private static (string firstName, string lastName) DeriveName(string email)
    {
        string local = email;
        int at = email.IndexOf('@');
        if (at > 0)
        {
            local = email[..at];
        }

        var parts = local
            .Split(new[] { '.', '_', '-', '+' }, StringSplitOptions.RemoveEmptyEntries)
            .Where(part => part.Any(char.IsLetterOrDigit))
            .ToArray();

        if (parts.Length == 0)
        {
            return ("eShop", "Shopper");
        }

        string firstName = parts[0];
        string lastName = parts.Length > 1 ? string.Join(" ", parts.Skip(1)) : "Shopper";
        return (Capitalize(firstName), Capitalize(lastName));
    }

    private static string Capitalize(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return value;
        }
        return char.ToUpperInvariant(value[0]) + value[1..];
    }
}
