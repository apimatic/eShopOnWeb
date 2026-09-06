using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.PublicApi.Services;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class GetSubscriptionPlansEndpoint : IEndpoint<IResult, EmptyRequest>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/subscription-plans",
            Handler)
            .WithTags("SubscriptionEndpoints")
            .WithName("GetSubscriptionPlans")
            .Produces<SubscriptionPlansResponse>();
    }

    public Task<IResult> HandleAsync(EmptyRequest request)
    {
        throw new NotImplementedException();
    }

    private async Task<IResult> Handler(IMaxioSubscriptionService service)
    {
        var response = new SubscriptionPlansResponse();

        try
        {
            var plans = await service.GetSubscriptionPlansAsync();
            response.Plans.AddRange(plans);
            return Results.Ok(response);
        }
        catch
        {
            return Results.StatusCode(StatusCodes.Status500InternalServerError);
        }
    }
}

public class EmptyRequest : BaseRequest
{
}
