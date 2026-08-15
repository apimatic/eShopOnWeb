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
/// Subscribes the authenticated shopper to a plan. Ensures a Maxio customer exists for the user
/// (idempotent by user reference) and enrolls them; a double-click never creates a duplicate.
/// </summary>
public class CreateSubscriptionEndpoint : IEndpoint<IResult, CreateSubscriptionRequest, IMaxioBillingService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreateSubscriptionRequest request, ClaimsPrincipal user, IMaxioBillingService billing, CancellationToken cancellationToken) =>
            {
                // Identity comes from the token, not the request body.
                request.Username = user.Identity?.Name ?? string.Empty;
                return await HandleAsync(request, billing, cancellationToken);
            })
            .Produces<CreateSubscriptionResponse>()
            .Produces(StatusCodes.Status401Unauthorized)
            .WithTags("SubscriptionEndpoints");
    }

    // Satisfies IEndpoint; the route handler calls the cancellation-aware overload below.
    public Task<IResult> HandleAsync(CreateSubscriptionRequest request, IMaxioBillingService billing)
        => HandleAsync(request, billing, CancellationToken.None);

    public async Task<IResult> HandleAsync(CreateSubscriptionRequest request, IMaxioBillingService billing, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Username))
            return Results.Unauthorized();

        var response = new CreateSubscriptionResponse(request.CorrelationId());

        var result = await billing.SubscribeAsync(new SubscribeRequest
        {
            UserReference = request.Username,
            Email = request.Username,
            PlanHandle = request.PlanHandle
        }, cancellationToken);

        response.Subscription = result.Subscription.ToDto();
        response.AlreadyExisted = result.AlreadyExisted;

        return Results.Ok(response);
    }
}
