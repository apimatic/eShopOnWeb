using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class ListSubscriptionPlansEndpoint : IEndpoint<IResult, EmptyRequest, IMaxioSubscriptionService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/subscription-plans", HandleAsync)
            .RequireAuthorization()
            .Produces<ListSubscriptionPlansResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public Task<IResult> HandleAsync(EmptyRequest _, IMaxioSubscriptionService service)
    {
        return HandleInternalAsync(service);
    }

    private static async Task<IResult> HandleInternalAsync(IMaxioSubscriptionService service)
    {
        var plans = await service.GetPlansAsync();
        var response = new ListSubscriptionPlansResponse
        {
            Plans = plans
        };
        return Results.Ok(response);
    }
}

public class ListSubscriptionPlansResponse : BaseResponse
{
    public SubscriptionPlanDto[] Plans { get; set; } = [];
}

public class EmptyRequest : BaseRequest
{
}
