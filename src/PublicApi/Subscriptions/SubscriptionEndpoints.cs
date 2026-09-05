using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

public sealed class SubscriptionPlansEndpoint : IEndpoint<IResult, MaxioSubscriptionService>
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public SubscriptionPlansEndpoint(IHttpContextAccessor httpContextAccessor) => _httpContextAccessor = httpContextAccessor;

    public void AddRoute(IEndpointRouteBuilder app) =>
        app.MapGet("api/subscription-plans", [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (MaxioSubscriptionService subscriptions) => await HandleAsync(subscriptions))
            .Produces<IReadOnlyList<SubscriptionPlanResponse>>()
            .WithTags("Subscriptions");

    public async Task<IResult> HandleAsync(MaxioSubscriptionService subscriptions) =>
        await SubscriptionEndpointResults.ExecuteAsync(() => subscriptions.ListPlansAsync(_httpContextAccessor.HttpContext!.RequestAborted));
}

public sealed class CreateSubscriptionEndpoint : IEndpoint<IResult, SubscribeRequest, MaxioSubscriptionService>
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public CreateSubscriptionEndpoint(IHttpContextAccessor httpContextAccessor) => _httpContextAccessor = httpContextAccessor;

    public void AddRoute(IEndpointRouteBuilder app) =>
        app.MapPost("api/subscriptions", [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (SubscribeRequest request, MaxioSubscriptionService subscriptions) => await HandleAsync(request, subscriptions))
            .Produces<SubscriptionResponse>(StatusCodes.Status201Created)
            .WithTags("Subscriptions");

    public async Task<IResult> HandleAsync(SubscribeRequest request, MaxioSubscriptionService subscriptions)
    {
        var context = _httpContextAccessor.HttpContext!;
        return await SubscriptionEndpointResults.ExecuteAsync(() => subscriptions.SubscribeAsync(context.User, request.ProductHandle, context.RequestAborted), created: true);
    }
}

public sealed class MySubscriptionsEndpoint : IEndpoint<IResult, MaxioSubscriptionService>
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public MySubscriptionsEndpoint(IHttpContextAccessor httpContextAccessor) => _httpContextAccessor = httpContextAccessor;

    public void AddRoute(IEndpointRouteBuilder app) =>
        app.MapGet("api/my-subscriptions", [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (MaxioSubscriptionService subscriptions) => await HandleAsync(subscriptions))
            .Produces<IReadOnlyList<SubscriptionResponse>>()
            .WithTags("Subscriptions");

    public async Task<IResult> HandleAsync(MaxioSubscriptionService subscriptions)
    {
        var context = _httpContextAccessor.HttpContext!;
        return await SubscriptionEndpointResults.ExecuteAsync(() => subscriptions.GetMySubscriptionsAsync(context.User, context.RequestAborted));
    }
}

internal static class SubscriptionEndpointResults
{
    public static async Task<IResult> ExecuteAsync<T>(Func<Task<T>> action, bool created = false)
    {
        try
        {
            var result = await action();
            return created ? Results.Created("api/my-subscriptions", result) : Results.Ok(result);
        }
        catch (ArgumentException ex)
        {
            return Results.Problem(ex.Message, statusCode: StatusCodes.Status400BadRequest);
        }
        catch (MaxioProviderException ex)
        {
            var status = ex.StatusCode is >= 400 and < 500 ? ex.StatusCode.Value : StatusCodes.Status502BadGateway;
            return Results.Problem(ex.Message, statusCode: status);
        }
    }
}
