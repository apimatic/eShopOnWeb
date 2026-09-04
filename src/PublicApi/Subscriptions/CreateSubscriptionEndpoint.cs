using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Options;
using Microsoft.eShopWeb.Infrastructure.Identity;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

public sealed class CreateSubscriptionEndpoint : IEndpoint<IResult, SubscribeRequest, SubscriptionBillingService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
                async (SubscribeRequest request,
                       SubscriptionBillingService service,
                       IOptions<MaxioOptions> options,
                       UserManager<ApplicationUser> userManager,
                       HttpContext httpContext,
                       CancellationToken cancellationToken) =>
                {
                    if (string.IsNullOrWhiteSpace(request.ProductHandle))
                        return Results.ValidationProblem(new Dictionary<string, string[]>
                        {
                            [nameof(SubscribeRequest.ProductHandle)] = new[] { "A product handle is required." }
                        });

                    var userName = httpContext.User.Identity?.Name;
                    var user = string.IsNullOrWhiteSpace(userName)
                        ? null
                        : await userManager.FindByNameAsync(userName);
                    if (user is null)
                        return Results.Unauthorized();

                    var subscription = await service.SubscribeAsync(user, request.ProductHandle, options.Value, cancellationToken);
                    return subscription is null ? Results.NotFound() : Results.Ok(subscription);
                })
            .Produces<SubscriptionDto>()
            .ProducesValidationProblem()
            .RequireAuthorization(new AuthorizeAttribute { AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme })
            .WithTags("SubscriptionEndpoints");
    }

    public Task<IResult> HandleAsync(SubscribeRequest request, SubscriptionBillingService service) =>
        Task.FromResult<IResult>(Results.StatusCode(StatusCodes.Status501NotImplemented));
}
