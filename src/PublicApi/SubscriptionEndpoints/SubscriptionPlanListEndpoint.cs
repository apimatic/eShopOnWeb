using System.Threading;
using System.Threading.Tasks;
using Ardalis.Result;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using MinimalApi.Endpoint;
using IResult = Microsoft.AspNetCore.Http.IResult;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class SubscriptionPlanListEndpoint : IEndpoint<IResult, SubscriptionPlanListRequest, ISubscriptionService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/subscription-plans",
                async (ISubscriptionService subscriptionService, CancellationToken cancellationToken) =>
                {
                    return await HandleAsync(new SubscriptionPlanListRequest(), subscriptionService, cancellationToken);
                })
            .Produces<SubscriptionPlanListResponse>()
            .WithTags("SubscriptionEndpoints")
            .RequireAuthorization(new AuthorizeAttribute { AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme });
    }

    public Task<IResult> HandleAsync(SubscriptionPlanListRequest request, ISubscriptionService subscriptionService)
        => HandleAsync(request, subscriptionService, CancellationToken.None);

    public async Task<IResult> HandleAsync(SubscriptionPlanListRequest request, ISubscriptionService subscriptionService, CancellationToken cancellationToken = default)
    {
        var response = new SubscriptionPlanListResponse(request.CorrelationId());

        var result = await subscriptionService.GetPlansAsync(cancellationToken);
        if (result.Status == ResultStatus.NotFound)
        {
            return Results.NotFound(new { errors = result.Errors });
        }

        if (result.Status != ResultStatus.Ok)
        {
            return Results.Problem(title: "Failed to load subscription plans.", detail: string.Join("; ", result.Errors), statusCode: 502);
        }

        response.Plans.AddRange(result.Value);
        return Results.Ok(response);
    }
}
