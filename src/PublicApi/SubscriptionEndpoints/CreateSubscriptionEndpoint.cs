using System.Security.Claims;
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
/// Subscribes the current user to a plan. Idempotent: ensures a single Maxio customer for the
/// user and a single live subscription per plan, so a double-click never double-enrolls.
/// </summary>
public class CreateSubscriptionEndpoint : IEndpoint<IResult, CreateSubscriptionRequest, ISubscriptionBillingService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreateSubscriptionRequest request, ClaimsPrincipal user, ISubscriptionBillingService billingService) =>
            {
                var userReference = user.Identity?.Name;
                if (string.IsNullOrWhiteSpace(userReference))
                    return Results.Unauthorized();

                // Identity comes from the token only — reference and email are the eShopOnWeb user name.
                request.SetIdentity(userReference, userReference);
                return await HandleAsync(request, billingService);
            })
            .Produces<CreateSubscriptionResponse>(StatusCodes.Status201Created)
            .Produces<CreateSubscriptionResponse>(StatusCodes.Status200OK)
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(CreateSubscriptionRequest request, ISubscriptionBillingService billingService)
    {
        var response = new CreateSubscriptionResponse(request.CorrelationId());

        if (string.IsNullOrWhiteSpace(request.PlanHandle))
            return Results.BadRequest("A plan handle is required to subscribe.");

        var result = await billingService.SubscribeAsync(new SubscribeRequest
        {
            UserReference = request.UserReference,
            Email = request.Email,
            FirstName = request.FirstName,
            LastName = request.LastName,
            ProductHandle = request.PlanHandle
        });

        response.Subscription = result.Subscription.ToDto();
        response.AlreadyExisted = result.AlreadyExisted;

        // 200 for an idempotent hit (already subscribed), 201 when a new subscription was created.
        return result.AlreadyExisted
            ? Results.Ok(response)
            : Results.Created("api/my-subscriptions", response);
    }
}
