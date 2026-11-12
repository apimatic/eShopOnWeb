using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.PublicApi.Maxio;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Lists the subscription plans offered in the configured Maxio product family
/// </summary>
public class SubscriptionPlanListEndpoint : IEndpoint<IResult, ListSubscriptionPlansRequest, IMaxioBillingService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/subscription-plans",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (IMaxioBillingService billing, CancellationToken ct) =>
            {
                return await HandleAsync(new ListSubscriptionPlansRequest(), billing, ct);
            })
            .Produces<ListSubscriptionPlansResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public Task<IResult> HandleAsync(ListSubscriptionPlansRequest request, IMaxioBillingService billing) =>
        HandleAsync(request, billing, CancellationToken.None);

    public async Task<IResult> HandleAsync(ListSubscriptionPlansRequest request, IMaxioBillingService billing,
        CancellationToken ct)
    {
        try
        {
            var result = await billing.ListPlansAsync(ct);
            return Results.Ok(new ListSubscriptionPlansResponse(request.CorrelationId())
            {
                Plans = new(result.Plans),
                IsComplete = result.IsComplete
            });
        }
        catch (BillingException ex)
        {
            return BillingResults.From(ex, request.CorrelationId());
        }
    }
}
