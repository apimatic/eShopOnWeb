using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.PublicApi.Billing;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// List the subscription plans a shopper can subscribe to
/// </summary>
public class SubscriptionPlanListEndpoint : IEndpoint<IResult, SubscriptionBillingService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/subscription-plans",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (SubscriptionBillingService billing, CancellationToken cancellationToken) =>
            {
                return await HandleAsync(billing, cancellationToken);
            })
            .Produces<ListSubscriptionPlansResponse>()
            .ProducesProblem(StatusCodes.Status502BadGateway)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable)
            .ProducesProblem(StatusCodes.Status504GatewayTimeout)
            .WithTags(BillingEndpointSupport.Tag);
    }

    public Task<IResult> HandleAsync(SubscriptionBillingService billing) => HandleAsync(billing, CancellationToken.None);

    public async Task<IResult> HandleAsync(SubscriptionBillingService billing, CancellationToken cancellationToken)
    {
        var response = new ListSubscriptionPlansResponse();
        try
        {
            var catalog = await billing.GetPlansAsync(cancellationToken);
            response.Plans.AddRange(catalog.Plans);
            response.IsPartial = catalog.IsPartial;
            return Results.Ok(response);
        }
        catch (BillingException ex)
        {
            return BillingEndpointSupport.Problem(ex, response.CorrelationId());
        }
    }
}
