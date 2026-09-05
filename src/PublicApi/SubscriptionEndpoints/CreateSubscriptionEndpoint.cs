using System;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Maxio;
using Microsoft.eShopWeb.Infrastructure.Identity;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Subscribes the authenticated eShopOnWeb user to a Maxio subscription plan, creating the Maxio
/// customer on first use. Idempotent: retrying (e.g. a double-click) never creates a second Maxio
/// customer or a second live subscription for the same user + plan.
/// </summary>
public class CreateSubscriptionEndpoint : IEndpoint<IResult, SubscribeRequest, IMaxioSubscriptionService>
{
    private readonly UserManager<ApplicationUser> _userManager;

    public CreateSubscriptionEndpoint(UserManager<ApplicationUser> userManager)
    {
        _userManager = userManager;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (SubscribeRequest request, ClaimsPrincipal user, IMaxioSubscriptionService subscriptionService, CancellationToken ct) =>
            {
                request.Username = user.Identity?.Name;
                return await HandleAsync(request, subscriptionService, ct);
            })
            .Produces<SubscribeResponse>()
            .Produces(StatusCodes.Status400BadRequest)
            .WithTags("SubscriptionEndpoints");
    }

    public Task<IResult> HandleAsync(SubscribeRequest request, IMaxioSubscriptionService subscriptionService)
        => HandleAsync(request, subscriptionService, CancellationToken.None);

    private async Task<IResult> HandleAsync(SubscribeRequest request, IMaxioSubscriptionService subscriptionService, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Username))
        {
            return Results.Unauthorized();
        }

        if (string.IsNullOrWhiteSpace(request.PlanHandle))
        {
            return Results.BadRequest(new BlazorShared.Models.ErrorDetails
            {
                StatusCode = StatusCodes.Status400BadRequest,
                Message = "PlanHandle is required."
            });
        }

        var applicationUser = await _userManager.FindByNameAsync(request.Username);
        if (applicationUser == null)
        {
            return Results.Unauthorized();
        }

        var email = string.IsNullOrWhiteSpace(applicationUser.Email) ? request.Username : applicationUser.Email;
        var (firstName, lastName) = SplitDisplayName(email);

        var profile = new MaxioCustomerProfile(
            Reference: request.Username,
            Email: email,
            FirstName: firstName,
            LastName: lastName);

        var subscription = await subscriptionService.SubscribeAsync(profile, request.PlanHandle, ct);

        var response = new SubscribeResponse(request.CorrelationId())
        {
            Subscription = new SubscriptionDto
            {
                PlanHandle = subscription.PlanHandle,
                PlanName = subscription.PlanName,
                PriceInCents = subscription.PriceInCents,
                Price = subscription.PriceInCents.HasValue ? subscription.PriceInCents.Value / 100m : null,
                State = subscription.State,
                NextBillingDate = subscription.NextBillingDate
            }
        };

        return Results.Ok(response);
    }

    // ApplicationUser carries no first/last name (plain ASP.NET Core Identity user, keyed by email/username),
    // so a display name is derived from the email's local part for the Maxio customer record.
    private static (string FirstName, string LastName) SplitDisplayName(string emailOrUsername)
    {
        var localPart = emailOrUsername.Split('@')[0];
        var segments = localPart.Split(new[] { '.', '_', '-' }, StringSplitOptions.RemoveEmptyEntries);

        if (segments.Length >= 2)
        {
            return (Capitalize(segments[0]), Capitalize(segments[^1]));
        }

        return (Capitalize(localPart), "Subscriber");
    }

    private static string Capitalize(string value) =>
        value.Length == 0 ? value : char.ToUpperInvariant(value[0]) + value[1..];
}
