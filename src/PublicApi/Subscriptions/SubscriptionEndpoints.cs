using System;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

/// <summary>JWT-protected subscription endpoints backed by Maxio Advanced Billing.</summary>
public sealed class SubscriptionEndpoints : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/subscription-plans", async (ISubscriptionService subscriptions, CancellationToken cancellationToken) =>
            await ExecuteAsync(() => subscriptions.ListPlansAsync(cancellationToken)))
            .RequireAuthorization()
            .Produces<SubscriptionPlanResponse[]>()
            .WithTags("Subscriptions");

        app.MapPost("api/subscriptions", async (SubscribeRequest request, ClaimsPrincipal user,
            ISubscriptionService subscriptions, CancellationToken cancellationToken) =>
        {
            var userName = user.Identity?.Name;
            return string.IsNullOrWhiteSpace(userName)
                ? Results.Unauthorized()
                : await ExecuteAsync(() => subscriptions.SubscribeAsync(userName, request.PlanHandle, cancellationToken));
        })
        .RequireAuthorization()
        .Produces<SubscriptionResponse>(StatusCodes.Status201Created)
        .WithTags("Subscriptions");

        app.MapGet("api/my-subscriptions", async (ClaimsPrincipal user, ISubscriptionService subscriptions,
            CancellationToken cancellationToken) =>
        {
            var userName = user.Identity?.Name;
            return string.IsNullOrWhiteSpace(userName)
                ? Results.Unauthorized()
                : await ExecuteAsync(() => subscriptions.ListMySubscriptionsAsync(userName, cancellationToken));
        })
        .RequireAuthorization()
        .Produces<SubscriptionResponse[]>()
        .WithTags("Subscriptions");
    }

    private static async Task<IResult> ExecuteAsync<T>(Func<Task<T>> operation)
    {
        try
        {
            var result = await operation();
            return result is SubscriptionResponse subscription
                ? Results.Created($"api/subscriptions/{subscription.Id}", subscription)
                : Results.Ok(result);
        }
        catch (SubscriptionValidationException exception)
        {
            return Results.Problem(exception.Message, statusCode: StatusCodes.Status400BadRequest);
        }
        catch (MaxioConfigurationException exception)
        {
            return Results.Problem(exception.Message, statusCode: StatusCodes.Status503ServiceUnavailable);
        }
        catch (MaxioApiException exception)
        {
            var statusCode = exception.StatusCode == System.Net.HttpStatusCode.UnprocessableEntity ||
                             exception.StatusCode == System.Net.HttpStatusCode.BadRequest
                ? StatusCodes.Status422UnprocessableEntity
                : StatusCodes.Status502BadGateway;
            return Results.Problem(exception.Message, statusCode: statusCode, title: "Maxio Advanced Billing request failed");
        }
    }
}
