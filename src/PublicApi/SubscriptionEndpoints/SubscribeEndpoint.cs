using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Billing;
using Microsoft.eShopWeb.ApplicationCore.Services;
using Microsoft.eShopWeb.Infrastructure.Identity;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Subscribes the authenticated user to a plan (idempotent per user and plan)
/// </summary>
public class SubscribeEndpoint : IEndpoint<IResult, SubscribeRequest?, HttpContext, SubscriptionService>
{
    private const int MaxPlanHandleLength = 100;

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (SubscribeRequest? request, HttpContext http, SubscriptionService service) =>
            {
                return await HandleAsync(request, http, service);
            })
            .Produces<SubscribeResponse>(StatusCodes.Status201Created)
            .Produces<SubscribeResponse>(StatusCodes.Status200OK)
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(SubscribeRequest? request, HttpContext http, SubscriptionService service)
    {
        request ??= new SubscribeRequest();
        var response = new SubscribeResponse(request.CorrelationId());

        var caller = await BillingCaller.ResolveAsync(http);
        if (caller is null)
        {
            return Results.Unauthorized();
        }

        if (request.PlanHandle is { Length: > MaxPlanHandleLength })
        {
            throw new BillingRequestException("The plan handle is invalid.");
        }

        var result = await service.SubscribeAsync(caller, request.PlanHandle, http.RequestAborted);
        response.Subscription = SubscriptionDto.From(result.Subscription);
        response.Created = result.Created;

        return result.Created
            ? Results.Created("api/my-subscriptions", response)
            : Results.Ok(response);
    }
}
