using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints.Maxio;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Lists the subscribable plans (products in the configured Maxio product family).
/// </summary>
public class SubscriptionPlansListEndpoint : IEndpoint<IResult, IMaxioSubscriptionService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/subscription-plans",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (IMaxioSubscriptionService service, CancellationToken ct) =>
            {
                return await HandleAsync(service, ct);
            })
            .Produces<SubscriptionPlansResponse>()
            .ProducesProblem(StatusCodes.Status502BadGateway)
            .WithTags("SubscriptionEndpoints");
    }

    public Task<IResult> HandleAsync(IMaxioSubscriptionService service) => HandleAsync(service, CancellationToken.None);

    public async Task<IResult> HandleAsync(IMaxioSubscriptionService service, CancellationToken ct)
    {
        var plans = await service.GetPlansAsync(ct);
        var response = new SubscriptionPlansResponse
        {
            Plans = plans.Select(SubscriptionPlanDto.From).ToList()
        };
        return Results.Ok(response);
    }
}
