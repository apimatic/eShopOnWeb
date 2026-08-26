using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Subscribes the authenticated user to a plan. Idempotent: repeating the call for a plan
/// the user is already subscribed to returns the existing subscription.
/// </summary>
public class CreateSubscriptionEndpoint : IEndpoint<IResult, CreateSubscriptionRequest, ClaimsPrincipal, ISubscriptionBillingService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreateSubscriptionRequest request, ClaimsPrincipal claimsPrincipal, ISubscriptionBillingService billingService) =>
            {
                return await HandleAsync(request, claimsPrincipal, billingService);
            })
            .Produces<CreateSubscriptionResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(CreateSubscriptionRequest request, ClaimsPrincipal claimsPrincipal, ISubscriptionBillingService billingService)
    {
        var userId = SubscriptionEndpointHelpers.GetUserId(claimsPrincipal);
        if (string.IsNullOrEmpty(userId))
        {
            return Results.Unauthorized();
        }

        if (string.IsNullOrWhiteSpace(request.ProductHandle))
        {
            return Results.BadRequest(new CreateSubscriptionResponse(request.CorrelationId()));
        }

        try
        {
            // The eShopOnWeb username is the user's email address.
            var subscription = await billingService.SubscribeAsync(userId, userId, request.ProductHandle);

            var response = new CreateSubscriptionResponse(request.CorrelationId())
            {
                Subscription = SubscriptionEndpointHelpers.ToDto(subscription)
            };
            return Results.Created("api/my-subscriptions", response);
        }
        catch (BillingException ex)
        {
            return SubscriptionEndpointHelpers.ToProblem(ex);
        }
    }
}
