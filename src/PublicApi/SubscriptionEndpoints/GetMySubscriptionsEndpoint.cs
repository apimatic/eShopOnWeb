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
using MinimalApi.Endpoint;
using Swashbuckle.AspNetCore.Annotations;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class GetMySubscriptionsEndpoint : IEndpoint<IResult, GetMySubscriptionsRequest, IMaxioSubscriptionService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (IMaxioSubscriptionService service, HttpContext context, CancellationToken ct) =>
            {
                var userId = context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value
                    ?? context.User.FindFirst("sub")?.Value
                    ?? throw new UnauthorizedAccessException("User ID not found in token");
                return await HandleAsync(new GetMySubscriptionsRequest(userId), service);
            })
            .Produces<GetMySubscriptionsResponse>()
            .WithName("GetMySubscriptions")
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(GetMySubscriptionsRequest request, IMaxioSubscriptionService service)
    {
        var response = new GetMySubscriptionsResponse(request.CorrelationId());

        try
        {
            var subscriptions = await service.GetUserSubscriptionsAsync(request.UserId, CancellationToken.None);
            foreach (var sub in subscriptions)
            {
                response.Subscriptions.Add(sub);
            }
            return Results.Ok(response);
        }
        catch (Exception ex)
        {
            return Results.BadRequest(new { error = ex.Message });
        }
    }
}

public class GetMySubscriptionsRequest : BaseRequest
{
    public GetMySubscriptionsRequest(string userId)
    {
        UserId = userId;
        _correlationId = System.Guid.NewGuid();
    }

    public string UserId { get; }
}

public class GetMySubscriptionsResponse : BaseResponse
{
    public GetMySubscriptionsResponse(Guid correlationId) : base(correlationId)
    {
    }

    public IList<SubscriptionDto> Subscriptions { get; } = new List<SubscriptionDto>();
}
