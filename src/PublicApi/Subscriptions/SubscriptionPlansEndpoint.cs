using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Options;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

public sealed class SubscriptionPlansEndpoint : IEndpoint<IResult, SubscriptionBillingService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/subscription-plans",
                async (SubscriptionBillingService service, IOptions<MaxioOptions> options, CancellationToken cancellationToken) =>
                    Results.Ok(new SubscriptionPlansResponse
                    {
                        Plans = await service.ListPlansAsync(options.Value, cancellationToken)
                    }))
            .Produces<SubscriptionPlansResponse>()
            .RequireAuthorization(new AuthorizeAttribute { AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme })
            .WithTags("SubscriptionEndpoints");
    }

    public Task<IResult> HandleAsync(SubscriptionBillingService service) =>
        Task.FromResult<IResult>(Results.StatusCode(StatusCodes.Status501NotImplemented));
}
