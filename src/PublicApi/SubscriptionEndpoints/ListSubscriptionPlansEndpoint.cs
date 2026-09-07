using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class ListSubscriptionPlansEndpoint : IEndpoint<IResult, EmptyRequest>
{
    private readonly Services.MaxioSubscriptionService _subscriptionService;

    public ListSubscriptionPlansEndpoint(Services.MaxioSubscriptionService subscriptionService)
    {
        _subscriptionService = subscriptionService;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/subscription-plans",
            async (Services.MaxioSubscriptionService service, CancellationToken ct) =>
            {
                return await HandleAsync(new EmptyRequest(), service, ct);
            })
            .Produces<ListSubscriptionPlansResponse>()
            .WithName("ListSubscriptionPlans")
            .WithTags("SubscriptionEndpoints")
            .RequireAuthorization();
    }

    public async Task<IResult> HandleAsync(EmptyRequest request, Services.MaxioSubscriptionService service, CancellationToken ct = default)
    {
        try
        {
            var plans = await service.ListSubscriptionPlansAsync(ct);
            var response = new ListSubscriptionPlansResponse(Guid.NewGuid());
            response.Plans.AddRange(plans);
            return Results.Ok(response);
        }
        catch (Exception ex)
        {
            return Results.BadRequest(new { error = ex.Message });
        }
    }

    Task<IResult> IEndpoint<IResult, EmptyRequest>.HandleAsync(EmptyRequest request) =>
        throw new NotImplementedException();
}

public class EmptyRequest : BaseMessage { }
