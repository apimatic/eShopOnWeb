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
/// Subscribes the authenticated caller to a plan. Ensures a Maxio customer exists for the eShop user
/// and enrolls them. Idempotent: a double-click never creates a second customer or a duplicate
/// subscription — the existing subscription is returned instead.
/// </summary>
public class SubscribeEndpoint : IEndpoint<IResult, SubscribeRequest, IMaxioBillingService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (SubscribeRequest request, ClaimsPrincipal user, IMaxioBillingService billingService, CancellationToken cancellationToken) =>
            {
                if (!SubscriptionUser.TryResolve(user, out var identity))
                {
                    return Results.Unauthorized();
                }

                request.SetIdentity(identity);
                return await HandleAsync(request, billingService, cancellationToken);
            })
            .Produces<SubscribeResponse>()
            .Produces<SubscribeResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status502BadGateway)
            .WithTags("SubscriptionEndpoints")
            .WithName("Subscribe");
    }

    public Task<IResult> HandleAsync(SubscribeRequest request, IMaxioBillingService billingService)
        => HandleAsync(request, billingService, CancellationToken.None);

    public async Task<IResult> HandleAsync(SubscribeRequest request, IMaxioBillingService billingService, CancellationToken cancellationToken)
    {
        var response = new SubscribeResponse(request.CorrelationId());

        if (string.IsNullOrWhiteSpace(request.PlanHandle))
        {
            throw new MaxioBillingException("A 'planHandle' is required to subscribe.", StatusCodes.Status400BadRequest);
        }

        // Identity is populated in AddRoute from the JWT; guard defensively.
        if (request.Identity is null)
        {
            return Results.Unauthorized();
        }

        var result = await billingService.SubscribeAsync(request.Identity, request.PlanHandle.Trim(), cancellationToken);

        response.Subscription = CustomerSubscriptionDto.FromDomain(result.Subscription);
        response.AlreadySubscribed = !result.WasCreated;
        response.MaxioCustomerId = result.MaxioCustomerId;

        return result.WasCreated
            ? Results.Created($"api/my-subscriptions", response)
            : Results.Ok(response);
    }
}
