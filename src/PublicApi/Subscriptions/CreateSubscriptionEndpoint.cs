using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

public sealed class SubscribeRequest
{
    public string ProductHandle { get; init; } = string.Empty;
}

public class CreateSubscriptionEndpoint : IEndpoint<IResult, SubscribeRequest, SubscriptionService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (SubscribeRequest request, HttpContext context, SubscriptionService subscriptions, CancellationToken cancellationToken) =>
            {
                if (string.IsNullOrWhiteSpace(request.ProductHandle))
                    return Results.BadRequest(new { message = "productHandle is required." });

                try
                {
                    var result = await subscriptions.SubscribeAsync(context.User, request.ProductHandle.Trim(), cancellationToken);
                    return Results.Created($"api/my-subscriptions/{result.Id}", result);
                }
                catch (ArgumentException ex)
                {
                    return Results.BadRequest(new { message = ex.Message });
                }
                catch (SubscriptionEnrollmentInProgressException ex)
                {
                    return Results.Conflict(new { message = ex.Message });
                }
                catch (MaxioApiException)
                {
                    return Results.StatusCode(StatusCodes.Status502BadGateway);
                }
            })
            .Produces<SubscriptionDto>(StatusCodes.Status201Created)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status409Conflict)
            .Produces(StatusCodes.Status502BadGateway)
            .WithTags("Subscriptions");
    }

    public Task<IResult> HandleAsync(SubscribeRequest request, SubscriptionService subscriptions) => throw new NotSupportedException();
}
