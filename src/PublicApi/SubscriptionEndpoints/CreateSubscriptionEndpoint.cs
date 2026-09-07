using System;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class CreateSubscriptionEndpoint : IEndpoint<IResult, SubscribeRequest>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (SubscribeRequest request, ISubscriptionService subscriptionService, HttpContext httpContext) =>
            {
                var response = new CreateSubscriptionResponse(request.CorrelationId());

                try
                {
                    var userId = httpContext.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
                    if (string.IsNullOrEmpty(userId))
                    {
                        return Results.Unauthorized();
                    }

                    var subscription = await subscriptionService.CreateSubscriptionAsync(userId, request.ProductId);
                    if (subscription == null)
                    {
                        return Results.BadRequest(new { error = "Failed to create subscription" });
                    }

                    response.Subscription = subscription;
                    return Results.Created($"api/subscriptions/{subscription.Id}", response);
                }
                catch (Exception ex)
                {
                    return Results.Problem($"Error creating subscription: {ex.Message}");
                }
            })
            .Produces<CreateSubscriptionResponse>()
            .WithName("CreateSubscription")
            .WithTags("SubscriptionEndpoints");
    }

    public Task<IResult> HandleAsync(SubscribeRequest request) => throw new NotImplementedException();
}
