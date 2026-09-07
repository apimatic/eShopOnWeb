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

public class CreateSubscriptionEndpoint : IEndpoint<IResult, CreateSubscriptionRequest>
{
    private readonly MaxioSubscriptionService _service;

    public CreateSubscriptionEndpoint(MaxioSubscriptionService service)
    {
        _service = service;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (CreateSubscriptionRequest request, HttpContext httpContext, CancellationToken ct) =>
            {
                return await HandleAsyncInternal(request, httpContext, ct);
            })
            .Produces<CreateSubscriptionResponse>()
            .WithName("CreateSubscription")
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(CreateSubscriptionRequest request)
    {
        var response = new CreateSubscriptionResponse(request.CorrelationId());
        return await Task.FromResult(Results.BadRequest());
    }

    private async Task<IResult> HandleAsyncInternal(CreateSubscriptionRequest request, HttpContext httpContext, CancellationToken ct)
    {
        var response = new CreateSubscriptionResponse(request.CorrelationId());

        try
        {
            var userId = httpContext.User.FindFirst(ClaimTypes.NameIdentifier)?.Value
                ?? throw new UnauthorizedAccessException("User ID not found in token");

            var subscription = await _service.CreateSubscriptionAsync(userId, request.PlanHandle, ct);
            response.Subscription = subscription;

            return Results.Created($"api/subscriptions/{subscription.Id}", response);
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
