using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public sealed class SubscriptionPlansEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/subscription-plans",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ISubscriptionBillingService service, CancellationToken cancellationToken) =>
                await HandleAsync(service, cancellationToken))
            .Produces<SubscriptionPlansResponse>()
            .ProducesProblem(StatusCodes.Status502BadGateway)
            .WithTags("SubscriptionEndpoints");
    }

    public static async Task<IResult> HandleAsync(ISubscriptionBillingService service,
        CancellationToken cancellationToken)
    {
        try
        {
            return Results.Ok(new SubscriptionPlansResponse
            {
                Plans = await service.GetPlansAsync(cancellationToken)
            });
        }
        catch (Exception exception) when (exception is MaxioApiException)
        {
            return SubscriptionEndpointSupport.Error(exception);
        }
    }
}
