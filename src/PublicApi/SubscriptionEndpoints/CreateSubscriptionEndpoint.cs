using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Maxio;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Enrolls the calling shopper in a Maxio subscription plan - the hero "Subscribe" flow.
/// Ensures a Maxio customer exists for them first, then idempotently enrolls them: a double-click
/// (or a retry of the same request) finds and returns the existing subscription instead of creating
/// a second one.
/// </summary>
public class CreateSubscriptionEndpoint : IEndpoint<IResult, CreateSubscriptionRequest, IMaxioSubscriptionService, HttpContext>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreateSubscriptionRequest request, IMaxioSubscriptionService maxioSubscriptionService, HttpContext httpContext) =>
            {
                return await HandleAsync(request, maxioSubscriptionService, httpContext);
            })
            .Produces<CreateSubscriptionResponse>()
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(CreateSubscriptionRequest request, IMaxioSubscriptionService maxioSubscriptionService, HttpContext httpContext)
    {
        var username = httpContext.User.Identity?.Name;
        if (string.IsNullOrEmpty(username))
        {
            return Results.Unauthorized();
        }

        if (string.IsNullOrWhiteSpace(request.PlanHandle))
        {
            return Results.BadRequest("planHandle is required.");
        }

        var response = new CreateSubscriptionResponse(request.CorrelationId());

        var identity = MaxioCustomerIdentity.FromEShopUsername(username);
        var subscription = await maxioSubscriptionService.SubscribeAsync(identity, request.PlanHandle, httpContext.RequestAborted);
        response.Subscription = SubscriptionSummaryDto.FromServiceDto(subscription);

        return Results.Ok(response);
    }
}
