using System;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.PublicApi.Services;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class ListUserSubscriptionsEndpoint : IEndpoint<IResult>
{
    private readonly MaxioSubscriptionService _service;

    public ListUserSubscriptionsEndpoint(MaxioSubscriptionService service)
    {
        _service = service;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (HttpContext httpContext, CancellationToken ct) =>
            {
                return await HandleAsyncInternal(httpContext, ct);
            })
            .Produces<ListUserSubscriptionsResponse>()
            .WithName("ListMySubscriptions")
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync()
    {
        return await Task.FromResult(Results.BadRequest());
    }

    private async Task<IResult> HandleAsyncInternal(HttpContext httpContext, CancellationToken ct)
    {
        var response = new ListUserSubscriptionsResponse(Guid.NewGuid());

        try
        {
            var userId = httpContext.User.FindFirst(ClaimTypes.NameIdentifier)?.Value
                ?? throw new UnauthorizedAccessException("User ID not found in token");

            var subscriptions = await _service.GetUserSubscriptionsAsync(userId, ct);
            response.Subscriptions.AddRange(subscriptions);

            return Results.Ok(response);
        }
        catch (UnauthorizedAccessException)
        {
            return Results.Unauthorized();
        }
        catch (Exception ex)
        {
            return Results.BadRequest(new { error = ex.Message });
        }
    }
}
