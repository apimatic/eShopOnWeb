using System;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.PublicApi.Services;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class CreateSubscriptionEndpoint : IEndpoint<IResult, CreateSubscriptionRequest>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            Handler)
            .RequireAuthorization()
            .WithTags("SubscriptionEndpoints")
            .WithName("CreateSubscription")
            .Produces<CreateSubscriptionResponse>()
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status500InternalServerError);
    }

    public Task<IResult> HandleAsync(CreateSubscriptionRequest request)
    {
        throw new NotImplementedException();
    }

    private async Task<IResult> Handler(CreateSubscriptionRequest request, HttpContext httpContext, IMaxioSubscriptionService service)
    {
        var userId = httpContext.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(userId))
        {
            return Results.Unauthorized();
        }

        var response = new CreateSubscriptionResponse();

        try
        {
            var subscription = await service.CreateSubscriptionAsync(userId, request.ProductHandle);

            if (subscription == null)
            {
                return Results.StatusCode(StatusCodes.Status500InternalServerError);
            }

            response.Subscription = subscription;
            return Results.Ok(response);
        }
        catch
        {
            return Results.StatusCode(StatusCodes.Status500InternalServerError);
        }
    }
}
