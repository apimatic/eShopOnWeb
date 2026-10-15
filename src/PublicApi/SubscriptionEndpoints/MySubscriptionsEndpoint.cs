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

public class MySubscriptionsRequest : BaseRequest
{
    public string UserName { get; init; } = string.Empty;
    public CancellationToken CancellationToken { get; init; }
}

public class MySubscriptionsResponse : BaseResponse
{
    public MySubscriptionsResponse(Guid correlationId) : base(correlationId) { }
    public MySubscriptionsResponse() { }

    public List<SubscriptionDto> Subscriptions { get; set; } = new();
}

/// <summary>
/// Lists the signed-in user's subscriptions
/// </summary>
public class MySubscriptionsEndpoint : IEndpoint<IResult, MySubscriptionsRequest, ISubscriptionService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ClaimsPrincipal user, ISubscriptionService service, CancellationToken cancellationToken) =>
            {
                var userName = user.FindFirstValue(ClaimTypes.Name);
                if (string.IsNullOrWhiteSpace(userName)) return Results.Unauthorized();
                return await HandleAsync(new MySubscriptionsRequest { UserName = userName, CancellationToken = cancellationToken }, service);
            })
            .Produces<MySubscriptionsResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(MySubscriptionsRequest request, ISubscriptionService service)
    {
        var response = new MySubscriptionsResponse(request.CorrelationId());
        var subscriptions = await service.ListSubscriptionsAsync(request.UserName, request.CancellationToken);
        response.Subscriptions.AddRange(subscriptions.Select(SubscriptionMapper.ToDto));
        return Results.Ok(response);
    }
}
