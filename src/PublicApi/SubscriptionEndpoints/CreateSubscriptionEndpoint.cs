using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public sealed class CreateSubscriptionEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async (
                CreateSubscriptionRequest request,
                ClaimsPrincipal principal,
                ISubscriptionBillingService billingService,
                CancellationToken cancellationToken) =>
            {
                var user = SubscriptionUserFactory.Create(principal);
                var subscription = await billingService.SubscribeAsync(
                    user,
                    request.ProductHandle,
                    cancellationToken);
                return Results.Ok(SubscriptionDto.From(subscription));
            })
            .Accepts<CreateSubscriptionRequest>("application/json")
            .Produces<SubscriptionDto>()
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status422UnprocessableEntity)
            .Produces(StatusCodes.Status502BadGateway)
            .WithTags("SubscriptionEndpoints");
    }
}
