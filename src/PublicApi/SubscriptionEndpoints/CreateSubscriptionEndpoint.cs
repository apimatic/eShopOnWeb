using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Identity;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Subscribes the JWT-authenticated user to a plan. Idempotent: repeated
/// calls with the same plan return the existing subscription.
/// </summary>
public class CreateSubscriptionEndpoint : IEndpoint<IResult, CreateSubscriptionRequest,
    ISubscriptionBillingService, UserManager<ApplicationUser>>
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public CreateSubscriptionEndpoint(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreateSubscriptionRequest request, ISubscriptionBillingService subscriptionBillingService,
                UserManager<ApplicationUser> userManager) =>
            {
                return await HandleAsync(request, subscriptionBillingService, userManager);
            })
            .Produces<CreateSubscriptionResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(CreateSubscriptionRequest request,
        ISubscriptionBillingService subscriptionBillingService, UserManager<ApplicationUser> userManager)
    {
        var response = new CreateSubscriptionResponse(request.CorrelationId());

        if (string.IsNullOrWhiteSpace(request.PlanHandle))
        {
            return Results.BadRequest(new { errors = new[] { "PlanHandle is required." } });
        }

        var httpUser = _httpContextAccessor.HttpContext?.User;
        if (httpUser is null)
        {
            return Results.Unauthorized();
        }

        var user = await SubscriptionUserResolver.ResolveAsync(httpUser, userManager);
        if (user is null)
        {
            return Results.Unauthorized();
        }

        SubscriptionEnrollment enrollment;
        try
        {
            enrollment = await subscriptionBillingService.SubscribeAsync(
                user.Id, user.Email ?? user.UserName ?? string.Empty,
                user.UserName ?? user.Email ?? string.Empty, request.PlanHandle);
        }
        catch (UnknownSubscriptionPlanException ex)
        {
            return Results.NotFound(new { correlationId = response.CorrelationId(), errors = new[] { ex.Message } });
        }

        response.Subscription = MapSubscription(enrollment);
        return Results.Created("api/my-subscriptions", response);
    }

    internal static SubscriptionDto MapSubscription(SubscriptionEnrollment enrollment)
    {
        return new SubscriptionDto
        {
            SubscriptionId = enrollment.SubscriptionId,
            Reference = enrollment.Reference,
            PlanHandle = enrollment.PlanHandle,
            PlanName = enrollment.PlanName,
            PriceInCents = enrollment.PriceInCents,
            Price = SubscriptionPlanDto.FormatPrice(enrollment.PriceInCents),
            State = enrollment.State,
            NextBillingDate = enrollment.NextBillingDate,
            ActivatedAt = enrollment.ActivatedAt,
            AlreadySubscribed = enrollment.AlreadySubscribed
        };
    }
}