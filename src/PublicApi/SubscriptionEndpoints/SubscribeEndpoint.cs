using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.PublicApi.Maxio;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Idempotently subscribes the authenticated shopper to a plan. Repeating the same request never
/// creates a second customer or a second subscription: the shopper's existing subscription is returned
/// instead.
/// </summary>
public class SubscribeEndpoint : IEndpoint<IResult, SubscribeRequest, ClaimsPrincipal, MaxioBillingService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (SubscribeRequest request, ClaimsPrincipal user, MaxioBillingService billingService) =>
            {
                return await HandleAsync(request, user, billingService);
            })
            .Produces<SubscribeResponse>()
            .Produces<SubscribeResponse>(StatusCodes.Status200OK)
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(SubscribeRequest request, ClaimsPrincipal user, MaxioBillingService billingService)
    {
        var email = user.FindFirstValue(ClaimTypes.Name);
        if (string.IsNullOrWhiteSpace(email))
        {
            return Results.Unauthorized();
        }

        var enrollment = await billingService.SubscribeAsync(email, request.PlanHandle, request.FirstName, request.LastName);
        var response = new SubscribeResponse(request.CorrelationId())
        {
            Subscription = enrollment.Subscription
        };

        return enrollment.Created
            ? Results.Created("api/my-subscriptions", response)
            : Results.Ok(response);
    }
}
