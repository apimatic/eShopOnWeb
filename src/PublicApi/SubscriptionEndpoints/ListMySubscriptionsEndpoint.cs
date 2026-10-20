using System.Collections.Generic;
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
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class ListMySubscriptionsResponse : BaseResponse
{
    public List<SubscriptionDto> Subscriptions { get; set; } = new();
    public bool IsTruncated { get; set; }
}

/// <summary>
/// Lists the authenticated user's subscriptions
/// </summary>
public class ListMySubscriptionsEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ClaimsPrincipal user, ISubscriptionService subscriptions, CancellationToken cancellationToken) =>
            {
                return await HandleAsync(user, subscriptions, cancellationToken);
            })
            .Produces<ListMySubscriptionsResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(ClaimsPrincipal user, ISubscriptionService subscriptions,
        CancellationToken cancellationToken)
    {
        var userKey = SubscriptionEndpointSupport.UserKey(user);
        if (string.IsNullOrWhiteSpace(userKey)) return Results.Unauthorized();

        try
        {
            var list = await subscriptions.ListMySubscriptionsAsync(userKey, cancellationToken);
            return Results.Ok(new ListMySubscriptionsResponse
            {
                IsTruncated = list.IsTruncated,
                Subscriptions = list.Subscriptions.Select(SubscriptionDto.From).ToList()
            });
        }
        catch (SubscriptionBillingException ex)
        {
            return SubscriptionEndpointSupport.Problem(ex);
        }
    }
}
