using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.NotificationEndpoints;

/// <summary>
/// Operator action: re-send a message that did not reach the shopper. The request carries a caller-supplied
/// idempotency key (the <c>Idempotency-Key</c> header); repeating under the same key does not send a second
/// message, while a fresh key is a legitimate new attempt. Returns the id of the message the resend produced.
/// </summary>
public class ResendNotificationEndpoint : IEndpoint<IResult, int, IOrderNotificationService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/notifications/{notificationId}/resend",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int notificationId, [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey, IOrderNotificationService service) =>
            {
                var result = await service.ResendAsync(notificationId, idempotencyKey);
                if (!result.Found)
                    return Results.NotFound();

                var response = new ResendNotificationResponse
                {
                    NotificationId = result.NotificationId!.Value,
                    Status = result.Status,
                    Reused = result.Reused
                };
                return Results.Ok(response);
            })
            .Produces<ResendNotificationResponse>()
            .Produces(StatusCodes.Status404NotFound)
            .WithTags("NotificationEndpoints");
    }

    // Convention member; the route work runs in the lambda above.
    public Task<IResult> HandleAsync(int notificationId, IOrderNotificationService service) =>
        Task.FromResult(Results.NotFound());
}

public class ResendNotificationResponse
{
    /// <summary>The identifier of the message the resend produced.</summary>
    public int NotificationId { get; set; }
    public string? Status { get; set; }

    /// <summary>True when this returned an earlier result under the same idempotency key rather than sending again.</summary>
    public bool Reused { get; set; }
}
