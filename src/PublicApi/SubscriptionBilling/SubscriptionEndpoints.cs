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

namespace Microsoft.eShopWeb.PublicApi.SubscriptionBilling;

public sealed class SubscriptionPlansEndpoint : IEndpoint<IResult, ISubscriptionBillingService>
{
    public Task<IResult> HandleAsync(ISubscriptionBillingService _) => Task.FromResult<IResult>(Results.StatusCode(StatusCodes.Status405MethodNotAllowed));

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/subscription-plans",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ISubscriptionBillingService billing, HttpContext context) =>
            {
                try
                {
                    return Results.Ok(new SubscriptionPlansResponse(await billing.GetPlansAsync(context.RequestAborted)));
                }
                catch (BillingException exception)
                {
                    return SubscriptionEndpointResults.Problem(exception);
                }
            })
            .Produces<SubscriptionPlansResponse>()
            .RequireAuthorization()
            .WithTags("SubscriptionBilling");
    }
}

public sealed class CreateSubscriptionEndpoint : IEndpoint<IResult, ISubscriptionBillingService>
{
    public Task<IResult> HandleAsync(ISubscriptionBillingService _) => Task.FromResult<IResult>(Results.StatusCode(StatusCodes.Status405MethodNotAllowed));

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreateSubscriptionRequest request, ISubscriptionBillingService billing, UserManager<ApplicationUser> userManager, HttpContext context) =>
            {
                try
                {
                    var shopper = await ResolveShopperAsync(context.User, userManager);
                    if (shopper is null)
                    {
                        return Results.Unauthorized();
                    }

                    var subscription = await billing.SubscribeAsync(shopper, request.PlanHandle, context.RequestAborted);
                    return Results.Ok(new SubscriptionResponse(subscription, true));
                }
                catch (BillingException exception)
                {
                    return SubscriptionEndpointResults.Problem(exception);
                }
            })
            .Produces<SubscriptionResponse>()
            .RequireAuthorization()
            .WithTags("SubscriptionBilling");
    }

    private static async Task<Shopper?> ResolveShopperAsync(ClaimsPrincipal principal, UserManager<ApplicationUser> userManager)
    {
        var userId = principal.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(userId))
        {
            return null;
        }

        var user = await userManager.FindByIdAsync(userId);
        if (user?.Email is null || user.UserName is null)
        {
            return null;
        }

        return new Shopper(user.Id, user.Email, user.UserName);
    }
}

public sealed class MySubscriptionsEndpoint : IEndpoint<IResult, ISubscriptionBillingService>
{
    public Task<IResult> HandleAsync(ISubscriptionBillingService _) => Task.FromResult<IResult>(Results.StatusCode(StatusCodes.Status405MethodNotAllowed));

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ISubscriptionBillingService billing, HttpContext context) =>
            {
                try
                {
                    var userId = context.User.FindFirstValue(ClaimTypes.NameIdentifier);
                    if (string.IsNullOrWhiteSpace(userId))
                    {
                        return Results.Unauthorized();
                    }

                    return Results.Ok(new MySubscriptionsResponse(await billing.GetSubscriptionsAsync(userId, context.RequestAborted)));
                }
                catch (BillingException exception)
                {
                    return SubscriptionEndpointResults.Problem(exception);
                }
            })
            .Produces<MySubscriptionsResponse>()
            .RequireAuthorization()
            .WithTags("SubscriptionBilling");
    }
}

file static class SubscriptionEndpointResults
{
    public static IResult Problem(BillingException exception) => Results.Problem(
        title: "Subscription billing request failed",
        detail: exception.Message,
        statusCode: exception.StatusCode);
}
