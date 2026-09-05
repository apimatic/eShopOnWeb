using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading.Tasks;
using AutoMapper;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Services.Subscriptions;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class GetUserSubscriptionsEndpoint : IEndpoint<IResult, GetUserSubscriptionsRequest>
{
    private readonly ISubscriptionService _subscriptionService;
    private readonly IMapper _mapper;

    public GetUserSubscriptionsEndpoint(ISubscriptionService subscriptionService, IMapper mapper)
    {
        _subscriptionService = subscriptionService;
        _mapper = mapper;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ClaimsPrincipal user) =>
            {
                var request = new GetUserSubscriptionsRequest();
                var response = new GetUserSubscriptionsResponse(request.CorrelationId());

                try
                {
                    var userId = user.FindFirst(ClaimTypes.NameIdentifier)?.Value;

                    if (string.IsNullOrEmpty(userId))
                    {
                        return Results.Unauthorized();
                    }

                    var subscriptions = await _subscriptionService.GetUserSubscriptionsAsync(userId);
                    response.Subscriptions.AddRange(_mapper.Map<List<UserSubscriptionDto>>(subscriptions));
                }
                catch (Exception ex)
                {
                    response.Message = $"Error loading subscriptions: {ex.Message}";
                }

                return Results.Ok(response);
            })
            .Produces<GetUserSubscriptionsResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(GetUserSubscriptionsRequest request)
    {
        throw new NotImplementedException("Use AddRoute instead");
    }
}

public class GetUserSubscriptionsRequest : BaseRequest
{
}
