using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;
using Swashbuckle.AspNetCore.Annotations;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Lists the recurring subscription plans a shopper can subscribe to.
/// </summary>
public class SubscriptionPlanListEndpoint : IEndpoint<IResult, ISubscriptionBillingService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/subscription-plans",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ISubscriptionBillingService billingService, CancellationToken cancellationToken) =>
            {
                return await HandleAsync(billingService, cancellationToken);
            })
            .Produces<ListSubscriptionPlansResponse>()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable)
            .WithTags("SubscriptionEndpoints")
            .WithMetadata(new SwaggerOperationAttribute(
                summary: "Lists the available subscription plans",
                description: "Lists the recurring plans offered by the configured billing product family."));
    }

    public Task<IResult> HandleAsync(ISubscriptionBillingService billingService) =>
        HandleAsync(billingService, CancellationToken.None);

    public async Task<IResult> HandleAsync(
        ISubscriptionBillingService billingService,
        CancellationToken cancellationToken)
    {
        var response = new ListSubscriptionPlansResponse();

        var plans = await billingService.ListPlansAsync(cancellationToken);
        response.SubscriptionPlans.AddRange(plans.Select(SubscriptionMappings.ToDto));

        return Results.Ok(response);
    }
}
