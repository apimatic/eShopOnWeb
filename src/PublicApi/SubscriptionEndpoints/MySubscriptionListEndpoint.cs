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

public class MySubscriptionListEndpoint : IEndpoint<IResult, MySubscriptionListRequest, ISubscriptionService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions",
                async (ISubscriptionService subscriptionService,
                    System.Security.Claims.ClaimsPrincipal user, CancellationToken cancellationToken) =>
                {
                    return await HandleAsync(
                        new MySubscriptionListRequest { AuthenticatedUsername = user.Identity?.Name },
                        subscriptionService, cancellationToken);
                })
            .Produces<MySubscriptionListResponse>()
            .WithTags("SubscriptionEndpoints")
            .RequireAuthorization(new AuthorizeAttribute { AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme });
    }

    public Task<IResult> HandleAsync(MySubscriptionListRequest request, ISubscriptionService subscriptionService)
        => HandleAsync(request, subscriptionService, CancellationToken.None);

    public async Task<IResult> HandleAsync(MySubscriptionListRequest request, ISubscriptionService subscriptionService, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.AuthenticatedUsername))
        {
            return Results.Unauthorized();
        }

        var response = new MySubscriptionListResponse(request.CorrelationId());

        var result = await subscriptionService.GetMySubscriptionsAsync(request.AuthenticatedUsername, cancellationToken);
        if (result.Status != ResultStatus.Ok)
        {
            return Results.Problem(title: "Failed to load subscriptions.",
                detail: string.Join("; ", result.Errors), statusCode: 502);
        }

        response.Subscriptions.AddRange(result.Value);
        return Results.Ok(response);
    }
}
