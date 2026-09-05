using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>Lists subscription plans configured in the Maxio product family.</summary>
public sealed class SubscriptionPlansEndpoint : IEndpoint<IResult, ISubscriptionService>
{
    public void AddRoute(IEndpointRouteBuilder app) => app.MapGet("api/subscription-plans",
        [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async (ISubscriptionService service, HttpContext context) =>
            await HandleAsync(service, context))
        .Produces<SubscriptionPlanResponse[]>()
        .WithTags("SubscriptionEndpoints");

    public async Task<IResult> HandleAsync(ISubscriptionService service) => Results.Ok(await service.GetPlansAsync(default));

    private async Task<IResult> HandleAsync(ISubscriptionService service, HttpContext context)
    {
        try { return Results.Ok(await service.GetPlansAsync(context.RequestAborted)); }
        catch (Exception exception) { return SubscriptionEndpointErrors.SubscriptionProblem(exception); }
    }
}

/// <summary>Creates or returns a shopper's idempotent enrollment in a Maxio plan.</summary>
public sealed class CreateSubscriptionEndpoint : IEndpoint<IResult, SubscribeRequest, HttpContext, ISubscriptionService>
{
    public void AddRoute(IEndpointRouteBuilder app) => app.MapPost("api/subscriptions",
        [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async (SubscribeRequest request, HttpContext context, ISubscriptionService service) =>
            await HandleAsync(request, context, service))
        .Accepts<SubscribeRequest>("application/json")
        .Produces<SubscriptionResponse>()
        .WithTags("SubscriptionEndpoints");

    public Task<IResult> HandleAsync(SubscribeRequest request, HttpContext context, ISubscriptionService service) => HandleAsync(request, context, service, context.RequestAborted);

    private static async Task<IResult> HandleAsync(SubscribeRequest request, HttpContext context, ISubscriptionService service, System.Threading.CancellationToken cancellationToken)
    {
        try { return Results.Ok(await service.SubscribeAsync(context.User, request, cancellationToken)); }
        catch (Exception exception) { return SubscriptionEndpointErrors.SubscriptionProblem(exception); }
    }
}

/// <summary>Lists the authenticated shopper's subscriptions from Maxio.</summary>
public sealed class MySubscriptionsEndpoint : IEndpoint<IResult, HttpContext, ISubscriptionService>
{
    public void AddRoute(IEndpointRouteBuilder app) => app.MapGet("api/my-subscriptions",
        [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async (HttpContext context, ISubscriptionService service) =>
            await HandleAsync(context, service))
        .Produces<SubscriptionResponse[]>()
        .WithTags("SubscriptionEndpoints");

    public Task<IResult> HandleAsync(HttpContext context, ISubscriptionService service) => HandleAsync(context, service, context.RequestAborted);

    private static async Task<IResult> HandleAsync(HttpContext context, ISubscriptionService service, System.Threading.CancellationToken cancellationToken)
    {
        try { return Results.Ok(await service.GetMySubscriptionsAsync(context.User, cancellationToken)); }
        catch (Exception exception) { return SubscriptionEndpointErrors.SubscriptionProblem(exception); }
    }
}

internal static class SubscriptionEndpointErrors
{
    public static IResult SubscriptionProblem(Exception exception) => exception switch
    {
        ArgumentException => Results.BadRequest(new { error = exception.Message }),
        UnauthorizedAccessException => Results.Unauthorized(),
        MaxioConfigurationException => Results.Problem("The subscription service is not configured correctly.", statusCode: StatusCodes.Status503ServiceUnavailable),
        MaxioBillingException maxio when (int)maxio.StatusCode >= 400 && (int)maxio.StatusCode < 500 => Results.Problem("Maxio could not accept this subscription request.", statusCode: StatusCodes.Status422UnprocessableEntity),
        MaxioBillingException => Results.Problem("Maxio is temporarily unavailable.", statusCode: StatusCodes.Status502BadGateway),
        _ => Results.Problem("The subscription request could not be completed.", statusCode: StatusCodes.Status500InternalServerError)
    };
}
