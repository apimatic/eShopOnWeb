using System.Linq;
using System.Threading.Tasks;
using AutoMapper;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// List the subscription plans available to subscribe to (UC1 step 1). Anonymous, like the
/// catalogue endpoints — browsing plans does not require a login.
/// </summary>
public class ListPlansEndpoint : IEndpoint<IResult, ListPlansRequest, ISubscriptionService>
{
    private readonly IMapper _mapper;

    public ListPlansEndpoint(IMapper mapper)
    {
        _mapper = mapper;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/subscription-plans",
            async (ISubscriptionService subscriptionService) =>
            {
                return await HandleAsync(new ListPlansRequest(), subscriptionService);
            })
            .Produces<ListPlansResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(ListPlansRequest request, ISubscriptionService subscriptionService)
    {
        var response = new ListPlansResponse(request.CorrelationId());

        var plans = await subscriptionService.ListPlansAsync();
        response.Plans.AddRange(plans.Select(_mapper.Map<SubscriptionPlanDto>));

        return Results.Ok(response);
    }
}
