using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using System.Security.Claims;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Subscribes the authenticated shopper to a plan (POST /api/subscriptions). Idempotent: ensures a Maxio
/// customer exists for the eShop user and enrolls them, so a double-click never creates two customers or
/// two subscriptions. Returns 201 when a new subscription was created, 200 when one already existed.
/// </summary>
public class CreateSubscriptionEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (CreateSubscriptionRequest? request, ClaimsPrincipal user,
                   ISubscriptionBillingService billing, CancellationToken cancellationToken) =>
            {
                var reference = SubscriptionMappings.GetUserReference(user);
                if (string.IsNullOrWhiteSpace(reference))
                    return Results.Unauthorized();

                var billingUser = SubscriptionMappings.ToBillingUser(reference);
                var result = await billing.SubscribeAsync(billingUser, request?.PlanHandle, cancellationToken);

                var response = new CreateSubscriptionResponse(request?.CorrelationId() ?? System.Guid.NewGuid())
                {
                    Subscription = SubscriptionMappings.ToDto(result.Subscription),
                    AlreadyExisted = result.AlreadyExisted,
                };

                return result.AlreadyExisted
                    ? Results.Ok(response)
                    : Results.Created("api/my-subscriptions", response);
            })
            .Produces<CreateSubscriptionResponse>()
            .Produces<CreateSubscriptionResponse>(StatusCodes.Status201Created)
            .WithTags("SubscriptionEndpoints");
    }
}
