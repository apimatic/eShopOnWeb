using System;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Identity;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Subscribes the authenticated eShopOnWeb user to a Maxio plan - the hero flow. Ensures a Maxio
/// customer exists for the user (idempotent - a double-click never creates two customers) and
/// enrolls them, confirming plan/price/state/next-billing-date back to the caller.
/// </summary>
public class SubscribeEndpoint : IEndpoint<IResult, SubscribeRequest, ClaimsPrincipal>
{
    private readonly IMaxioSubscriptionService _maxioService;
    private readonly UserManager<ApplicationUser> _userManager;

    public SubscribeEndpoint(IMaxioSubscriptionService maxioService, UserManager<ApplicationUser> userManager)
    {
        _maxioService = maxioService;
        _userManager = userManager;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (SubscribeRequest request, ClaimsPrincipal user) =>
            {
                return await HandleAsync(request, user);
            })
            .Produces<SubscribeResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(SubscribeRequest request, ClaimsPrincipal user)
    {
        var response = new SubscribeResponse(request.CorrelationId());

        var username = user.Identity?.Name;
        if (string.IsNullOrWhiteSpace(username))
        {
            return Results.Unauthorized();
        }

        if (string.IsNullOrWhiteSpace(request.PlanHandle))
        {
            return Results.BadRequest("planHandle is required.");
        }

        var applicationUser = await _userManager.FindByNameAsync(username);
        var email = applicationUser?.Email ?? username;
        var (firstName, lastName) = DeriveName(email);

        // Namespaced so this reference can't collide with any other reference convention already in
        // use on a shared Maxio site.
        var customerReference = $"eshoponweb:{username}";

        var subscription = await _maxioService.SubscribeAsync(customerReference, email, firstName, lastName, request.PlanHandle);

        response.Subscription = new CustomerSubscriptionDto
        {
            Id = subscription.Id,
            PlanHandle = subscription.ProductHandle ?? request.PlanHandle,
            PlanName = subscription.ProductName ?? string.Empty,
            Price = (subscription.PriceInCents ?? 0) / 100m,
            Currency = subscription.Currency ?? string.Empty,
            State = subscription.State ?? string.Empty,
            NextBillingDate = subscription.NextAssessmentAt
        };

        return Results.Ok(response);
    }

    private static (string FirstName, string LastName) DeriveName(string email)
    {
        var localPart = email.Split('@')[0];
        var parts = localPart.Split(new[] { '.', '_', '-' }, StringSplitOptions.RemoveEmptyEntries);

        return parts.Length switch
        {
            0 => ("Customer", "Customer"),
            1 => (Capitalize(parts[0]), "Customer"),
            _ => (Capitalize(parts[0]), Capitalize(parts[1]))
        };
    }

    private static string Capitalize(string value) =>
        value.Length == 0 ? value : char.ToUpperInvariant(value[0]) + value[1..];
}
