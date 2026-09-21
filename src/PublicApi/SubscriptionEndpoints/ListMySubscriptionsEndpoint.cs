using System;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>Request for <see cref="ListMySubscriptionsEndpoint"/> (carries caller identity + cancellation token).</summary>
public class ListMySubscriptionsRequest : BaseRequest
{
    [JsonIgnore]
    public ClaimsPrincipal? Caller { get; set; }

    [JsonIgnore]
    public CancellationToken CancellationToken { get; set; }
}

public class ListMySubscriptionsResponse : BaseResponse
{
    public ListMySubscriptionsResponse(Guid correlationId) : base(correlationId) { }
    public ListMySubscriptionsResponse() { }

    public System.Collections.Generic.List<CustomerSubscriptionDto> Subscriptions { get; set; } = new();
}

/// <summary>
/// Lists the authenticated caller's subscriptions. JWT-authenticated.
/// </summary>
public class ListMySubscriptionsEndpoint : IEndpoint<IResult, ListMySubscriptionsRequest, ISubscriptionBillingService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ClaimsPrincipal user, ISubscriptionBillingService billingService, CancellationToken cancellationToken) =>
            {
                return await HandleAsync(
                    new ListMySubscriptionsRequest { Caller = user, CancellationToken = cancellationToken },
                    billingService);
            })
            .Produces<ListMySubscriptionsResponse>()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(ListMySubscriptionsRequest request, ISubscriptionBillingService billingService)
    {
        var userReference = request.Caller!.GetUserReference();
        var subscriptions = await billingService.GetSubscriptionsAsync(userReference, request.CancellationToken);
        var response = new ListMySubscriptionsResponse(request.CorrelationId())
        {
            Subscriptions = subscriptions.Select(s => s.ToDto()).ToList()
        };
        return Results.Ok(response);
    }
}
