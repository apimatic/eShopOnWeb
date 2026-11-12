using System.Linq;
using System.Security.Claims;
using System.Threading;
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
/// Lists the authenticated user's subscriptions
/// </summary>
public class MySubscriptionsListEndpoint : IEndpoint<IResult, ListMySubscriptionsRequest, IMaxioBillingService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ClaimsPrincipal user, IMaxioBillingService billing, CancellationToken ct) =>
            {
                return await HandleAsync(new ListMySubscriptionsRequest(), user.Identity?.Name, billing, ct);
            })
            .Produces<ListMySubscriptionsResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public Task<IResult> HandleAsync(ListMySubscriptionsRequest request, IMaxioBillingService billing) =>
        HandleAsync(request, null, billing, CancellationToken.None);

    public async Task<IResult> HandleAsync(ListMySubscriptionsRequest request, string? userName,
        IMaxioBillingService billing, CancellationToken ct)
    {
        try
        {
            var subs = await billing.ListMySubscriptionsAsync(userName, ct);
            return Results.Ok(new ListMySubscriptionsResponse(request.CorrelationId())
            {
                Subscriptions = subs.ToList()
            });
        }
        catch (BillingException ex)
        {
            return BillingResults.From(ex, request.CorrelationId());
        }
    }
}
