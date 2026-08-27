using System.Security.Claims;
using System.Threading;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

public sealed class CreateSubscriptionEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions", async (
                CreateSubscriptionRequest request,
                ClaimsPrincipal principal,
                ISubscriptionBillingService billing,
                CancellationToken cancellationToken) =>
            {
                var result = await billing.SubscribeAsync(principal, request.ProductHandle,
                    cancellationToken);
                return result.IsPending
                    ? Results.Accepted(value: new CreateSubscriptionResponse(
                        "reconciling", null, "The enrollment is being reconciled with Maxio."))
                    : Results.Ok(new CreateSubscriptionResponse("completed", result.Subscription));
            })
            .RequireAuthorization(new AuthorizeAttribute
            {
                AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme
            })
            .Produces<CreateSubscriptionResponse>()
            .Produces<CreateSubscriptionResponse>(StatusCodes.Status202Accepted)
            .ProducesValidationProblem()
            .WithTags("SubscriptionEndpoints");
    }
}
