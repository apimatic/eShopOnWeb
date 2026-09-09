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
using Microsoft.eShopWeb.Infrastructure.Identity;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Subscribes the authenticated user to a subscription plan.
/// Idempotent: a Maxio customer is ensured for the user (keyed on the eShopOnWeb user id)
/// and an in-flight/duplicate subscribe resolves to the same subscription.
/// </summary>
public class SubscribeEndpoint : IEndpoint<IResult, CreateSubscriptionRequest, ISubscriptionService, ClaimsPrincipal>
{
    private readonly UserManager<ApplicationUser> _userManager;

    public SubscribeEndpoint(UserManager<ApplicationUser> userManager)
    {
        _userManager = userManager;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreateSubscriptionRequest request, ISubscriptionService subscriptionService,
             ClaimsPrincipal principal, CancellationToken cancellationToken) =>
            {
                return await HandleAsync(request, subscriptionService, principal);
            })
            .Produces<CreateSubscriptionResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(CreateSubscriptionRequest request, ISubscriptionService subscriptionService, ClaimsPrincipal principal)
    {
        var response = new CreateSubscriptionResponse(request.CorrelationId());

        if (string.IsNullOrWhiteSpace(request.ProductHandle))
        {
            return Results.BadRequest(new { message = "productHandle is required." });
        }

        // The caller's identity comes from the JWT: the token's Name claim is the username.
        var userName = principal.Identity?.Name;
        if (string.IsNullOrWhiteSpace(userName))
        {
            return Results.Unauthorized();
        }

        var user = await _userManager.FindByNameAsync(userName);
        if (user is null)
        {
            return Results.Unauthorized();
        }

        var enrollment = await subscriptionService.SubscribeAsync(user.Id, user.Email!, request.ProductHandle, CancellationToken.None);

        response.Subscription = ToDto(enrollment.Subscription);
        response.AlreadySubscribed = enrollment.AlreadySubscribed;

        return enrollment.AlreadySubscribed
            ? Results.Ok(response)
            : Results.Created($"api/my-subscriptions", response);
    }

    internal static SubscriptionDto ToDto(ApplicationCore.Models.SubscriptionSummary subscription) => new()
    {
        Id = subscription.Id,
        Reference = subscription.Reference,
        State = subscription.State,
        ProductHandle = subscription.ProductHandle,
        ProductName = subscription.ProductName,
        Price = subscription.Price,
        PriceCents = subscription.PriceCents,
        BillingInterval = subscription.BillingInterval,
        BillingIntervalUnit = subscription.BillingIntervalUnit,
        ActivatedAt = subscription.ActivatedAt,
        CurrentPeriodStartedAt = subscription.CurrentPeriodStartedAt,
        NextBillingDate = subscription.NextBillingDate,
        CanceledAt = subscription.CanceledAt,
        CustomerId = subscription.CustomerId
    };
}
