using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Threading;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Billing;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public sealed partial class CreateSubscriptionEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async (
                CreateSubscriptionRequest request,
                HttpContext httpContext,
                IShopperIdentityResolver identityResolver,
                ISubscriptionBillingService billingService,
                CancellationToken cancellationToken) =>
            {
                var productHandle = request.ProductHandle?.Trim();
                if (string.IsNullOrWhiteSpace(productHandle) || !ProductHandlePattern().IsMatch(productHandle))
                {
                    return Results.ValidationProblem(new Dictionary<string, string[]>
                    {
                        [nameof(request.ProductHandle)] =
                            ["ProductHandle must be 1-255 characters using letters, digits, hyphens, or underscores."]
                    });
                }

                var shopper = await identityResolver.ResolveAsync(httpContext.User, cancellationToken);
                var result = await billingService.SubscribeAsync(shopper, productHandle, cancellationToken);
                if (result.Outcome == SubscribeOutcome.Pending)
                {
                    httpContext.Response.Headers.RetryAfter = "2";
                    return Results.Accepted(
                        "/api/my-subscriptions",
                        new SubscriptionPendingResponse("pending", "/api/my-subscriptions"));
                }

                var response = SubscriptionDto.From(result.Subscription!);
                return result.Outcome == SubscribeOutcome.Created
                    ? Results.Created("/api/my-subscriptions", response)
                    : Results.Ok(response);
            })
            .Produces<SubscriptionDto>(StatusCodes.Status200OK)
            .Produces<SubscriptionDto>(StatusCodes.Status201Created)
            .Produces<SubscriptionPendingResponse>(StatusCodes.Status202Accepted)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .ProducesProblem(StatusCodes.Status502BadGateway)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable)
            .WithTags("SubscriptionEndpoints");
    }

    [GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9_-]{0,254}$", RegexOptions.CultureInvariant)]
    private static partial Regex ProductHandlePattern();
}
