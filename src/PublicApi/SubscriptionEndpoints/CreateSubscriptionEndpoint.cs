using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.PublicApi.Billing;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Subscribes the authenticated user to a plan. Idempotent: repeating the same request
/// returns the existing subscription instead of creating a duplicate.
/// </summary>
public class CreateSubscriptionEndpoint : IEndpoint<IResult, CreateSubscriptionRequest, ClaimsPrincipal, IMaxioBillingService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreateSubscriptionRequest request, ClaimsPrincipal user, IMaxioBillingService billingService) =>
            {
                return await HandleAsync(request, user, billingService);
            })
           .Produces<CreateSubscriptionResponse>()
           .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(CreateSubscriptionRequest request, ClaimsPrincipal user, IMaxioBillingService billingService)
    {
        var username = user.FindFirst(ClaimTypes.Name)?.Value ?? user.Identity?.Name;
        if (string.IsNullOrEmpty(username))
        {
            return Results.Unauthorized();
        }

        if (string.IsNullOrWhiteSpace(request.ProductHandle))
        {
            return Results.BadRequest("ProductHandle is required.");
        }

        var subscription = await billingService.SubscribeAsync(username, request.ProductHandle);

        var response = new CreateSubscriptionResponse(request.CorrelationId())
        {
            Subscription = subscription
        };

        return Results.Ok(response);
    }
}
