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
using Swashbuckle.AspNetCore.Annotations;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Lists the recurring plans a shopper can subscribe to.
/// </summary>
public class SubscriptionPlansListEndpoint : IEndpoint<IResult, ListSubscriptionPlansRequest, ISubscriptionPlanService>
{
    private readonly IMapper _mapper;

    public SubscriptionPlansListEndpoint(IMapper mapper)
    {
        _mapper = mapper;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/subscription-plans",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ISubscriptionPlanService planService, CancellationToken cancellationToken) =>
            {
                return await HandleAsync(new ListSubscriptionPlansRequest(cancellationToken), planService);
            })
            .Produces<ListSubscriptionPlansResponse>()
            .WithTags("SubscriptionEndpoints")
            .WithMetadata(new SwaggerOperationAttribute(
                summary: "Lists subscription plans",
                description: "Returns the recurring plans on offer, cheapest first. Subscribe with the plan handle.")
            {
                OperationId = "subscriptions.listPlans"
            });
    }

    public async Task<IResult> HandleAsync(ListSubscriptionPlansRequest request, ISubscriptionPlanService planService)
    {
        var response = new ListSubscriptionPlansResponse(request.CorrelationId());

        var plans = await planService.ListPlansAsync(request.CancellationToken);
        response.Plans.AddRange(plans.Select(_mapper.Map<SubscriptionPlanDto>));

        return Results.Ok(response);
    }
}
