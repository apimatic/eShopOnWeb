using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Get available subscription plans
/// </summary>
public class GetSubscriptionPlansEndpoint : IEndpoint<IResult>
{
    private readonly SubscriptionsService _service;

    public GetSubscriptionPlansEndpoint(SubscriptionsService service)
    {
        _service = service;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/subscription-plans",
            async () =>
            {
                return await HandleAsync();
            })
            .Produces<GetSubscriptionPlansResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync()
    {
        var response = new GetSubscriptionPlansResponse(Guid.NewGuid());

        try
        {
            var plans = await _service.GetAvailablePlansAsync();
            foreach (var plan in plans)
            {
                response.Plans.Add(plan);
            }
            return Results.Ok(response);
        }
        catch (Exception ex)
        {
            return Results.Problem(
                detail: ex.Message,
                statusCode: StatusCodes.Status500InternalServerError);
        }
    }
}
