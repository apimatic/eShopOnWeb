using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class ListSubscriptionPlansEndpoint : IEndpoint<IResult, ListSubscriptionPlansRequest>
{
    private readonly MaxioSubscriptionService _subscriptionService;

    public ListSubscriptionPlansEndpoint(MaxioSubscriptionService subscriptionService)
    {
        _subscriptionService = subscriptionService;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/subscription-plans",
            async (CancellationToken ct) =>
            {
                return await HandleAsyncInternal(new ListSubscriptionPlansRequest(), ct);
            })
            .Produces<ListSubscriptionPlansResponse>()
            .WithTags("SubscriptionEndpoints")
            .WithName("GetSubscriptionPlans");
    }

    public async Task<IResult> HandleAsync(ListSubscriptionPlansRequest request)
    {
        throw new NotImplementedException();
    }

    private async Task<IResult> HandleAsyncInternal(ListSubscriptionPlansRequest request, CancellationToken ct)
    {
        var response = new ListSubscriptionPlansResponse(request.CorrelationId());

        try
        {
            var plans = await _subscriptionService.GetAvailablePlans(ct);
            response.Plans = plans;
            return Results.Ok(response);
        }
        catch (InvalidOperationException ex)
        {
            response.ErrorMessage = ex.Message;
            return Results.BadRequest(response);
        }
        catch (Exception)
        {
            response.ErrorMessage = "An unexpected error occurred while retrieving subscription plans";
            return Results.StatusCode(StatusCodes.Status500InternalServerError);
        }
    }
}
