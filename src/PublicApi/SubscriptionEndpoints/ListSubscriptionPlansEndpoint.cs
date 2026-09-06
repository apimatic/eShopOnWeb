using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AutoMapper;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Lists the recurring plans a shopper can subscribe to, straight from the billing system of record.
/// </summary>
public class ListSubscriptionPlansEndpoint : IEndpoint<IResult, ListSubscriptionPlansRequest, ISubscriptionService>
{
    private readonly IMapper _mapper;

    public ListSubscriptionPlansEndpoint(IMapper mapper)
    {
        _mapper = mapper;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/subscription-plans",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ISubscriptionService subscriptionService, CancellationToken cancellationToken) =>
            {
                return await HandleAsync(new ListSubscriptionPlansRequest(cancellationToken), subscriptionService);
            })
            .Produces<ListSubscriptionPlansResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(ListSubscriptionPlansRequest request, ISubscriptionService subscriptionService)
    {
        var response = new ListSubscriptionPlansResponse(request.CorrelationId());

        var plans = await subscriptionService.ListPlansAsync(request.CancellationToken);
        response.Plans.AddRange(plans.Select(_mapper.Map<SubscriptionPlanDto>));

        return Results.Ok(response);
    }
}
