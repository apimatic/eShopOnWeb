using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Subscribes the authenticated shopper to a plan (the hero "Subscribe" flow). Ensures a Maxio customer
/// exists for the shopper and enrolls them, idempotently, so a repeated/double-clicked request never creates
/// a second customer or subscription. The shopper's identity is taken from the JWT, not the request body.
/// </summary>
public class SubscribeEndpoint : IEndpoint<IResult, SubscribeRequest, HttpContext, ISubscriptionService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (SubscribeRequest request, HttpContext httpContext, ISubscriptionService subscriptionService) =>
            {
                return await HandleAsync(request, httpContext, subscriptionService);
            })
            .Produces<SubscribeResponse>(StatusCodes.Status201Created)
            .Produces<SubscribeResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status502BadGateway)
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(
        SubscribeRequest request, HttpContext httpContext, ISubscriptionService subscriptionService)
    {
        var shopper = ShopperIdentity.FromPrincipal(httpContext.User);
        if (shopper is null)
        {
            return Results.Unauthorized();
        }

        if (request is null || string.IsNullOrWhiteSpace(request.PlanHandle))
        {
            return Results.Problem(
                title: "A plan handle is required.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        var command = new SubscribeCommand(
            shopper.Reference, shopper.Email, shopper.FirstName, shopper.LastName, request.PlanHandle.Trim());

        CancellationToken cancellationToken = httpContext.RequestAborted;

        try
        {
            var result = await subscriptionService.SubscribeAsync(command, cancellationToken);

            var response = new SubscribeResponse
            {
                Subscription = result.Subscription.ToDto(),
                CustomerId = result.CustomerId,
                AlreadyExisted = result.AlreadyExisted,
                CustomerCreated = result.CustomerCreated,
                Message = BuildMessage(result),
            };

            return result.AlreadyExisted
                ? Results.Ok(response)
                : Results.Created("api/my-subscriptions", response);
        }
        catch (SubscriptionPlanNotFoundException ex)
        {
            return Results.Problem(
                title: ex.Message,
                statusCode: StatusCodes.Status404NotFound);
        }
        catch (SubscriptionBillingException ex)
        {
            // Upstream billing-system failure: surface as Bad Gateway rather than a generic 500.
            return Results.Problem(
                title: "The subscription could not be completed by the billing system.",
                detail: ex.Message,
                statusCode: StatusCodes.Status502BadGateway);
        }
    }

    private static string BuildMessage(SubscribeResult result)
    {
        var subscription = result.Subscription;
        if (result.AlreadyExisted)
        {
            return $"You are already subscribed to {subscription.PlanName} ({subscription.State}). " +
                   $"Next billing date: {FormatDate(subscription.NextBillingAt)}.";
        }

        return $"Subscribed to {subscription.PlanName} at {subscription.FormattedPrice}. " +
               $"Status: {subscription.State}. Next billing date: {FormatDate(subscription.NextBillingAt)}.";
    }

    private static string FormatDate(System.DateTimeOffset? date) =>
        date.HasValue ? date.Value.ToString("u") : "n/a";
}
