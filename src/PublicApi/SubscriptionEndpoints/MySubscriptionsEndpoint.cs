using System.Collections.Generic;
using System.Security.Claims;
using System.Text.Json.Serialization;
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

public class ListMySubscriptionsRequest : BaseRequest
{
    [JsonIgnore]
    public string? UserName { get; set; }

    [JsonIgnore]
    public CancellationToken Cancellation { get; set; }
}

public class ListMySubscriptionsResponse : BaseResponse
{
    public ListMySubscriptionsResponse(System.Guid correlationId) : base(correlationId)
    {
    }

    public ListMySubscriptionsResponse()
    {
    }

    public List<SubscriptionDto> Subscriptions { get; set; } = new();
}

/// <summary>
/// Lists the authenticated user's subscriptions
/// </summary>
public class MySubscriptionsEndpoint : IEndpoint<IResult, ListMySubscriptionsRequest, SubscriptionService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ClaimsPrincipal user, SubscriptionService service, CancellationToken ct) =>
            {
                return await HandleAsync(new ListMySubscriptionsRequest
                {
                    UserName = SubscriptionEndpointSupport.UserNameOf(user),
                    Cancellation = ct
                }, service);
            })
            .Produces<ListMySubscriptionsResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(ListMySubscriptionsRequest request, SubscriptionService service)
    {
        if (string.IsNullOrWhiteSpace(request.UserName))
        {
            return Results.Unauthorized();
        }

        try
        {
            var subscriptions = await service.ListMySubscriptionsAsync(request.UserName, request.Cancellation);
            return Results.Ok(new ListMySubscriptionsResponse(request.CorrelationId())
            {
                Subscriptions = new List<SubscriptionDto>(subscriptions)
            });
        }
        catch (SubscriptionException ex)
        {
            return SubscriptionEndpointSupport.Failure(ex);
        }
    }
}
