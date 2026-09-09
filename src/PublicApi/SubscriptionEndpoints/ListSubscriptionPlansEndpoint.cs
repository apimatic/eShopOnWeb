using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Lists the available subscription plans (the products in the configured Maxio product family).
/// </summary>
public class ListSubscriptionPlansEndpoint : IEndpoint<IResult>
{
    private readonly ISubscriptionBillingService _billing;

    public ListSubscriptionPlansEndpoint(ISubscriptionBillingService billing)
    {
        _billing = billing;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/subscription-plans",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (CancellationToken ct) => await HandleAsync(ct))
            .Produces<ListSubscriptionPlansResponse>()
            .ProducesProblem(StatusCodes.Status502BadGateway)
            .WithTags("SubscriptionEndpoints");
    }

    public Task<IResult> HandleAsync() => HandleAsync(CancellationToken.None);

    public async Task<IResult> HandleAsync(CancellationToken cancellationToken)
    {
        try
        {
            var plans = await _billing.GetPlansAsync(cancellationToken);
            return Results.Ok(new ListSubscriptionPlansResponse { Plans = new(plans) });
        }
        catch (SubscriptionBillingException ex)
        {
            return Results.Problem(detail: ex.Message, statusCode: ex.SuggestedStatusCode);
        }
    }
}
