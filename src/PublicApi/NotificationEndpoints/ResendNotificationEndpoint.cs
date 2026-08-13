using System;
using System.Threading;
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
/// Operator action: re-send a message that did not reach the shopper. The caller supplies an
/// idempotency key (via the <c>Idempotency-Key</c> header or an <c>idempotencyKey</c> query value):
/// repeating the request under the same key does not send a second message, while a fresh key is a
/// legitimate new attempt. Returns the notificationId of the message the resend produced.
/// </summary>
public class ResendNotificationEndpoint : IEndpoint<IResult, ResendNotificationRequest, IOrderNotificationService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/notifications/{notificationId}/resend",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int notificationId, [FromHeader(Name = "Idempotency-Key")] string? idempotencyKeyHeader,
             [FromQuery] string? idempotencyKey, HttpContext http, IOrderNotificationService service) =>
            {
                var request = new ResendNotificationRequest
                {
                    NotificationId = notificationId,
                    IdempotencyKey = idempotencyKeyHeader ?? idempotencyKey
                };
                return await HandleAsync(request, service, http.RequestAborted);
            })
            .Produces<ResendNotificationResponse>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .WithTags("NotificationEndpoints");
    }

    public Task<IResult> HandleAsync(ResendNotificationRequest request, IOrderNotificationService service) =>
        HandleAsync(request, service, CancellationToken.None);

    public async Task<IResult> HandleAsync(ResendNotificationRequest request, IOrderNotificationService service, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.IdempotencyKey))
        {
            return Results.Problem(detail: "An idempotency key is required (Idempotency-Key header or idempotencyKey query value).",
                statusCode: StatusCodes.Status400BadRequest, title: "Missing idempotency key.");
        }

        var result = await service.ResendAsync(request.NotificationId, request.IdempotencyKey, ct);
        switch (result.Status)
        {
            case ResendStatus.NotificationNotFound:
                return Results.NotFound();
            case ResendStatus.NothingResendable:
                return Results.Problem(detail: result.Reason, statusCode: StatusCodes.Status409Conflict,
                    title: "The message cannot be resent.");
            default:
                var response = new ResendNotificationResponse(request.CorrelationId())
                {
                    NotificationId = result.Notification!.Id
                };
                return Results.Ok(response);
        }
    }
}

public class ResendNotificationRequest : BaseRequest
{
    public int NotificationId { get; set; }
    public string? IdempotencyKey { get; set; }
}

public class ResendNotificationResponse : BaseResponse
{
    public ResendNotificationResponse(Guid correlationId) : base(correlationId) { }

    /// <summary>The identifier of the message this resend produced.</summary>
    public int NotificationId { get; set; }
}
