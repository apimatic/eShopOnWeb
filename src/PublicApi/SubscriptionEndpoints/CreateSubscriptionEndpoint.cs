using System.Threading.Tasks;
using BlazorShared.Models;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Subscribes the authenticated shopper to a plan, creating their Maxio customer if needed.
/// Repeating the call for a plan the shopper already holds returns the existing subscription.
/// </summary>
public class CreateSubscriptionEndpoint : IEndpoint<IResult, CreateSubscriptionRequest, HttpContext, ISubscriptionService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreateSubscriptionRequest request, HttpContext httpContext, ISubscriptionService subscriptionService) =>
            {
                return await HandleAsync(request, httpContext, subscriptionService);
            })
            .Produces<CreateSubscriptionResponse>(StatusCodes.Status201Created)
            .Produces<CreateSubscriptionResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(CreateSubscriptionRequest request, HttpContext httpContext,
        ISubscriptionService subscriptionService)
    {
        if (string.IsNullOrWhiteSpace(request.PlanHandle))
        {
            // Same error shape as ExceptionMiddleware produces for every other failure.
            return Results.Content(new ErrorDetails
            {
                StatusCode = StatusCodes.Status400BadRequest,
                Message = "planHandle is required."
            }.ToString(), "application/json", statusCode: StatusCodes.Status400BadRequest);
        }

        var response = new CreateSubscriptionResponse(request.CorrelationId());

        var result = await subscriptionService.SubscribeAsync(httpContext.User.Identity!.Name!, request.PlanHandle.Trim(),
            request.FirstName, request.LastName, httpContext.RequestAborted);

        response.Subscription = SubscriptionDto.From(result.Subscription);
        response.AlreadySubscribed = result.AlreadySubscribed;

        return result.AlreadySubscribed
            ? Results.Ok(response)
            : Results.Created("api/my-subscriptions", response);
    }
}
