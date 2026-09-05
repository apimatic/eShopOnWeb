using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.PublicApi.Maxio;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>Authenticated storefront subscription operations backed by Maxio Advanced Billing.</summary>
public sealed class SubscriptionEndpoints : IEndpoint<IResult, SubscriptionService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        var authenticated = new AuthorizeAttribute { AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme };

        app.MapGet("api/subscription-plans", async (SubscriptionService service, CancellationToken cancellationToken) =>
                Results.Ok(await service.GetPlansAsync(cancellationToken)))
            .RequireAuthorization(authenticated)
            .Produces<SubscriptionPlanResponse[]>()
            .WithTags("Subscriptions");

        app.MapPost("api/subscriptions", async (SubscribeRequest request, HttpContext context, SubscriptionService service, CancellationToken cancellationToken) =>
                await SubscribeAsync(request, context, service, cancellationToken))
            .RequireAuthorization(authenticated)
            .Produces<SubscriptionResponse>(StatusCodes.Status201Created)
            .ProducesValidationProblem()
            .WithTags("Subscriptions");

        app.MapGet("api/my-subscriptions", async (HttpContext context, SubscriptionService service, CancellationToken cancellationToken) =>
                await MySubscriptionsAsync(context, service, cancellationToken))
            .RequireAuthorization(authenticated)
            .Produces<MySubscriptionsResponse>()
            .WithTags("Subscriptions");
    }

    // This endpoint class registers three routes in AddRoute; there is no single route handler.
    public Task<IResult> HandleAsync(SubscriptionService service) => Task.FromResult<IResult>(Results.NotFound());

    private static async Task<IResult> SubscribeAsync(SubscribeRequest request, HttpContext context, SubscriptionService service, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.PlanHandle)) return Results.ValidationProblem(new Dictionary<string, string[]> { [nameof(request.PlanHandle)] = new[] { "PlanHandle is required." } });
        try
        {
            var subscription = await service.SubscribeAsync(context.User.Identity!.Name!, request.PlanHandle, cancellationToken);
            return Results.Created($"api/subscriptions/{subscription.Id}", subscription);
        }
        catch (SubscriptionValidationException exception)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]> { [nameof(request.PlanHandle)] = new[] { exception.Message } });
        }
        catch (MaxioApiException)
        {
            return Results.Problem("The billing service could not complete the enrollment. Please retry.", statusCode: StatusCodes.Status502BadGateway);
        }
    }

    private static async Task<IResult> MySubscriptionsAsync(HttpContext context, SubscriptionService service, CancellationToken cancellationToken)
    {
        try
        {
            return Results.Ok(await service.GetMySubscriptionsAsync(context.User.Identity!.Name!, cancellationToken));
        }
        catch (SubscriptionValidationException)
        {
            return Results.Unauthorized();
        }
        catch (MaxioApiException)
        {
            return Results.Problem("The billing service could not retrieve subscriptions. Please retry.", statusCode: StatusCodes.Status502BadGateway);
        }
    }
}
