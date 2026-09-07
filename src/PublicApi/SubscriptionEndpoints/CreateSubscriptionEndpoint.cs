using System;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using AutoMapper;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.PublicApi.Services;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public static class CreateSubscriptionEndpoint
{
    public static void MapCreateSubscription(this IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions", HandleAsync)
           .Produces<CreateSubscriptionResponse>()
           .WithTags("SubscriptionEndpoints")
           .RequireAuthorization();
    }

    private static async Task<IResult> HandleAsync(
        CreateSubscriptionRequest request,
        HttpContext context,
        IMaxioSubscriptionService subscriptionService,
        IMapper mapper,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var userId = context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value ??
                         context.User.FindFirst("sub")?.Value;

            if (string.IsNullOrEmpty(userId))
            {
                return Results.Unauthorized();
            }

            var subscription = await subscriptionService.CreateSubscriptionAsync(
                userId, request.ProductHandle, cancellationToken);

            var response = new CreateSubscriptionResponse
            {
                Subscription = mapper.Map<SubscriptionDto>(subscription)
            };

            return Results.Created($"/api/subscriptions/{subscription.Id}", response);
        }
        catch (InvalidOperationException ex)
        {
            return Results.Problem(detail: ex.Message, statusCode: StatusCodes.Status400BadRequest);
        }
        catch (Exception ex)
        {
            return Results.Problem(detail: ex.Message, statusCode: StatusCodes.Status500InternalServerError);
        }
    }
}

public class CreateSubscriptionRequest
{
    public string ProductHandle { get; set; } = string.Empty;
}

public class CreateSubscriptionResponse
{
    public SubscriptionDto Subscription { get; set; } = new();
}
