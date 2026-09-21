using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.NotificationEndpoints;

/// <summary>
/// Operator: re-sends a message that did not reach the shopper. The caller-supplied idempotency key
/// (Idempotency-Key header, or body/query) makes a repeat under the same key a no-op, while a fresh key
/// is a legitimate second attempt.
/// </summary>
public class ResendNotificationEndpoint : IEndpoint<IResult, IOrderNotificationService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/notifications/{notificationId}/resend",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int notificationId, ResendRequest? request, IOrderNotificationService service, HttpContext http) =>
            {
                var key = http.Request.Headers["Idempotency-Key"].FirstOrDefault();
                if (string.IsNullOrWhiteSpace(key))
                {
                    key = request?.IdempotencyKey;
                }
                if (string.IsNullOrWhiteSpace(key))
                {
                    key = http.Request.Query["idempotencyKey"].FirstOrDefault();
                }
                if (string.IsNullOrWhiteSpace(key))
                {
                    return Results.BadRequest(new { message = "An idempotency key is required (Idempotency-Key header, body, or query)." });
                }

                var result = await service.ResendAsync(notificationId, key!, http.RequestAborted);
                if (result is null)
                {
                    return Results.NotFound();
                }

                return Results.Ok(new ResendResponse { NotificationId = result.NotificationId, Deduplicated = result.Deduplicated });
            })
            .Produces<ResendResponse>()
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status404NotFound)
            .WithTags("NotificationEndpoints");
    }

    public Task<IResult> HandleAsync(IOrderNotificationService service) => Task.FromResult(Results.Empty as IResult);
}
