using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Lists the authenticated user's subscriptions. The user is resolved from the JWT and
/// mapped to their Maxio customer via a stable reference; returns an empty list if the
/// user has never subscribed.
/// </summary>
public class MySubscriptionsEndpoint : IEndpoint<IResult, MySubscriptionsRequest, IMaxioBillingService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ClaimsPrincipal user, IMaxioBillingService billingService, CancellationToken cancellationToken) =>
            {
                return await HandleAsync(new MySubscriptionsRequest(user.Identity?.Name), billingService, cancellationToken);
            })
            .Produces<ListMySubscriptionsResponse>()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status502BadGateway)
            .WithTags("SubscriptionEndpoints");
    }

    public Task<IResult> HandleAsync(MySubscriptionsRequest request, IMaxioBillingService billingService)
        => HandleAsync(request, billingService, CancellationToken.None);

    public async Task<IResult> HandleAsync(MySubscriptionsRequest request, IMaxioBillingService billingService, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.UserName))
            return Results.Unauthorized();

        var response = new ListMySubscriptionsResponse(request.CorrelationId());
        var subscriber = new SubscriberIdentity(request.UserName!);

        try
        {
            var subscriptions = await billingService.GetSubscriptionsAsync(subscriber, cancellationToken);
            response.Subscriptions = subscriptions.Select(s => s.ToDto()).ToList();
            return Results.Ok(response);
        }
        catch (MaxioBillingException ex)
        {
            return SubscriptionEndpointResults.FromBillingException(ex);
        }
    }
}
