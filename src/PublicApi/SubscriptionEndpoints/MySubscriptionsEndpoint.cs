using System;
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
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// List the caller's subscriptions. Administrators may list another user's by supplying
/// <c>userReference</c>.
/// </summary>
public class MySubscriptionsEndpoint : IEndpoint<IResult, ListSubscriptionsRequest, ISubscriptionService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async (
                string? userReference,
                ClaimsPrincipal user,
                ISubscriptionService subscriptionService,
                CancellationToken cancellationToken) =>
            {
                var reference = SubscriptionCaller.ResolveUserReference(user, userReference);
                if (reference is null)
                {
                    return SubscriptionCaller.Forbidden();
                }

                return await HandleAsync(
                    new ListSubscriptionsRequest { UserReference = reference },
                    subscriptionService,
                    cancellationToken);
            })
            .Produces<ListSubscriptionsResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public Task<IResult> HandleAsync(ListSubscriptionsRequest request, ISubscriptionService subscriptionService)
        => HandleAsync(request, subscriptionService, CancellationToken.None);

    public async Task<IResult> HandleAsync(
        ListSubscriptionsRequest request,
        ISubscriptionService subscriptionService,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.UserReference))
        {
            return Results.BadRequest("userReference could not be resolved for the caller.");
        }

        var subscriptions = await subscriptionService.ListSubscriptionsAsync(request.UserReference, cancellationToken);

        var response = new ListSubscriptionsResponse(request.CorrelationId());
        response.Subscriptions.AddRange(subscriptions.Select(SubscriptionDto.From));

        return Results.Ok(response);
    }
}

public class ListSubscriptionsRequest : BaseRequest
{
    /// <summary>
    /// The user whose subscriptions to list. Resolved from the caller's identity; only
    /// administrators may supply somebody else's reference.
    /// </summary>
    public string? UserReference { get; set; }
}

public class ListSubscriptionsResponse : BaseResponse
{
    public ListSubscriptionsResponse(Guid correlationId) : base(correlationId)
    {
    }

    public ListSubscriptionsResponse()
    {
    }

    public List<SubscriptionDto> Subscriptions { get; set; } = new();
}
