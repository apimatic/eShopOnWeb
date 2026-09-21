using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// The hero flow: subscribe the authenticated shopper to a plan. Ensures a Maxio customer exists for the
/// user (idempotently) and enrolls them, returning the plan/price/state/next-billing-date. JWT-authenticated;
/// the caller's identity comes from the token. Safe to repeat — a double-click returns the same subscription
/// rather than creating a duplicate.
/// </summary>
public class SubscribeEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (SubscribeRequest? request, ClaimsPrincipal user, ISubscriptionBillingService billing, CancellationToken cancellationToken) =>
                await HandleAsync(request, user, billing, cancellationToken))
            .Produces<SubscribeResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .ProducesProblem(StatusCodes.Status502BadGateway)
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(
        SubscribeRequest? request, ClaimsPrincipal user, ISubscriptionBillingService billing, CancellationToken cancellationToken)
    {
        if (!SubscriptionMappings.TryCreateSubscriber(user, out var subscriber))
        {
            return Results.Unauthorized();
        }

        try
        {
            var subscription = await billing.SubscribeAsync(subscriber, request?.PlanHandle, cancellationToken);
            var response = new SubscribeResponse { Subscription = SubscriptionMappings.ToDto(subscription) };
            return Results.Created("api/my-subscriptions", response);
        }
        catch (MaxioBillingException ex)
        {
            return SubscriptionMappings.ToProblemResult(ex);
        }
    }
}
