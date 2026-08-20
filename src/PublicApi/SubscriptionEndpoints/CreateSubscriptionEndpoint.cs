using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.Infrastructure.Identity;
using Microsoft.eShopWeb.PublicApi.Subscriptions;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public sealed class CreateSubscriptionEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions", async (CreateSubscriptionRequest request, ClaimsPrincipal principal,
                ISubscriptionService subscriptionService, UserManager<ApplicationUser> userManager,
                CancellationToken cancellationToken) =>
                await HandleAsync(request, principal, subscriptionService, userManager, cancellationToken))
            .RequireAuthorization(new AuthorizeAttribute
            {
                AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme
            })
            .Produces<CreateSubscriptionResponse>(StatusCodes.Status201Created)
            .Produces<CreateSubscriptionResponse>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status502BadGateway)
            .WithTags("SubscriptionEndpoints");
    }

    public static async Task<IResult> HandleAsync(CreateSubscriptionRequest request, ClaimsPrincipal principal,
        ISubscriptionService subscriptionService, UserManager<ApplicationUser> userManager,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.ProductHandle))
        {
            return Results.Problem(statusCode: StatusCodes.Status400BadRequest,
                title: "A product handle is required.");
        }

        var username = principal.Identity?.Name;
        var user = string.IsNullOrWhiteSpace(username) ? null : await userManager.FindByNameAsync(username);
        if (user == null)
        {
            return Results.Unauthorized();
        }

        try
        {
            var result = await subscriptionService.SubscribeAsync(user, request.ProductHandle,
                cancellationToken);
            var response = new CreateSubscriptionResponse
            {
                Subscription = result.Subscription,
                AlreadySubscribed = result.AlreadySubscribed
            };

            return result.AlreadySubscribed
                ? Results.Ok(response)
                : Results.Created("/api/my-subscriptions", response);
        }
        catch (SubscriptionPlanNotFoundException exception)
        {
            return Results.Problem(statusCode: StatusCodes.Status400BadRequest,
                title: "Subscription plan unavailable", detail: exception.Message);
        }
    }
}
