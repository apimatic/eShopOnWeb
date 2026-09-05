using System;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Maxio;
using Microsoft.eShopWeb.Infrastructure.Identity;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Subscribes the authenticated user to a Maxio plan. Ensures a Maxio customer exists for the
/// user (idempotent by user id), then enrolls them - or, if they're already actively subscribed
/// to that plan, returns the existing subscription instead of creating a duplicate. This makes a
/// double-click on "Subscribe" safe.
/// </summary>
public class CreateSubscriptionEndpoint : IEndpoint<IResult, CreateSubscriptionRequest, ClaimsPrincipal>
{
    private readonly IMaxioClient _maxioClient;
    private readonly UserManager<ApplicationUser> _userManager;

    public CreateSubscriptionEndpoint(IMaxioClient maxioClient, UserManager<ApplicationUser> userManager)
    {
        _maxioClient = maxioClient;
        _userManager = userManager;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreateSubscriptionRequest request, ClaimsPrincipal user) =>
            {
                return await HandleAsync(request, user);
            })
            .Produces<CreateSubscriptionResponse>()
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status404NotFound)
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(CreateSubscriptionRequest request, ClaimsPrincipal principal)
    {
        var response = new CreateSubscriptionResponse(request.CorrelationId());

        if (string.IsNullOrWhiteSpace(request.PlanHandle))
        {
            return Results.BadRequest("PlanHandle is required.");
        }

        var appUser = await _userManager.FindByNameAsync(principal.Identity!.Name!);
        if (appUser is null)
        {
            return Results.Unauthorized();
        }

        var plans = await _maxioClient.ListPlansAsync();
        var plan = plans.FirstOrDefault(p => string.Equals(p.Handle, request.PlanHandle, StringComparison.OrdinalIgnoreCase));
        if (plan is null)
        {
            return Results.NotFound($"No subscription plan found with handle '{request.PlanHandle}'.");
        }

        var email = appUser.Email ?? appUser.UserName!;
        var customer = await _maxioClient.FindOrCreateCustomerAsync(
            reference: appUser.Id,
            email: email,
            firstName: email.Split('@')[0],
            lastName: "eShopOnWeb Customer");

        var existingSubscriptions = await _maxioClient.ListCustomerSubscriptionsAsync(customer.Id);
        var reusable = existingSubscriptions.FirstOrDefault(s =>
            string.Equals(s.ProductHandle, plan.Handle, StringComparison.OrdinalIgnoreCase) && SubscriptionMapper.IsLive(s.State));

        var subscription = reusable ?? await _maxioClient.CreateSubscriptionAsync(customer.Id, plan.Handle);

        response.Subscription = SubscriptionMapper.ToDto(subscription);
        return Results.Ok(response);
    }
}
