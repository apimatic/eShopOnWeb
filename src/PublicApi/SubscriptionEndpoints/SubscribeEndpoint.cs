using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Billing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Identity;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Subscribes the authenticated caller to a plan. Idempotent: a double-click never creates a
/// second Maxio customer or a duplicate active subscription. The caller's identity always comes
/// from the JWT, never from the request body.
/// </summary>
public class SubscribeEndpoint : IEndpoint<IResult, SubscribeSubscriptionRequest, IMaxioBillingService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (SubscribeSubscriptionRequest request,
             ClaimsPrincipal user,
             UserManager<ApplicationUser> userManager,
             IMaxioBillingService billingService) =>
            {
                var username = user.FindFirstValue(ClaimTypes.Name);
                if (string.IsNullOrWhiteSpace(username))
                {
                    return Results.Unauthorized();
                }

                // Use the (stable across in-memory re-seeds) username as the Maxio customer reference,
                // and the account's email for the customer record.
                var appUser = await userManager.FindByNameAsync(username);
                request.UserReference = username;
                request.Email = appUser?.Email ?? username;

                return await HandleAsync(request, billingService);
            })
            .Produces<SubscribeSubscriptionResponse>(StatusCodes.Status200OK)
            .Produces<SubscribeSubscriptionResponse>(StatusCodes.Status201Created)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(SubscribeSubscriptionRequest request, IMaxioBillingService billingService)
    {
        if (string.IsNullOrWhiteSpace(request.PlanHandle))
        {
            return Results.BadRequest(new { message = "planHandle is required." });
        }

        var result = await billingService.SubscribeAsync(new SubscribeRequest
        {
            UserReference = request.UserReference,
            Email = request.Email,
            PlanHandle = request.PlanHandle
        });

        var response = new SubscribeSubscriptionResponse(request.CorrelationId())
        {
            Subscription = CustomerSubscriptionDto.FromDomain(result.Subscription),
            AlreadySubscribed = !result.Created
        };

        return result.Created
            ? Results.Created("api/my-subscriptions", response)
            : Results.Ok(response);
    }
}
