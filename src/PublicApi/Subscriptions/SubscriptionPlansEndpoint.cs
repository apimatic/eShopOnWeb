using System;
using System.Threading.Tasks;
using System.Threading;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

public sealed class SubscriptionPlansEndpoint : IEndpoint<IResult>
{
    public Task<IResult> HandleAsync() => Task.FromResult(Results.NotFound());

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/subscription-plans", async (IMaxioSubscriptionService subscriptions, CancellationToken cancellationToken) =>
        {
            try
            {
                var plans = await subscriptions.ListPlansAsync(cancellationToken);
                return Results.Ok(new SubscriptionPlansResponse { Plans = plans });
            }
            catch (Exception exception)
            {
                return SubscriptionEndpointResults.From(exception);
            }
        })
        .RequireAuthorization(new AuthorizeAttribute { AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme })
        .Produces<SubscriptionPlansResponse>()
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status502BadGateway)
        .WithTags("Subscriptions");
    }
}
