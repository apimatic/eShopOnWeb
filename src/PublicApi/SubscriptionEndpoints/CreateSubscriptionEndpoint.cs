using System;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.Infrastructure.Identity;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class CreateSubscriptionEndpoint : IEndpoint<IResult, CreateSubscriptionRequest, MaxioSubscriptionService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreateSubscriptionRequest request, MaxioSubscriptionService subscriptionService, UserManager<ApplicationUser> userManager, HttpContext httpContext) =>
            {
                var response = new CreateSubscriptionResponse(request.CorrelationId());

                var userId = httpContext.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
                if (string.IsNullOrEmpty(userId))
                {
                    return Results.Unauthorized();
                }

                var user = await userManager.FindByIdAsync(userId);
                if (user == null)
                {
                    return Results.NotFound("User not found");
                }

                try
                {
                    var subscription = await subscriptionService.CreateSubscriptionAsync(user, request.ProductHandle);
                    if (subscription != null)
                    {
                        response.Subscription = subscription;
                        return Results.Created($"api/subscriptions/{subscription.Id}", response);
                    }
                    return Results.StatusCode(500);
                }
                catch (InvalidOperationException ex)
                {
                    return Results.BadRequest(new { error = ex.Message });
                }
                catch (Exception)
                {
                    return Results.StatusCode(500);
                }
            })
           .Produces<CreateSubscriptionResponse>()
           .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(
        CreateSubscriptionRequest request,
        MaxioSubscriptionService subscriptionService)
    {
        return await Task.FromResult(Results.BadRequest());
    }
}
