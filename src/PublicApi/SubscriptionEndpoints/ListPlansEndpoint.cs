using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AutoMapper;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Lists the subscription plans customers can enroll in
/// </summary>
public class ListPlansEndpoint : IEndpoint<IResult, ISubscriptionService>
{
    private readonly IMapper _mapper;

    public ListPlansEndpoint(IMapper mapper)
    {
        _mapper = mapper;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/subscription-plans",
            async (ISubscriptionService subscriptionService, CancellationToken cancellationToken) =>
            {
                return await HandleAsync(subscriptionService, cancellationToken);
            })
            .Produces<ListPlansResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public Task<IResult> HandleAsync(ISubscriptionService subscriptionService)
    {
        return HandleAsync(subscriptionService, CancellationToken.None);
    }

    public async Task<IResult> HandleAsync(ISubscriptionService subscriptionService, CancellationToken cancellationToken)
    {
        var response = new ListPlansResponse();

        var plans = await subscriptionService.GetPlansAsync(cancellationToken);
        response.Plans.AddRange(plans.Select(_mapper.Map<SubscriptionPlanDto>));

        return Results.Ok(response);
    }
}
