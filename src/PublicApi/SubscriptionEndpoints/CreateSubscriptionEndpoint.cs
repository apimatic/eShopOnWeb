using System;
using System.Security.Claims;
using System.Threading.Tasks;
using AutoMapper;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using Microsoft.eShopWeb.Infrastructure.Identity;
using Microsoft.Extensions.DependencyInjection;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Subscribes the signed-in user to a plan. Idempotent: repeating the call returns the existing subscription.
/// </summary>
public class CreateSubscriptionEndpoint : IEndpoint<IResult, CreateSubscriptionRequest, HttpContext, ISubscriptionBillingService>
{
    private readonly IMapper _mapper;

    public CreateSubscriptionEndpoint(IMapper mapper)
    {
        _mapper = mapper;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreateSubscriptionRequest request, HttpContext httpContext, ISubscriptionBillingService billingService) =>
            {
                return await HandleAsync(request, httpContext, billingService);
            })
            .Produces<CreateSubscriptionResponse>(StatusCodes.Status201Created)
            .Produces<CreateSubscriptionResponse>(StatusCodes.Status200OK)
            .Produces<CreateSubscriptionResponse>(StatusCodes.Status202Accepted)
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(CreateSubscriptionRequest request, HttpContext httpContext, ISubscriptionBillingService billingService)
    {
        var response = new CreateSubscriptionResponse(request.CorrelationId());

        if (string.IsNullOrWhiteSpace(request.PlanHandle))
        {
            return Results.BadRequest(new { StatusCode = StatusCodes.Status400BadRequest, Message = "PlanHandle is required." });
        }

        // The caller's identity comes from the JWT only, never from the request body.
        var userName = httpContext.User.FindFirstValue(ClaimTypes.Name);
        if (string.IsNullOrWhiteSpace(userName))
        {
            return Results.Unauthorized();
        }

        var userManager = httpContext.RequestServices.GetRequiredService<UserManager<ApplicationUser>>();
        var user = await userManager.FindByNameAsync(userName);
        if (user is null)
        {
            return Results.Unauthorized();
        }
        var email = string.IsNullOrWhiteSpace(user.Email) ? userName : user.Email;

        var result = await billingService.SubscribeAsync(new Subscriber(userName, email), request.PlanHandle, httpContext.RequestAborted);

        response.PlanHandle = result.PlanHandle;
        response.Subscription = result.Subscription is null ? null : _mapper.Map<SubscriptionDto>(result.Subscription);

        switch (result.Outcome)
        {
            case SubscribeOutcome.Created:
                response.Status = "created";
                return Results.Created("api/my-subscriptions", response);
            case SubscribeOutcome.AlreadySubscribed:
                response.Status = "already_subscribed";
                response.Message = "You are already subscribed to this plan.";
                return Results.Ok(response);
            case SubscribeOutcome.Pending:
                response.Status = "pending";
                response.Message = "Your subscription is being confirmed with the billing provider. Check api/my-subscriptions shortly.";
                return Results.Accepted("api/my-subscriptions", response);
            default:
                throw new InvalidOperationException($"Unhandled subscribe outcome {result.Outcome}.");
        }
    }
}
