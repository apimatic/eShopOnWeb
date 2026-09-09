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
/// Subscribes the authenticated shopper to a plan. Ensures a single Maxio customer exists for the
/// eShopOnWeb user and enrolls them idempotently (a double-click will not create duplicates). The
/// subscriber's identity comes from the JWT, never from the request body.
/// </summary>
public class CreateSubscriptionEndpoint : IEndpoint<IResult, SubscribeRequest, ISubscriptionBillingService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (SubscribeRequest request, ClaimsPrincipal user, ISubscriptionBillingService billing, CancellationToken cancellationToken) =>
            {
                request.UserName = SubscriptionEndpointHelpers.GetUserName(user) ?? string.Empty;
                return await HandleAsync(request, billing, cancellationToken);
            })
            .Produces<SubscribeResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status502BadGateway)
            .WithTags("SubscriptionEndpoints");
    }

    public Task<IResult> HandleAsync(SubscribeRequest request, ISubscriptionBillingService billing)
        => HandleAsync(request, billing, CancellationToken.None);

    private async Task<IResult> HandleAsync(SubscribeRequest request, ISubscriptionBillingService billing, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.UserName))
        {
            return Results.Unauthorized();
        }

        if (string.IsNullOrWhiteSpace(request.PlanHandle))
        {
            return Results.Problem(
                detail: "A 'planHandle' is required to subscribe.",
                statusCode: StatusCodes.Status400BadRequest,
                title: "Missing plan");
        }

        try
        {
            var subscription = await billing.SubscribeAsync(request.UserName, request.PlanHandle, cancellationToken);

            var response = new SubscribeResponse(request.CorrelationId())
            {
                Subscription = SubscriptionEndpointHelpers.ToDto(subscription),
            };

            return Results.Created("api/my-subscriptions", response);
        }
        catch (SubscriptionBillingException ex)
        {
            return SubscriptionEndpointHelpers.ToProblem(ex);
        }
    }
}
