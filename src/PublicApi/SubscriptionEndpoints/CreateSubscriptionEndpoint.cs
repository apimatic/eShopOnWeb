using System.Collections.Generic;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Billing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public sealed class CreateSubscriptionEndpoint : IEndpoint<IResult, CreateSubscriptionRequest, ClaimsPrincipal, ISubscriptionBillingService>
{
    public async Task<IResult> HandleAsync(
        CreateSubscriptionRequest request,
        ClaimsPrincipal user,
        ISubscriptionBillingService billing)
    {
        if (request.ProductId <= 0)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                [nameof(request.ProductId)] = ["A positive productId is required."]
            });
        }

        var identity = BillingIdentityFactory.Create(user);
        return Results.Ok(await billing.SubscribeAsync(identity, request.ProductId, default));
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
                [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
                (CreateSubscriptionRequest request,
                    ClaimsPrincipal user,
                    ISubscriptionBillingService billing,
                    CancellationToken cancellationToken) =>
                {
                    if (request.ProductId <= 0)
                    {
                        return Results.ValidationProblem(new Dictionary<string, string[]>
                        {
                            [nameof(request.ProductId)] = ["A positive productId is required."]
                        });
                    }

                    var identity = BillingIdentityFactory.Create(user);
                    var subscription = await billing.SubscribeAsync(identity, request.ProductId, cancellationToken);
                    return Results.Ok(subscription);
                })
            .Produces<SubscriptionDetails>()
            .ProducesValidationProblem()
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status409Conflict)
            .WithTags("SubscriptionEndpoints");
    }
}
