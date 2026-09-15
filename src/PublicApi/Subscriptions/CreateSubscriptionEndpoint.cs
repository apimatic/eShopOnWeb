using System;
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
/// Enrolls the authenticated eShop user in a Maxio subscription plan.
/// </summary>
public class CreateSubscriptionEndpoint : IEndpoint<IResult>
{
    public Task<IResult> HandleAsync()
    {
        throw new NotSupportedException("This endpoint is invoked through its route handler.");
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreateSubscriptionRequest request, ISubscriptionService subscriptions, UserManager<ApplicationUser> userManager, HttpContext context) =>
            {
                var user = await GetCurrentUserAsync(context, userManager);
                if (user == null)
                {
                    return Results.Unauthorized();
                }

                try
                {
                    var subscription = await subscriptions.SubscribeAsync(user, request.PlanHandle, context.RequestAborted);
                    return Results.Created($"api/subscriptions/{subscription.Id}", subscription);
                }
                catch (SubscriptionPlanNotFoundException exception)
                {
                    return Results.BadRequest(new { message = exception.Message });
                }
                catch (SubscriptionUserProfileException exception)
                {
                    return Results.BadRequest(new { message = exception.Message });
                }
                catch (SubscriptionEnrollmentInProgressException exception)
                {
                    return Results.Conflict(new { message = exception.Message });
                }
                catch (MaxioApiException)
                {
                    return Results.Problem("Subscription enrollment is temporarily unavailable.", statusCode: StatusCodes.Status502BadGateway);
                }
            })
            .Produces<SubscriptionDto>(StatusCodes.Status201Created)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status502BadGateway)
            .WithTags("Subscriptions");
    }

    internal static Task<ApplicationUser?> GetCurrentUserAsync(HttpContext context, UserManager<ApplicationUser> userManager)
    {
        var userName = context.User.Identity?.Name;
        return string.IsNullOrWhiteSpace(userName)
            ? Task.FromResult<ApplicationUser?>(null)
            : userManager.FindByNameAsync(userName);
    }
}
