using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Subscribes the caller to a plan. Ensures a Maxio customer exists for the caller (idempotent) and
/// enrolls them (idempotent per plan): calling this twice for the same plan returns the same subscription.
/// </summary>
public class CreateSubscriptionEndpoint : IEndpoint<IResult, CreateSubscriptionRequest, ISubscriptionBillingService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async ([FromBody] CreateSubscriptionRequest request, ClaimsPrincipal user, ISubscriptionBillingService billingService) =>
            {
                request.Username = user.Identity?.Name ?? string.Empty;
                return await HandleAsync(request, billingService);
            })
            .Produces<CreateSubscriptionResponse>()
            .Produces(StatusCodes.Status400BadRequest)
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(CreateSubscriptionRequest request, ISubscriptionBillingService billingService)
    {
        if (string.IsNullOrWhiteSpace(request.Username))
        {
            return Results.Unauthorized();
        }

        if (string.IsNullOrWhiteSpace(request.PlanHandle))
        {
            return Results.BadRequest("PlanHandle is required.");
        }

        var response = new CreateSubscriptionResponse(request.CorrelationId());

        var subscription = await billingService.SubscribeAsync(request.Username, request.Username, request.PlanHandle);
        response.Subscription = subscription.ToDto();

        return Results.Ok(response);
    }
}
