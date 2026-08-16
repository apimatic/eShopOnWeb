using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Maxio;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Subscribes the authenticated shopper to a plan. Idempotent: ensures a single Maxio customer
/// for the shopper and returns the existing subscription (HTTP 200) instead of creating a
/// duplicate when one already exists; otherwise creates it (HTTP 201). The shopper is
/// identified by the JWT, never by the request body.
/// </summary>
public class CreateSubscriptionEndpoint
    : IEndpoint<IResult, CreateSubscriptionRequest, ClaimsPrincipal, ISubscriptionBillingService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreateSubscriptionRequest request, ClaimsPrincipal user,
                   ISubscriptionBillingService billingService, CancellationToken cancellationToken) =>
                await ExecuteAsync(request, user, billingService, cancellationToken))
            .Produces<CreateSubscriptionResponse>(StatusCodes.Status201Created)
            .Produces<CreateSubscriptionResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status502BadGateway)
            .WithTags("SubscriptionEndpoints");
    }

    // The interface entry point; the route wires cancellation through ExecuteAsync directly.
    public Task<IResult> HandleAsync(
        CreateSubscriptionRequest request, ClaimsPrincipal user, ISubscriptionBillingService billingService)
        => ExecuteAsync(request, user, billingService, CancellationToken.None);

    private static async Task<IResult> ExecuteAsync(
        CreateSubscriptionRequest request,
        ClaimsPrincipal user,
        ISubscriptionBillingService billingService,
        CancellationToken cancellationToken)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.PlanHandle))
        {
            return Results.BadRequest(new { errors = new[] { "planHandle is required." } });
        }

        var subscriber = SubscriberIdentityFactory.FromPrincipal(user);
        if (subscriber is null)
        {
            return Results.Unauthorized();
        }

        try
        {
            var subscription = await billingService.SubscribeAsync(
                subscriber, request.PlanHandle.Trim(), request.PricePointHandle, cancellationToken);

            var response = new CreateSubscriptionResponse(request.CorrelationId())
            {
                Subscription = subscription.ToDto(),
                AlreadyExisted = subscription.AlreadyExisted,
            };

            return subscription.AlreadyExisted
                ? Results.Ok(response)
                : Results.Created("api/my-subscriptions", response);
        }
        catch (BillingException ex)
        {
            return Results.BadRequest(new { errors = ex.Errors });
        }
        catch (MaxioApiException ex)
        {
            return Results.Problem(
                title: "The billing provider could not be reached.",
                detail: ex.Message,
                statusCode: StatusCodes.Status502BadGateway);
        }
    }
}
