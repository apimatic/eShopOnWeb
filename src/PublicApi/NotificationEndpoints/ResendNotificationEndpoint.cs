using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.NotificationEndpoints;

/// <summary>
/// Operator action: re-sends a message that did not reach the shopper. Repeating the request under the same
/// idempotency key does not send a second message; a fresh key is a legitimate new attempt.
/// </summary>
public class ResendNotificationEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/notifications/{notificationId:int}/resend",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
                AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int notificationId, ResendNotificationRequest request, HttpContext http, IOrderNotificationService service) =>
                await HandleAsync(notificationId, request, service, http.RequestAborted))
            .Produces<ResendNotificationResponse>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .WithTags("NotificationEndpoints");
    }

    public async Task<IResult> HandleAsync(
        int notificationId, ResendNotificationRequest request, IOrderNotificationService service, CancellationToken ct)
    {
        try
        {
            var resend = await service.ResendAsync(notificationId, request.IdempotencyKey, ct);
            if (resend is null)
                return Results.NotFound();

            return Results.Ok(new ResendNotificationResponse { NotificationId = resend.Id });
        }
        catch (OrderValidationException ex)
        {
            return Results.BadRequest(new { error = ex.Message });
        }
    }
}
