using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Subscribes the authenticated shopper to a plan. Ensures a matching Maxio customer exists (idempotent)
/// and enrolls them; a repeated request for the same (user, plan) returns the existing subscription.
/// </summary>
public class CreateSubscriptionEndpoint : IEndpoint<IResult, CreateSubscriptionRequest, IMaxioBillingService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreateSubscriptionRequest request, ClaimsPrincipal user, IMaxioBillingService billingService, CancellationToken cancellationToken) =>
            {
                // Identity is server-authoritative: take it from the token, never from the request body.
                request.UserName = user.FindFirstValue(ClaimTypes.Name) ?? user.Identity?.Name;
                return await HandleAsync(request, billingService, cancellationToken);
            })
            .Produces<CreateSubscriptionResponse>()
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .WithTags("SubscriptionEndpoints");
    }

    public Task<IResult> HandleAsync(CreateSubscriptionRequest request, IMaxioBillingService billingService)
        => HandleAsync(request, billingService, default);

    public async Task<IResult> HandleAsync(CreateSubscriptionRequest request, IMaxioBillingService billingService, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.UserName))
        {
            return Results.Unauthorized();
        }

        if (string.IsNullOrWhiteSpace(request.PlanHandle))
        {
            return Results.BadRequest("A plan handle is required to subscribe.");
        }

        var subscriber = SubscriberIdentity.FromUserName(request.UserName);
        var result = await billingService.SubscribeAsync(subscriber, request.PlanHandle, cancellationToken);

        var response = new CreateSubscriptionResponse(request.CorrelationId())
        {
            Subscription = result.Subscription.ToDto(),
            AlreadyExisted = result.AlreadyExisted
        };

        return Results.Ok(response);
    }
}
