using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.Infrastructure.Maxio;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Subscribes the authenticated shopper to a plan. Idempotent - if the shopper already has a
/// current subscription to the requested plan the existing subscription is returned instead of
/// creating a second one.
/// </summary>
public class CreateSubscriptionEndpoint : IEndpoint<IResult, CreateSubscriptionRequest, HttpContext, IMaxioSubscriptionService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreateSubscriptionRequest request, HttpContext httpContext,
                IMaxioSubscriptionService subscriptionService) =>
            {
                return await HandleAsync(request, httpContext, subscriptionService);
            })
            .Produces<CreateSubscriptionResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(
        CreateSubscriptionRequest request,
        HttpContext httpContext,
        IMaxioSubscriptionService subscriptionService)
    {
        var response = new CreateSubscriptionResponse(request.CorrelationId());

        var shopperEmail = httpContext.User.Identity?.Name;
        if (string.IsNullOrWhiteSpace(shopperEmail))
        {
            return Results.Unauthorized();
        }

        var planHandle = request.PlanHandle?.Trim();
        if (string.IsNullOrWhiteSpace(planHandle))
        {
            return Results.BadRequest(new { message = "PlanHandle is required." });
        }

        try
        {
            var result = await subscriptionService.SubscribeAsync(
                shopperEmail, shopperEmail, planHandle, httpContext.RequestAborted);

            response.Created = result.Created;
            response.Subscription = SubscriptionDtoMapper.ToSubscriptionDto(result.Subscription);

            return result.Created
                ? Results.Created("/api/my-subscriptions", response)
                : Results.Ok(response);
        }
        catch (SubscriptionPlanNotFoundException ex)
        {
            return Results.NotFound(new { message = ex.Message });
        }
        catch (MaxioApiException ex) when (ex.StatusCode >= 400 && ex.StatusCode < 500)
        {
            return Results.Json(new { message = ex.Message }, statusCode: ex.StatusCode);
        }
    }
}
