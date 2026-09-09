using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Lists the subscription plans available to shoppers.
/// </summary>
public class GetSubscriptionPlansEndpoint : IEndpoint<IResult, GetSubscriptionPlansRequest, Maxio.IMaxioSubscriptionService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/subscription-plans",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (Maxio.IMaxioSubscriptionService subscriptionService, CancellationToken ct) =>
            {
                return await GetPlansAsync(subscriptionService, ct);
            })
            .Produces<GetSubscriptionPlansResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public Task<IResult> HandleAsync(GetSubscriptionPlansRequest request, Maxio.IMaxioSubscriptionService subscriptionService) =>
        GetPlansAsync(subscriptionService, CancellationToken.None);

    private async Task<IResult> GetPlansAsync(Maxio.IMaxioSubscriptionService subscriptionService, CancellationToken ct)
    {
        var request = new GetSubscriptionPlansRequest();
        var response = new GetSubscriptionPlansResponse(request.CorrelationId());
        try
        {
            var plans = await subscriptionService.GetPlansAsync(ct);
            response.Plans = plans.Select(p => p.ToDto()).ToList();
            return Results.Ok(response);
        }
        catch (Maxio.MaxioBillingException ex)
        {
            return SubscriptionEndpointErrors.ToResult(ex);
        }
    }
}
