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
/// idempotency key (the <c>Idempotency-Key</c> header): repeating a request under the same key does
/// not send a second message, while a genuine second attempt under a fresh key does.
/// </summary>
public class ResendNotificationEndpoint : IEndpoint<IResult, int, IOrderNotificationService, string>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/notifications/{notificationId:int}/resend",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int notificationId, [FromServices] IOrderNotificationService service, [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey) =>
                await HandleAsync(notificationId, service, idempotencyKey!))
            .Produces<ResendNotificationResponse>(StatusCodes.Status201Created)
            .Produces<ResendNotificationResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status404NotFound)
            .WithTags("NotificationEndpoints");
    }

    public async Task<IResult> HandleAsync(int notificationId, IOrderNotificationService service, string? idempotencyKey)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            return Results.BadRequest(new { message = "An 'Idempotency-Key' header is required." });
        }

        var result = await service.ResendAsync(notificationId, idempotencyKey);
        if (result.OriginalNotFound)
        {
            return Results.NotFound();
        }
        if (result.Error != null || result.Notification is null)
        {
            return Results.BadRequest(new { message = result.Error ?? "The message could not be resent." });
        }

        var response = new ResendNotificationResponse(result.Notification.Id, result.AlreadyProcessed);
        // A repeat under the same key is a no-op that reports the earlier message; a new send is a create.
        return result.AlreadyProcessed
            ? Results.Ok(response)
            : Results.Created($"api/notifications/{result.Notification.Id}", response);
    }
}

public class ResendNotificationResponse : BaseResponse
{
    public ResendNotificationResponse(int notificationId, bool alreadyProcessed)
    {
        NotificationId = notificationId;
        AlreadyProcessed = alreadyProcessed;
    }

    /// <summary>Identifier of the message the resend produced, as a top-level field.</summary>
    public int NotificationId { get; set; }

    /// <summary>True when this request repeated an earlier idempotency key and sent nothing further.</summary>
    public bool AlreadyProcessed { get; set; }
}
