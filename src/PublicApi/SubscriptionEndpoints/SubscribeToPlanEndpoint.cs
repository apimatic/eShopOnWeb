using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using BlazorShared.Models;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.PublicApi.Maxio;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class SubscribeToPlanEndpoint : IEndpoint<IResult, SubscribeToPlanRequest, ClaimsPrincipal, ISubscriptionService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (SubscribeToPlanRequest request, ClaimsPrincipal user, ISubscriptionService subscriptionService) =>
            {
                return await HandleAsync(request, user, subscriptionService);
            })
            .Accepts<SubscribeToPlanRequest>("application/json")
            .Produces<SubscribeToPlanResponse>(StatusCodes.Status200OK)
            .Produces<SubscribeToPlanResponse>(StatusCodes.Status201Created)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status502BadGateway)
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(SubscribeToPlanRequest request, ClaimsPrincipal principal, ISubscriptionService subscriptionService)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.PlanHandle))
        {
            return Results.BadRequest(new ErrorDetails { StatusCode = StatusCodes.Status400BadRequest, Message = "A planHandle is required." });
        }

        try
        {
            var enrollment = await subscriptionService.SubscribeAsync(principal, request.PlanHandle.Trim(), CancellationToken.None);
            var response = new SubscribeToPlanResponse
            {
                Subscription = enrollment.Subscription,
                Created = enrollment.Created
            };
            return enrollment.Created
                ? Results.Created("api/my-subscriptions", response)
                : Results.Ok(response);
        }
        catch (SubscriptionAccessDeniedException)
        {
            return Results.Unauthorized();
        }
        catch (InvalidSubscriptionRequestException ex)
        {
            return Results.BadRequest(new ErrorDetails { StatusCode = StatusCodes.Status400BadRequest, Message = ex.Message });
        }
        catch (MaxioApiException ex)
        {
            return Results.Json(new ErrorDetails { StatusCode = StatusCodes.Status502BadGateway, Message = ex.Message },
                statusCode: StatusCodes.Status502BadGateway);
        }
    }
}
