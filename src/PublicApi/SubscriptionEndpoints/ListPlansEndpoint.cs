using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class ListPlansResponse : BaseResponse
{
    public ListPlansResponse(System.Guid correlationId) : base(correlationId) { }
    public ListPlansResponse() { }

    public System.Collections.Generic.List<PlanDto> Plans { get; set; } = new();
}

/// <summary>
/// Lists the plans configured for this integration (UC1 step 1). Anonymous - browsing plans
/// requires no authentication.
/// </summary>
public class ListPlansEndpoint : IEndpoint<IResult, ISubscriptionService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/subscriptions/plans",
            async (ISubscriptionService subscriptionService) =>
            {
                return await HandleAsync(subscriptionService);
            })
            .AllowAnonymous()
            .Produces<ListPlansResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(ISubscriptionService subscriptionService)
    {
        var response = new ListPlansResponse();

        var plans = await subscriptionService.ListPlansAsync();
        response.Plans.AddRange(plans.Select(PlanDto.FromDomain));

        return Results.Ok(response);
    }
}
