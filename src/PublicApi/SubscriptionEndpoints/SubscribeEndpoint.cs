using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// POST /api/subscriptions — ensures a Maxio customer exists for the caller (idempotent) and enrolls them
/// in the requested plan. A double-click returns the existing subscription rather than creating a second.
/// </summary>
public class SubscribeEndpoint : IEndpoint<IResult, SubscribeRequest, ISubscriptionBillingService>
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public SubscribeEndpoint(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (SubscribeRequest request, ISubscriptionBillingService billing) => await HandleAsync(request, billing))
            .Produces<SubscribeResponse>(StatusCodes.Status201Created)
            .Produces<SubscribeResponse>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status502BadGateway)
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(SubscribeRequest request, ISubscriptionBillingService billing)
    {
        var http = _httpContextAccessor.HttpContext!;

        var identity = await SubscriberIdentityResolver.ResolveAsync(http);
        if (identity is null)
        {
            return Results.Unauthorized();
        }

        if (string.IsNullOrWhiteSpace(request?.PlanHandle))
        {
            return Results.Problem(
                detail: "A planHandle is required.", statusCode: StatusCodes.Status400BadRequest,
                title: "Invalid subscription request");
        }

        try
        {
            var result = await billing.SubscribeAsync(identity, request.PlanHandle, http.RequestAborted);
            var response = new SubscribeResponse(request.CorrelationId())
            {
                Subscription = BillingResultMapper.ToDto(result.Subscription),
                CustomerId = result.CustomerId,
                AlreadySubscribed = result.AlreadySubscribed
            };

            return result.AlreadySubscribed
                ? Results.Ok(response)
                : Results.Created("api/my-subscriptions", response);
        }
        catch (SubscriptionBillingException ex)
        {
            return BillingResultMapper.ToProblem(ex);
        }
    }
}
