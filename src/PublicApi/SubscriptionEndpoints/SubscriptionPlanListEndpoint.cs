using System.Linq;
using System.Threading.Tasks;
using System.Collections.Generic;
using System;
using MinimalApi.Endpoint;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class SubscriptionPlanListEndpoint : IEndpoint<IResult, SubscriptionPlanListRequest, IMaxioBillingService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/subscription-plans",
            async (IMaxioBillingService svc) => await HandleAsync(new SubscriptionPlanListRequest(), svc))
            .Produces<SubscriptionPlanListResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(SubscriptionPlanListRequest request, IMaxioBillingService svc)
    {
        var response = new SubscriptionPlanListResponse();
        try
        {
            var plans = await svc.ListPlansAsync();
            response.Plans = plans.Select(p => new SubscriptionPlanDto(p.Handle, p.Id, p.Name, p.Price)).ToList();
            return Results.Ok(response);
        }
        catch (Exception ex)
        {
            return Results.Problem(detail: ex.Message, statusCode: 500);
        }
    }
}

public class SubscriptionPlanListRequest : BaseRequest
{
}

public class SubscriptionPlanListResponse : BaseResponse
{
    public List<SubscriptionPlanDto> Plans { get; set; } = new();
    public SubscriptionPlanListResponse() { }
}

public record SubscriptionPlanDto(string Handle, int Id, string Name, decimal Price);


