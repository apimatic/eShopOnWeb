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
using Microsoft.eShopWeb.Infrastructure.Maxio;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Subscribes the authenticated caller to a plan. Idempotent: a repeated subscribe
/// to the same plan returns the existing subscription instead of creating a second one.
/// </summary>
public class CreateSubscriptionEndpoint : IEndpoint<IResult, CreateSubscriptionRequest, UserManager<ApplicationUser>>
{
    private readonly IMaxioBillingService _billingService;

    public CreateSubscriptionEndpoint(IMaxioBillingService billingService)
    {
        _billingService = billingService;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreateSubscriptionRequest request, HttpContext httpContext, ClaimsPrincipal user, UserManager<ApplicationUser> userManager) =>
            {
                return await HandleAsync(request, user, userManager, httpContext.RequestAborted);
            })
            .Produces<CreateSubscriptionResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public Task<IResult> HandleAsync(CreateSubscriptionRequest request, UserManager<ApplicationUser> userManager) =>
        HandleAsync(request, user: null, userManager, CancellationToken.None);

    public async Task<IResult> HandleAsync(CreateSubscriptionRequest request, ClaimsPrincipal? user, UserManager<ApplicationUser> userManager, CancellationToken cancellationToken)
    {
        var response = new CreateSubscriptionResponse(request.CorrelationId());

        var appUser = await SubscriptionUsers.ResolveCurrentUserAsync(user, userManager);
        if (appUser is null)
        {
            return Results.Unauthorized();
        }
        if (string.IsNullOrWhiteSpace(request.PlanHandle))
        {
            return Results.Json(
                new BillingErrorResponse(request.CorrelationId())
                {
                    Message = "A plan handle is required."
                },
                statusCode: 400);
        }

        try
        {
            var subscriber = SubscriptionUsers.BuildSubscriber(appUser);
            var subscription = await _billingService.SubscribeAsync(subscriber, request.PlanHandle, cancellationToken);

            response.Subscription = new SubscriptionDto
            {
                SubscriptionId = subscription.SubscriptionId,
                PlanHandle = subscription.PlanHandle,
                PlanName = subscription.PlanName,
                Price = subscription.Price,
                State = subscription.State,
                NextBillingDate = subscription.NextBillingDate,
                CustomerReference = subscription.CustomerReference,
                AlreadySubscribed = subscription.AlreadySubscribed
            };
            return Results.Ok(response);
        }
        catch (MaxioBillingException ex)
        {
            return BillingErrorResults.From(ex, request.CorrelationId());
        }
    }
}