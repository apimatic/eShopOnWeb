using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Records pay-as-you-go usage against a subscription's metered component (UC2)
/// </summary>
public class RecordUsageEndpoint : IEndpoint<IResult, RecordUsageRequest, ISubscriptionService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions/usage",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (RecordUsageRequest request, ClaimsPrincipal user, ISubscriptionService subscriptionService,
                CancellationToken cancellationToken) =>
            {
                request.UserReference = user.Identity?.Name ?? string.Empty;
                // Metering someone else's subscription is an administrative act.
                request.IsAdministrator = user.IsInRole(BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS);
                return await HandleAsync(request, subscriptionService, cancellationToken);
            })
            .Produces<RecordUsageResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public Task<IResult> HandleAsync(RecordUsageRequest request, ISubscriptionService subscriptionService) =>
        HandleAsync(request, subscriptionService, CancellationToken.None);

    public async Task<IResult> HandleAsync(RecordUsageRequest request,
        ISubscriptionService subscriptionService,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.UserReference))
        {
            return Results.Unauthorized();
        }

        if (request.Quantity <= 0)
        {
            return Results.BadRequest("Quantity must be greater than zero.");
        }

        if (request.SubscriptionId.HasValue && !request.IsAdministrator)
        {
            // An explicit status, not Results.Forbid(): the host's default forbid handler is
            // Identity's cookie scheme, which would answer an API caller with a login redirect.
            return Results.StatusCode(StatusCodes.Status403Forbidden);
        }

        var response = new RecordUsageResponse(request.CorrelationId());

        var report = request.SubscriptionId.HasValue
            ? await subscriptionService.RecordUsageForSubscriptionAsync(request.SubscriptionId.Value,
                request.Quantity, request.Memo, cancellationToken)
            : await subscriptionService.RecordUsageAsync(request.UserReference, request.Quantity, request.Memo,
                cancellationToken);

        response.Usage = report.ToDto();

        return Results.Ok(response);
    }
}
