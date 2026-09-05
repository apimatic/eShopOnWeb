using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Subscribes the calling (JWT-authenticated) user to a plan. Ensures a Maxio customer exists
/// for the user and enrolls them - idempotently, so a double-click never creates two customers
/// or subscriptions.
/// </summary>
public class SubscribeEndpoint : IEndpoint<IResult, SubscribeRequest, ISubscriptionBillingService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (SubscribeRequest request, ClaimsPrincipal user, ISubscriptionBillingService billingService) =>
            {
                var buyerReference = user.FindFirst(ClaimTypes.NameIdentifier)?.Value;
                if (string.IsNullOrEmpty(buyerReference))
                {
                    return Results.Unauthorized();
                }

                request.BuyerReference = buyerReference;
                request.Email = user.FindFirst(ClaimTypes.Email)?.Value
                    ?? user.FindFirst(ClaimTypes.Name)?.Value
                    ?? buyerReference;

                return await HandleAsync(request, billingService);
            })
            .Produces<SubscribeResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(SubscribeRequest request, ISubscriptionBillingService billingService)
    {
        if (string.IsNullOrWhiteSpace(request.PlanHandle))
        {
            return Results.BadRequest("planHandle is required.");
        }

        var response = new SubscribeResponse(request.CorrelationId());

        var result = await billingService.SubscribeAsync(request.BuyerReference, request.Email, request.PlanHandle);
        response.Subscription = UserSubscriptionDto.FromMaxio(result.Subscription);
        response.AlreadySubscribed = result.AlreadyExisted;

        return result.AlreadyExisted
            ? Results.Ok(response)
            : Results.Created($"api/my-subscriptions", response);
    }
}
