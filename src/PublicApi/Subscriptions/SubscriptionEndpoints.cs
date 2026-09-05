using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

public sealed class SubscriptionPlansEndpoint : IEndpoint<IResult, SubscriptionPlansRequest, SubscriptionService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/subscription-plans", [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async (SubscriptionService service, HttpContext context) =>
            await HandleAsync(service, context.RequestAborted))
            .Produces<SubscriptionPlansResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public static async Task<IResult> HandleAsync(SubscriptionService service, System.Threading.CancellationToken cancellationToken)
    {
        try
        {
            var response = new SubscriptionPlansResponse();
            response.Plans.AddRange(await service.ListPlansAsync(cancellationToken));
            return Results.Ok(response);
        }
        catch (MaxioConfigurationException exception)
        {
            return Results.Problem(exception.Message, statusCode: StatusCodes.Status503ServiceUnavailable);
        }
        catch (MaxioApiException)
        {
            return Results.Problem("The subscription service is temporarily unavailable.", statusCode: StatusCodes.Status502BadGateway);
        }
    }

    // Required by the endpoint-discovery convention. HTTP-specific execution is routed above.
    public Task<IResult> HandleAsync(SubscriptionPlansRequest request, SubscriptionService service) =>
        HandleAsync(service, System.Threading.CancellationToken.None);
}

public sealed class SubscribeEndpoint : IEndpoint<IResult, SubscribeRequest, SubscriptionService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions", [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async (SubscribeRequest request, SubscriptionService service, HttpContext context) =>
            await HandleAsync(request, service, context, context.RequestAborted))
            .Produces<SubscribeResponse>(StatusCodes.Status201Created)
            .WithTags("SubscriptionEndpoints");
    }

    public static async Task<IResult> HandleAsync(SubscribeRequest request, SubscriptionService service, HttpContext context, System.Threading.CancellationToken cancellationToken)
    {
        try
        {
            var subscription = await service.SubscribeAsync(context.User, request.PlanHandle ?? string.Empty, cancellationToken);
            return Results.Created($"api/subscriptions/{subscription.Id}", new SubscribeResponse(subscription));
        }
        catch (InvalidSubscriptionPlanException exception)
        {
            return Results.BadRequest(new { error = exception.Message });
        }
        catch (CurrentUserNotFoundException)
        {
            return Results.Unauthorized();
        }
        catch (SubscriptionProvisioningInProgressException exception)
        {
            return Results.Conflict(new { error = exception.Message });
        }
        catch (MaxioConfigurationException exception)
        {
            return Results.Problem(exception.Message, statusCode: StatusCodes.Status503ServiceUnavailable);
        }
        catch (MaxioApiException)
        {
            return Results.Problem("The subscription service is temporarily unavailable.", statusCode: StatusCodes.Status502BadGateway);
        }
    }

    // Required by the endpoint-discovery convention. The route supplies the authenticated principal.
    public Task<IResult> HandleAsync(SubscribeRequest request, SubscriptionService service) =>
        Task.FromResult<IResult>(Results.Problem("An authenticated HTTP context is required.", statusCode: StatusCodes.Status500InternalServerError));
}

public sealed class MySubscriptionsEndpoint : IEndpoint<IResult, MySubscriptionsRequest, SubscriptionService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions", [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async (SubscriptionService service, HttpContext context) =>
            await HandleAsync(service, context, context.RequestAborted))
            .Produces<MySubscriptionsResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public static async Task<IResult> HandleAsync(SubscriptionService service, HttpContext context, System.Threading.CancellationToken cancellationToken)
    {
        try
        {
            var response = new MySubscriptionsResponse();
            response.Subscriptions.AddRange(await service.GetMySubscriptionsAsync(context.User, cancellationToken));
            return Results.Ok(response);
        }
        catch (CurrentUserNotFoundException)
        {
            return Results.Unauthorized();
        }
        catch (MaxioConfigurationException exception)
        {
            return Results.Problem(exception.Message, statusCode: StatusCodes.Status503ServiceUnavailable);
        }
        catch (MaxioApiException)
        {
            return Results.Problem("The subscription service is temporarily unavailable.", statusCode: StatusCodes.Status502BadGateway);
        }
    }

    // Required by the endpoint-discovery convention. The route supplies the authenticated principal.
    public Task<IResult> HandleAsync(MySubscriptionsRequest request, SubscriptionService service) =>
        Task.FromResult<IResult>(Results.Problem("An authenticated HTTP context is required.", statusCode: StatusCodes.Status500InternalServerError));
}
