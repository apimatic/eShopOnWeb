using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.PublicApi.Subscriptions;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class MySubscriptionsRequest : BaseRequest
{
    [System.Text.Json.Serialization.JsonIgnore]
    public string UserName { get; set; } = string.Empty;
}

public class MySubscriptionsResponse : BaseResponse
{
    public MySubscriptionsResponse(Guid correlationId) : base(correlationId)
    {
    }

    public MySubscriptionsResponse()
    {
    }

    public IReadOnlyList<SubscriptionDto> Subscriptions { get; set; } = Array.Empty<SubscriptionDto>();
}

/// <summary>
/// Lists the authenticated user's subscriptions
/// </summary>
public class MySubscriptionsEndpoint : IEndpoint<IResult, MySubscriptionsRequest, MaxioSubscriptionService, CancellationToken>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ClaimsPrincipal user, MaxioSubscriptionService service, CancellationToken ct) =>
            {
                return await HandleAsync(new MySubscriptionsRequest { UserName = user.Identity?.Name ?? string.Empty }, service, ct);
            })
            .Produces<MySubscriptionsResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(MySubscriptionsRequest request, MaxioSubscriptionService service, CancellationToken ct)
    {
        try
        {
            var subs = await service.ListMySubscriptionsAsync(request.UserName, ct);
            return Results.Ok(new MySubscriptionsResponse(request.CorrelationId()) { Subscriptions = subs });
        }
        catch (BillingException ex)
        {
            return ex.ToResult();
        }
    }
}
