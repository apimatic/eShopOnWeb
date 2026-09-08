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
/// Subscribes the authenticated shopper to a plan (POST /api/subscriptions). Idempotent: a repeated
/// subscribe (e.g. a double-click) never creates a second customer or subscription.
/// </summary>
public class CreateSubscriptionEndpoint : IEndpoint<IResult, CreateSubscriptionRequest, string, IMaxioSubscriptionService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
                [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
                (CreateSubscriptionRequest request, HttpContext httpContext, IMaxioSubscriptionService subscriptionService) =>
                {
                    string? userEmail = httpContext.User.Identity?.Name;
                    if (string.IsNullOrWhiteSpace(userEmail))
                    {
                        return Results.Unauthorized();
                    }

                    return await HandleAsync(request, userEmail, subscriptionService);
                })
            .Produces<CreateSubscriptionResponse>(StatusCodes.Status201Created)
            .Produces<CreateSubscriptionResponse>(StatusCodes.Status200OK)
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(CreateSubscriptionRequest request, string userEmail, IMaxioSubscriptionService subscriptionService)
    {
        if (string.IsNullOrWhiteSpace(request.ProductHandle))
        {
            return Results.BadRequest(new { message = "The 'productHandle' of the plan to subscribe to is required." });
        }

        var response = new CreateSubscriptionResponse(request.CorrelationId());

        var result = await subscriptionService.SubscribeAsync(userEmail, request.ProductHandle);

        response.Subscription = SubscriptionDto.FromMaxioSubscription(result.Subscription);
        response.Created = result.Created;

        if (result.Created)
        {
            return Results.Created("/api/my-subscriptions", response);
        }

        return Results.Ok(response);
    }
}
