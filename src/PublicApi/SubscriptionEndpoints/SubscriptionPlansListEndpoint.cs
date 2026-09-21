using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using MinimalApi.Endpoint;
using Swashbuckle.AspNetCore.Annotations;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Lists the subscription plans available in the configured Maxio product family.
/// </summary>
public class SubscriptionPlansListEndpoint : IEndpoint<IResult, HttpContext>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        // The second (CancellationToken) parameter is what makes this bind to the Delegate overload of
        // MapGet (which returns RouteHandlerBuilder, enabling .Produces) rather than the RequestDelegate
        // overload a lone HttpContext parameter would select.
        app.MapGet("api/subscription-plans",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (HttpContext http, System.Threading.CancellationToken cancellationToken) => await HandleAsync(http))
            .Produces<ListSubscriptionPlansResponse>()
            .ProducesProblem(StatusCodes.Status502BadGateway)
            .WithTags("SubscriptionEndpoints")
            .WithMetadata(new SwaggerOperationAttribute("Lists available subscription plans"));
    }

    public async Task<IResult> HandleAsync(HttpContext http)
    {
        var billingService = http.RequestServices.GetRequiredService<ISubscriptionBillingService>();
        try
        {
            var plans = await billingService.GetPlansAsync(http.RequestAborted);
            var response = new ListSubscriptionPlansResponse();
            response.Plans.AddRange(plans.Select(SubscriptionEndpointHelpers.ToDto));
            return Results.Ok(response);
        }
        catch (SubscriptionBillingException ex)
        {
            return SubscriptionEndpointHelpers.ToProblem(ex);
        }
    }
}

/// <summary>Response for <c>GET /api/subscription-plans</c>.</summary>
public class ListSubscriptionPlansResponse : BaseResponse
{
    public System.Collections.Generic.List<SubscriptionPlanDto> Plans { get; set; } = new();
}
