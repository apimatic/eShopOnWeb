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

/// <summary>
/// Subscribes the authenticated eShop user to a plan. Ensures a Maxio customer exists for the
/// caller (idempotent) and enrolls them; a double-click never creates a second customer or a
/// second live subscription to the same plan.
/// </summary>
public class SubscribeEndpoint : IEndpoint<IResult, SubscribeRequest, ClaimsPrincipal, CancellationToken>
{
    private readonly IMaxioSubscriptionService _service;

    public SubscribeEndpoint(IMaxioSubscriptionService service)
    {
        _service = service;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (SubscribeRequest request, ClaimsPrincipal user, CancellationToken ct) =>
                await HandleAsync(request, user, ct))
            .Produces<SubscribeResponse>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .ProducesProblem(StatusCodes.Status502BadGateway)
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(SubscribeRequest request, ClaimsPrincipal user, CancellationToken ct)
    {
        var userName = user.Identity?.Name ?? user.FindFirstValue(ClaimTypes.Name);
        if (string.IsNullOrWhiteSpace(userName))
        {
            return Results.Unauthorized();
        }

        var response = new SubscribeResponse(request.CorrelationId());
        try
        {
            var outcome = await _service.SubscribeAsync(userName, request?.PlanHandle, ct);
            response.Subscription = outcome.Subscription;
            response.AlreadySubscribed = outcome.AlreadySubscribed;
            return Results.Ok(response);
        }
        catch (MaxioApiException ex)
        {
            return SubscriptionResults.Problem(ex);
        }
    }
}
