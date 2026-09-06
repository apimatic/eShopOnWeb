using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.Infrastructure.Services;
using MinimalApi.Endpoint;
using Swashbuckle.AspNetCore.Annotations;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class GetUserSubscriptionsEndpoint : IEndpoint<IResult, EmptyRequest>
{
    private readonly IMaxioSubscriptionService _subscriptionService;

    public GetUserSubscriptionsEndpoint(IMaxioSubscriptionService subscriptionService)
    {
        _subscriptionService = subscriptionService;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (ClaimsPrincipal user, IMaxioSubscriptionService subscriptionService) =>
            {
                var userName = user.FindFirst(ClaimTypes.Name)?.Value;
                if (string.IsNullOrEmpty(userName))
                {
                    return Results.Unauthorized();
                }

                try
                {
                    var request = new EmptyRequest();
                    var result = await subscriptionService.GetUserSubscriptionsAsync(userName);
                    var response = new GetUserSubscriptionsResponse(request.CorrelationId())
                    {
                        Subscriptions = result.Subscriptions
                    };

                    return Results.Ok(response);
                }
                catch (Exception ex)
                {
                    return Results.BadRequest(new { error = ex.Message });
                }
            })
            .Produces<GetUserSubscriptionsResponse>()
            .WithTags("SubscriptionEndpoints")
            .WithName("GetMySubscriptions");
    }

    [SwaggerOperation(
        Summary = "Get user's subscriptions",
        Description = "Returns all subscriptions for the authenticated user",
        OperationId = "subscription.getMySubscriptions",
        Tags = new[] { "SubscriptionEndpoints" })]
    public async Task<IResult> HandleAsync(EmptyRequest request)
    {
        try
        {
            var result = await _subscriptionService.GetUserSubscriptionsAsync("");
            var response = new GetUserSubscriptionsResponse(request.CorrelationId());
            response.Subscriptions.AddRange(result.Subscriptions);

            return Results.Ok(response);
        }
        catch (Exception ex)
        {
            return Results.BadRequest(new { error = ex.Message });
        }
    }
}

public class GetUserSubscriptionsResponse : BaseResponse
{
    public GetUserSubscriptionsResponse(Guid correlationId) : base(correlationId) { }
    public GetUserSubscriptionsResponse() { }

    public List<Infrastructure.Services.SubscriptionDto> Subscriptions { get; set; } = new();
}
