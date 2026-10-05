using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Lists the subscription plans this application offers (the products of the configured Maxio
/// product family).
/// </summary>
public class SubscriptionPlanListEndpoint : IEndpoint<IResult, SubscriptionPlanListRequest, IMaxioBillingService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/subscription-plans",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (IMaxioBillingService billingService) =>
            {
                return await HandleAsync(new SubscriptionPlanListRequest(), billingService);
            })
            .Produces<SubscriptionPlanListResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(SubscriptionPlanListRequest request, IMaxioBillingService billingService)
    {
        try
        {
            var catalog = await billingService.GetPlansAsync();

            var response = new SubscriptionPlanListResponse(request.CorrelationId())
            {
                Truncated = catalog.Truncated
            };
            response.Plans.AddRange(catalog.Plans.Select(SubscriptionDtos.FromPlan));

            return Results.Ok(response);
        }
        catch (ApplicationCore.Exceptions.MaxioBillingException ex)
        {
            return MaxioBillingResults.Problem(ex);
        }
    }
}