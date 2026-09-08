using System;
using System.Security.Claims;
using System.Threading.Tasks;
using BlazorShared.Models;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.PublicApi.Services.Subscriptions;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class CreateSubscriptionEndpoint : IEndpoint<IResult, CreateSubscriptionRequest, ClaimsPrincipal, ISubscriptionService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (CreateSubscriptionRequest request, ClaimsPrincipal user, ISubscriptionService subscriptionService) =>
            {
                return await HandleAsync(request, user, subscriptionService);
            })
            .Produces<CreateSubscriptionResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(
        CreateSubscriptionRequest request,
        ClaimsPrincipal user,
        ISubscriptionService subscriptionService)
    {
        if (request is null)
        {
            return BadRequest("A request body is required.");
        }

        string? userName = user.Identity?.Name;
        if (string.IsNullOrWhiteSpace(userName))
        {
            return Results.Unauthorized();
        }

        if (string.IsNullOrWhiteSpace(request.PlanHandle))
        {
            return BadRequest("planHandle is required.");
        }

        if (string.IsNullOrWhiteSpace(request.FirstName) || string.IsNullOrWhiteSpace(request.LastName))
        {
            return BadRequest("firstName and lastName are required to create a Maxio customer.");
        }

        try
        {
            var result = await subscriptionService.SubscribeAsync(
                userName,
                request.PlanHandle,
                request.FirstName,
                request.LastName,
                request.Email,
                default);

            var response = new CreateSubscriptionResponse
            {
                AlreadySubscribed = result.AlreadySubscribed,
                Subscription = new SubscriptionDto
                {
                    Id = result.Subscription.SubscriptionId,
                    State = result.Subscription.State,
                    PlanHandle = result.Subscription.PlanHandle,
                    PlanName = result.Subscription.PlanName,
                    PriceInCents = result.Subscription.PriceInCents,
                    NextBillingDate = result.Subscription.NextBillingDate,
                    CreatedAt = result.Subscription.CreatedAt
                }
            };

            if (result.AlreadySubscribed)
            {
                return Results.Ok(response);
            }

            return Results.Created($"/api/subscriptions/{response.Subscription.Id}", response);
        }
        catch (Exception ex)
        {
            return SubscriptionEndpointErrorMapping.ToErrorResult(ex);
        }
    }

    private static IResult BadRequest(string message)
    {
        return Results.BadRequest(new ErrorDetails
        {
            StatusCode = StatusCodes.Status400BadRequest,
            Message = message
        });
    }
}
