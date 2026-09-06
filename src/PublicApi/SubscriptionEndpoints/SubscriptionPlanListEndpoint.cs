using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using MinimalApi.Endpoint;
using Swashbuckle.AspNetCore.Annotations;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class SubscriptionPlanListEndpoint : IEndpoint<IResult, SubscriptionPlanListRequest, IMaxioSubscriptionService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/subscription-plans",
            async (IMaxioSubscriptionService service, CancellationToken ct) =>
            {
                return await HandleAsync(new SubscriptionPlanListRequest(System.Guid.NewGuid()), service);
            })
            .Produces<SubscriptionPlanListResponse>()
            .WithName("GetSubscriptionPlans")
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(SubscriptionPlanListRequest request, IMaxioSubscriptionService service)
    {
        var response = new SubscriptionPlanListResponse(request.CorrelationId());

        try
        {
            var plans = await service.GetSubscriptionPlansAsync(CancellationToken.None);
            foreach (var plan in plans)
            {
                response.Plans.Add(plan);
            }
            return Results.Ok(response);
        }
        catch (Exception ex)
        {
            return Results.BadRequest(new { error = ex.Message });
        }
    }
}

public class SubscriptionPlanListRequest : BaseRequest
{
    public SubscriptionPlanListRequest(Guid correlationId)
    {
        _correlationId = correlationId;
    }
}

public class SubscriptionPlanListResponse : BaseResponse
{
    public SubscriptionPlanListResponse(Guid correlationId) : base(correlationId)
    {
    }

    public IList<SubscriptionPlanDto> Plans { get; } = new List<SubscriptionPlanDto>();
}
