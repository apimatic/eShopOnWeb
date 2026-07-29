using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Lists the authenticated caller's subscriptions, sourced from Maxio.
/// </summary>
public class MySubscriptionsListEndpoint : IEndpoint<IResult, MySubscriptionsRequest, IMaxioBillingService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ClaimsPrincipal user, IMaxioBillingService billingService, CancellationToken cancellationToken) =>
            {
                if (!SubscriptionUser.TryResolve(user, out var identity))
                {
                    return Results.Unauthorized();
                }

                return await HandleAsync(new MySubscriptionsRequest(identity.Reference), billingService, cancellationToken);
            })
            .Produces<MySubscriptionsResponse>()
            .ProducesProblem(StatusCodes.Status502BadGateway)
            .WithTags("SubscriptionEndpoints")
            .WithName("ListMySubscriptions");
    }

    public Task<IResult> HandleAsync(MySubscriptionsRequest request, IMaxioBillingService billingService)
        => HandleAsync(request, billingService, CancellationToken.None);

    public async Task<IResult> HandleAsync(MySubscriptionsRequest request, IMaxioBillingService billingService, CancellationToken cancellationToken)
    {
        var response = new MySubscriptionsResponse(request.CorrelationId());

        var subscriptions = await billingService.GetSubscriptionsAsync(request.CustomerReference, cancellationToken);
        response.Subscriptions = subscriptions.Select(CustomerSubscriptionDto.FromDomain).ToList();

        return Results.Ok(response);
    }
}
