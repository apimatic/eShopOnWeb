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
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Subscribes the authenticated user to a plan. Ensures a Maxio customer exists for the
/// user (idempotent) and enrolls them; a double-click never creates a duplicate customer
/// or subscription. The subscriber's identity comes from the JWT, not the request body.
/// </summary>
public class SubscribeEndpoint : IEndpoint<IResult, SubscribeRequest, IMaxioBillingService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (SubscribeRequest request, ClaimsPrincipal user, IMaxioBillingService billingService, CancellationToken cancellationToken) =>
            {
                // Identity is taken from the token, overriding anything in the body.
                request.UserName = user.Identity?.Name;
                return await HandleAsync(request, billingService, cancellationToken);
            })
            .Produces<SubscribeResponse>(StatusCodes.Status201Created)
            .Produces<SubscribeResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status502BadGateway)
            .WithTags("SubscriptionEndpoints");
    }

    public Task<IResult> HandleAsync(SubscribeRequest request, IMaxioBillingService billingService)
        => HandleAsync(request, billingService, CancellationToken.None);

    public async Task<IResult> HandleAsync(SubscribeRequest request, IMaxioBillingService billingService, CancellationToken cancellationToken)
    {
        var response = new SubscribeResponse(request.CorrelationId());

        if (string.IsNullOrWhiteSpace(request.UserName))
            return Results.Unauthorized();

        if (string.IsNullOrWhiteSpace(request.PlanHandle))
            return Results.Problem(
                title: "Missing plan handle",
                detail: "A planHandle is required to subscribe.",
                statusCode: StatusCodes.Status400BadRequest);

        var subscriber = new SubscriberIdentity(request.UserName!);

        try
        {
            var result = await billingService.SubscribeAsync(subscriber, request.PlanHandle, cancellationToken);
            response.Subscription = result.Subscription.ToDto();
            response.AlreadySubscribed = result.AlreadySubscribed;

            return result.AlreadySubscribed
                ? Results.Ok(response)
                : Results.Created("api/my-subscriptions", response);
        }
        catch (MaxioBillingException ex)
        {
            return SubscriptionEndpointResults.FromBillingException(ex);
        }
    }
}
