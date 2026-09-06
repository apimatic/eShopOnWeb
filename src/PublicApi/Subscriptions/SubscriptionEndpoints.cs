using System.ComponentModel.DataAnnotations;
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
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

public sealed class SubscriptionPlansEndpoint : IEndpoint<IResult, object, MaxioSubscriptionService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/subscription-plans",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async (MaxioSubscriptionService subscriptions, CancellationToken cancellationToken) =>
                Results.Ok(await subscriptions.GetPlansAsync(cancellationToken)))
            .Produces<SubscriptionPlanDto[]>()
            .WithTags("Subscriptions");
    }

    public Task<IResult> HandleAsync(object request, MaxioSubscriptionService response) => throw new System.NotSupportedException();
}

public sealed class CreateSubscriptionEndpoint : IEndpoint<IResult, SubscribeRequest, MaxioSubscriptionService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async (SubscribeRequest request, HttpContext context, UserManager<ApplicationUser> users, MaxioSubscriptionService subscriptions, CancellationToken cancellationToken) =>
            {
                if (string.IsNullOrWhiteSpace(request.ProductHandle))
                {
                    return Results.ValidationProblem(new System.Collections.Generic.Dictionary<string, string[]> { [nameof(request.ProductHandle)] = new[] { "A product handle is required." } });
                }

                var user = await SubscriptionEndpointUser.GetAsync(context.User, users);
                return user is null
                    ? Results.Unauthorized()
                    : Results.Created($"api/subscriptions/{request.ProductHandle}", await subscriptions.SubscribeAsync(user, request.ProductHandle, cancellationToken));
            })
            .Produces<SubscriptionDto>(StatusCodes.Status201Created)
            .ProducesValidationProblem()
            .WithTags("Subscriptions");
    }

    public Task<IResult> HandleAsync(SubscribeRequest request, MaxioSubscriptionService response) => throw new System.NotSupportedException();
}

public sealed class MySubscriptionsEndpoint : IEndpoint<IResult, object, MaxioSubscriptionService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async (HttpContext context, UserManager<ApplicationUser> users, MaxioSubscriptionService subscriptions, CancellationToken cancellationToken) =>
            {
                var user = await SubscriptionEndpointUser.GetAsync(context.User, users);
                return user is null ? Results.Unauthorized() : Results.Ok(await subscriptions.GetMySubscriptionsAsync(user, cancellationToken));
            })
            .Produces<SubscriptionDto[]>()
            .WithTags("Subscriptions");
    }

    public Task<IResult> HandleAsync(object request, MaxioSubscriptionService response) => throw new System.NotSupportedException();
}

public sealed class SubscribeRequest
{
    [Required]
    public string ProductHandle { get; init; } = string.Empty;
}

internal static class SubscriptionEndpointUser
{
    public static Task<ApplicationUser?> GetAsync(ClaimsPrincipal principal, UserManager<ApplicationUser> users)
    {
        var userId = principal.FindFirstValue(ClaimTypes.NameIdentifier);
        return string.IsNullOrWhiteSpace(userId) ? Task.FromResult<ApplicationUser?>(null) : users.FindByIdAsync(userId);
    }
}
