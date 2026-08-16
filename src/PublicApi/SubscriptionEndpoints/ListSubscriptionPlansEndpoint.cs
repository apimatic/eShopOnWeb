using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;
using Swashbuckle.AspNetCore.Annotations;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Lists the subscription plans available for enrollment (the products of the configured Maxio
/// product family).
/// </summary>
public class ListSubscriptionPlansEndpoint : IEndpoint<IResult, ISubscriptionBillingService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/subscription-plans",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (ISubscriptionBillingService billing) => await HandleAsync(billing))
            .Produces<ListSubscriptionPlansResponse>()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status502BadGateway)
            .WithTags("SubscriptionEndpoints")
            .WithMetadata(new SwaggerOperationAttribute(
                summary: "Lists subscription plans",
                description: "Lists the recurring subscription plans a shopper can enroll in."));
    }

    public async Task<IResult> HandleAsync(ISubscriptionBillingService billing)
    {
        var response = new ListSubscriptionPlansResponse();
        try
        {
            var plans = await billing.GetPlansAsync();
            response.Plans.AddRange(plans.Select(p => p.ToDto()));
            return Results.Ok(response);
        }
        catch (BillingServiceException ex)
        {
            return Results.Problem(detail: ex.Message, statusCode: StatusCodes.Status502BadGateway,
                title: "Billing system unavailable");
        }
    }
}
