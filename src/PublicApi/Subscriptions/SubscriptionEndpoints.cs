using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

public sealed record CreateSubscriptionRequest(string PlanHandle);

public sealed record CreateSubscriptionResponse(SubscriptionDetails Subscription, bool Created);

/// <summary>JWT-protected subscription routes backed by Maxio Advanced Billing.</summary>
public sealed class SubscriptionEndpoints : IEndpoint<IResult, IMaxioAdvancedBillingClient>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/subscription-plans", [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (IMaxioAdvancedBillingClient maxio, CancellationToken cancellationToken) =>
            {
                try
                {
                    return Results.Ok(await maxio.GetPlansAsync(cancellationToken));
                }
                catch (Exception ex) when (ex is MaxioApiException or InvalidOperationException)
                {
                    return BillingUnavailable();
                }
            })
            .Produces<IReadOnlyList<SubscriptionPlan>>()
            .ProducesProblem(StatusCodes.Status502BadGateway)
            .WithTags("Subscriptions");

        app.MapPost("api/subscriptions", [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (CreateSubscriptionRequest request, HttpContext context, CurrentMaxioUserAccessor currentUser,
                IMaxioAdvancedBillingClient maxio, CancellationToken cancellationToken) =>
            {
                if (string.IsNullOrWhiteSpace(request.PlanHandle))
                {
                    return Results.ValidationProblem(new Dictionary<string, string[]> { ["planHandle"] = new[] { "A plan handle is required." } });
                }

                var customer = await currentUser.GetAsync(context.User);
                if (customer is null)
                {
                    return Results.Problem(statusCode: StatusCodes.Status401Unauthorized, title: "Authenticated user was not found.");
                }

                try
                {
                    var enrollment = await maxio.SubscribeAsync(customer, request.PlanHandle, cancellationToken);
                    return Results.Ok(new CreateSubscriptionResponse(enrollment.Subscription, enrollment.Created));
                }
                catch (ArgumentException)
                {
                    return Results.ValidationProblem(new Dictionary<string, string[]> { ["planHandle"] = new[] { "The requested plan is not available." } });
                }
                catch (Exception ex) when (ex is MaxioApiException or InvalidOperationException)
                {
                    return BillingUnavailable();
                }
            })
            .Produces<CreateSubscriptionResponse>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status502BadGateway)
            .WithTags("Subscriptions");

        app.MapGet("api/my-subscriptions", [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (HttpContext context, CurrentMaxioUserAccessor currentUser, IMaxioAdvancedBillingClient maxio,
                CancellationToken cancellationToken) =>
            {
                var customer = await currentUser.GetAsync(context.User);
                if (customer is null)
                {
                    return Results.Problem(statusCode: StatusCodes.Status401Unauthorized, title: "Authenticated user was not found.");
                }

                try
                {
                    return Results.Ok(await maxio.GetSubscriptionsAsync(customer, cancellationToken));
                }
                catch (Exception ex) when (ex is MaxioApiException or InvalidOperationException)
                {
                    return BillingUnavailable();
                }
            })
            .Produces<IReadOnlyList<SubscriptionDetails>>()
            .ProducesProblem(StatusCodes.Status502BadGateway)
            .WithTags("Subscriptions");
    }

    private static IResult BillingUnavailable() => Results.Problem(
        statusCode: StatusCodes.Status502BadGateway,
        title: "Subscription billing is temporarily unavailable.");

    // Required by the existing MinimalApi.Endpoint registration convention.
    public async Task<IResult> HandleAsync(IMaxioAdvancedBillingClient maxio)
    {
        try
        {
            return Results.Ok(await maxio.GetPlansAsync());
        }
        catch (Exception ex) when (ex is MaxioApiException or InvalidOperationException)
        {
            return BillingUnavailable();
        }
    }
}
