using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.Infrastructure.Identity;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

/// <summary>
/// JWT-protected recurring-subscription endpoints backed by Maxio Advanced Billing.
/// </summary>
public sealed class SubscriptionEndpoints : IEndpoint<IResult>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        var authorize = new AuthorizeAttribute { AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme };

        app.MapGet("api/subscription-plans", async (ISubscriptionService subscriptions, HttpContext context) =>
            Results.Ok(await subscriptions.GetPlansAsync(context.RequestAborted)))
            .RequireAuthorization(authorize)
            .Produces<SubscriptionPlanDto[]>()
            .WithTags("Subscriptions");

        app.MapPost("api/subscriptions", async (
            SubscribeRequest request,
            UserManager<ApplicationUser> userManager,
            ISubscriptionService subscriptions,
            HttpContext context) =>
        {
            var user = await userManager.FindByNameAsync(context.User.Identity?.Name ?? string.Empty);
            if (user is null)
            {
                return Results.Unauthorized();
            }

            try
            {
                var result = await subscriptions.SubscribeAsync(user, request.PlanHandle, context.RequestAborted);
                return result.Created
                    ? Results.Created($"api/subscriptions/{result.Subscription.Id}", result)
                    : Results.Ok(result);
            }
            catch (UnknownSubscriptionPlanException exception)
            {
                return Results.BadRequest(new { message = exception.Message });
            }
            catch (SubscriptionCustomerDataException exception)
            {
                return Results.BadRequest(new { message = exception.Message });
            }
            catch (SubscriptionEnrollmentInProgressException exception)
            {
                return Results.Conflict(new { message = exception.Message });
            }
        })
        .RequireAuthorization(authorize)
        .Produces<SubscribeResponse>(StatusCodes.Status201Created)
        .Produces<SubscribeResponse>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status401Unauthorized)
        .Produces(StatusCodes.Status409Conflict)
        .WithTags("Subscriptions");

        app.MapGet("api/my-subscriptions", async (
            UserManager<ApplicationUser> userManager,
            ISubscriptionService subscriptions,
            HttpContext context) =>
        {
            var user = await userManager.FindByNameAsync(context.User.Identity?.Name ?? string.Empty);
            if (user is null)
            {
                return Results.Unauthorized();
            }

            var result = await subscriptions.GetMySubscriptionsAsync(user, context.RequestAborted);
            return Results.Ok(new MySubscriptionsResponse { Subscriptions = result });
        })
        .RequireAuthorization(authorize)
        .Produces<MySubscriptionsResponse>()
        .Produces(StatusCodes.Status401Unauthorized)
        .WithTags("Subscriptions");
    }

    // IEndpoint requires a handler even though this route group exposes three HTTP operations.
    // Routing always enters through AddRoute above.
    public Task<IResult> HandleAsync() => Task.FromResult(Results.NotFound());
}
