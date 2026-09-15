using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

public sealed class CreateSubscriptionEndpoint : IEndpoint<IResult, CreateSubscriptionRequest, MaxioBillingService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
                [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
                async (CreateSubscriptionRequest request, HttpContext context, MaxioBillingService service,
                    CancellationToken cancellationToken) =>
                    Results.Ok(await service.SubscribeAsync(context.User, request.ProductHandle, cancellationToken)))
            .Produces<SubscribeResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(CreateSubscriptionRequest request, MaxioBillingService service)
    {
        return Results.Ok(await service.SubscribeAsync(new System.Security.Claims.ClaimsPrincipal(), request.ProductHandle,
            CancellationToken.None));
    }
}
