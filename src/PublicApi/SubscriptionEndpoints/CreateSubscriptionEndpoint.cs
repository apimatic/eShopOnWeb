using System.Threading.Tasks;
using AutoMapper;
using BlazorShared.Models;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.Infrastructure.Subscriptions;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Subscribes the calling user to a subscription plan
/// </summary>
public class CreateSubscriptionEndpoint : IEndpoint<IResult, CreateSubscriptionRequest, HttpContext, ISubscriptionService>
{
    private readonly IMapper _mapper;

    public CreateSubscriptionEndpoint(IMapper mapper)
    {
        _mapper = mapper;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreateSubscriptionRequest request, HttpContext httpContext, ISubscriptionService subscriptionService) =>
            {
                return await HandleAsync(request, httpContext, subscriptionService);
            })
            .Produces<CreateSubscriptionResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(CreateSubscriptionRequest request, HttpContext httpContext, ISubscriptionService subscriptionService)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.PlanHandle))
        {
            return Results.BadRequest(new ErrorDetails
            {
                StatusCode = StatusCodes.Status400BadRequest,
                Message = "A planHandle is required."
            });
        }

        var response = new CreateSubscriptionResponse(request.CorrelationId());

        var userId = httpContext.User.Identity?.Name;
        if (string.IsNullOrWhiteSpace(userId))
        {
            return Results.Unauthorized();
        }

        var subscription = await subscriptionService.SubscribeAsync(userId, request.PlanHandle, httpContext.RequestAborted);

        response.Subscription = _mapper.Map<SubscriptionDto>(subscription);

        return Results.Ok(response);
    }
}
