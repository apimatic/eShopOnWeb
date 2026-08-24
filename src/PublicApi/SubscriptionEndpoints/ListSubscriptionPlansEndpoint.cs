using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.PublicApi.Maxio;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Lists the subscription plans (products in the configured Maxio product family) available to shoppers.
/// </summary>
public class ListSubscriptionPlansEndpoint : IEndpoint<IResult, ListSubscriptionPlansRequest>
{
    private readonly SubscriptionService _subscriptionService;

    public ListSubscriptionPlansEndpoint(SubscriptionService subscriptionService)
    {
        _subscriptionService = subscriptionService;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/subscription-plans",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async () =>
                await HandleAsync(new ListSubscriptionPlansRequest()))
            .Produces<ListSubscriptionPlansResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(ListSubscriptionPlansRequest request)
    {
        var response = new ListSubscriptionPlansResponse(request.CorrelationId());
        try
        {
            response.Plans.AddRange(await _subscriptionService.ListPlansAsync());
            return Results.Ok(response);
        }
        catch (MaxioApiException ex)
        {
            return MaxioProblem(ex);
        }
    }

    internal static IResult MaxioProblem(MaxioApiException ex) => Results.Problem(
        title: "The Maxio billing service rejected the request.",
        detail: ex.ResponseBody,
        statusCode: StatusCodes.Status502BadGateway);
}
