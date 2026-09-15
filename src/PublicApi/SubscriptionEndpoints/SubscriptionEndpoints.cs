using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Billing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Identity;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public sealed record SubscribeRequest(string ProductHandle);

public sealed class SubscriptionPlansEndpoint : IEndpoint<IResult, ISubscriptionBillingService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/subscription-plans", [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ISubscriptionBillingService billing, HttpContext context) =>
            await SubscriptionEndpointResults.ExecuteAsync(() => billing.GetPlansAsync(context.RequestAborted)))
            .Produces<IReadOnlyList<SubscriptionPlan>>()
            .WithTags("Subscriptions");
    }

    public Task<IResult> HandleAsync(ISubscriptionBillingService billing) =>
        SubscriptionEndpointResults.ExecuteAsync(() => billing.GetPlansAsync());
}

public sealed class CreateSubscriptionEndpoint : IEndpoint<IResult, ISubscriptionBillingService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions", [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (SubscribeRequest request, ISubscriptionBillingService billing, UserManager<ApplicationUser> users, HttpContext context) =>
            {
                var subscriber = await SubscriptionEndpointHelpers.GetSubscriberAsync(context, users);
                return subscriber is null
                    ? Results.Unauthorized()
                    : await SubscriptionEndpointResults.ExecuteAsync(() => billing.SubscribeAsync(subscriber, request.ProductHandle, context.RequestAborted));
            })
            .Produces<SubscriptionSummary>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .WithTags("Subscriptions");
    }

    // The authenticated-route lambda supplies the caller identity; this required scanner method is not used for HTTP dispatch.
    public Task<IResult> HandleAsync(ISubscriptionBillingService billing) =>
        Task.FromResult<IResult>(Results.Problem(statusCode: StatusCodes.Status500InternalServerError));
}

public sealed class MySubscriptionsEndpoint : IEndpoint<IResult, ISubscriptionBillingService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions", [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ISubscriptionBillingService billing, UserManager<ApplicationUser> users, HttpContext context) =>
            {
                var subscriber = await SubscriptionEndpointHelpers.GetSubscriberAsync(context, users);
                return subscriber is null
                    ? Results.Unauthorized()
                    : await SubscriptionEndpointResults.ExecuteAsync(() => billing.GetSubscriptionsAsync(subscriber, context.RequestAborted));
            })
            .Produces<IReadOnlyList<SubscriptionSummary>>()
            .WithTags("Subscriptions");
    }

    // The authenticated-route lambda supplies the caller identity; this required scanner method is not used for HTTP dispatch.
    public Task<IResult> HandleAsync(ISubscriptionBillingService billing) =>
        Task.FromResult<IResult>(Results.Problem(statusCode: StatusCodes.Status500InternalServerError));
}

internal static class SubscriptionEndpointHelpers
{
    public static async Task<SubscriptionSubscriber?> GetSubscriberAsync(HttpContext context, UserManager<ApplicationUser> users)
    {
        var name = context.User.Identity?.Name;
        if (string.IsNullOrWhiteSpace(name)) return null;
        var user = await users.FindByNameAsync(name);
        if (user is null || string.IsNullOrWhiteSpace(user.Email)) return null;

        var localPart = user.Email.Split('@', 2)[0];
        return new SubscriptionSubscriber(user.Id, user.Email, localPart, "Customer");
    }
}

internal static class SubscriptionEndpointResults
{
    public static async Task<IResult> ExecuteAsync<T>(Func<Task<T>> action)
    {
        try
        {
            return Results.Ok(await action());
        }
        catch (SubscriptionBillingException ex)
        {
            return Results.Problem(statusCode: ex.StatusCode, title: ex.Message);
        }
    }
}
