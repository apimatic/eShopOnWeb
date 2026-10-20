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
/// List the recurring plans a shopper can subscribe to
/// </summary>
public class SubscriptionPlanListEndpoint : IEndpoint<IResult, HttpContext, ISubscriptionBillingService>
{
    private readonly IMapper _mapper;

    public SubscriptionPlanListEndpoint(IMapper mapper)
    {
        _mapper = mapper;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/subscription-plans",
            async (HttpContext httpContext, ISubscriptionBillingService billingService) =>
            {
                return await HandleAsync(httpContext, billingService);
            })
           .Produces<ListSubscriptionPlansResponse>()
           .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(HttpContext httpContext, ISubscriptionBillingService billingService)
    {
        var response = new ListSubscriptionPlansResponse();

        var catalog = await billingService.ListPlansAsync(httpContext.RequestAborted);

        response.Plans.AddRange(catalog.Plans.Select(_mapper.Map<SubscriptionPlanDto>));
        response.Truncated = catalog.Truncated;

        return Results.Ok(response);
    }
}
