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
/// Operator action: re-sends a message that did not reach the shopper. The caller supplies an
/// idempotency key (the <c>Idempotency-Key</c> header): repeating under the same key does not send a
/// second message, while a fresh key is a legitimate second attempt. Restricted to the administrator role.
/// </summary>
public class ResendNotificationEndpoint : IEndpoint<IResult, int, string, IOrderNotificationService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/notifications/{notificationId}/resend",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
                       AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int notificationId, [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey,
             IOrderNotificationService service) =>
            {
                if (string.IsNullOrWhiteSpace(idempotencyKey))
                    return Results.ValidationProblem(new System.Collections.Generic.Dictionary<string, string[]>
                    {
                        ["Idempotency-Key"] = new[] { "The Idempotency-Key header is required." }
                    });

                return await HandleAsync(notificationId, idempotencyKey, service);
            })
            .Produces<ResendNotificationResponse>()
            .ProducesValidationProblem()
            .Produces(StatusCodes.Status404NotFound)
            .WithTags("NotificationEndpoints");
    }

    public async Task<IResult> HandleAsync(int notificationId, string idempotencyKey, IOrderNotificationService service)
    {
        var result = await service.ResendAsync(notificationId, idempotencyKey);
        if (!result.IsSuccess)
            return result.ToFailureResult();

        var dto = OrderNotificationDto.From(result.Value);
        return Results.Ok(new ResendNotificationResponse
        {
            NotificationId = dto.NotificationId,
            DeliveryOutcome = dto.DeliveryOutcome,
            ProviderMessageSid = dto.ProviderMessageSid
        });
    }
}
