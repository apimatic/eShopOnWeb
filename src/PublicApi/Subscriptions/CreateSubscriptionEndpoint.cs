using System.Security.Claims;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

/// <summary>Enrolls the JWT-authenticated shopper in one configured Maxio plan.</summary>
public class CreateSubscriptionEndpoint : IEndpoint<IResult, CreateSubscriptionRequest, HttpContext, SubscriptionService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions", async (CreateSubscriptionRequest request, HttpContext context,
            SubscriptionService subscriptions, CancellationToken cancellationToken) =>
        {
            var username = context.User.FindFirstValue(ClaimTypes.Name);
            if (string.IsNullOrWhiteSpace(username)) return Results.Unauthorized();

            try
            {
                var subscription = await subscriptions.SubscribeAsync(username, request.ProductHandle ?? string.Empty, cancellationToken);
                return Results.Created($"api/subscriptions/{subscription.Id}", subscription);
            }
            catch (SubscriptionValidationException ex)
            {
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["productHandle"] = new[] { ex.Message } });
            }
            catch (MaxioConfigurationException)
            {
                return Results.Problem("Subscription billing is not configured.", statusCode: StatusCodes.Status503ServiceUnavailable);
            }
            catch (MaxioApiException)
            {
                return Results.Problem("Subscription billing is temporarily unavailable.", statusCode: StatusCodes.Status502BadGateway);
            }
        })
        .RequireAuthorization(new AuthorizeAttribute { AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme })
        .Produces<SubscriptionDto>(StatusCodes.Status201Created)
        .ProducesValidationProblem()
        .WithTags("Subscriptions");
    }

    public async Task<IResult> HandleAsync(CreateSubscriptionRequest request, HttpContext context, SubscriptionService subscriptions)
    {
        var username = context.User.FindFirstValue(ClaimTypes.Name);
        if (string.IsNullOrWhiteSpace(username)) return Results.Unauthorized();
        return Results.Created(string.Empty,
            await subscriptions.SubscribeAsync(username, request.ProductHandle ?? string.Empty, CancellationToken.None));
    }
}

public sealed record CreateSubscriptionRequest(string? ProductHandle);
