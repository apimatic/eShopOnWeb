using System.Net;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.PublicApi.Maxio;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionPlanEndpoints;

/// <summary>
/// Lists the subscription plans available for purchase
/// </summary>
public class ListSubscriptionPlansEndpoint : IEndpoint<IResult, ISubscriptionBillingService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/subscription-plans",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ISubscriptionBillingService billingService) =>
            {
                return await HandleAsync(billingService);
            })
            .Produces<ListSubscriptionPlansResponse>()
            .WithTags("SubscriptionPlanEndpoints");
    }

    public async Task<IResult> HandleAsync(ISubscriptionBillingService billingService)
    {
        try
        {
            var plans = await billingService.GetPlansAsync();
            var response = new ListSubscriptionPlansResponse();
            response.Plans.AddRange(plans);
            return Results.Ok(response);
        }
        catch (MaxioBillingException ex)
        {
            return ToProblem(ex);
        }
    }

    internal static IResult ToProblem(MaxioBillingException ex)
    {
        var statusCode = ex.IsProviderRejection
            ? (int)ex.ProviderStatusCode!.Value
            : (int)HttpStatusCode.BadGateway;
        return Results.Problem(detail: ex.Message, statusCode: statusCode);
    }
}
