using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Ardalis.Result;
using IResult = Microsoft.AspNetCore.Http.IResult;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.NotificationEndpoints;

public class ResendNotificationRequest : BaseRequest
{
    public int NotificationId { get; set; }

    /// <summary>Caller-supplied idempotency key (from the <c>Idempotency-Key</c> header).</summary>
    public string IdempotencyKey { get; set; } = string.Empty;
}

public class ResendNotificationResponse : BaseResponse
{
    public ResendNotificationResponse(Guid correlationId) : base(correlationId) { }
    public ResendNotificationResponse() { }

    /// <summary>Identifier of the message the resend produced (top-level).</summary>
    public int NotificationId { get; set; }

    public string Status { get; set; } = string.Empty;
    public string? ProviderMessageSid { get; set; }
}

/// <summary>
/// Operator action: re-sends a message that did not reach the shopper. Repeating a request under the
/// same idempotency key does not send a second message; a genuine second attempt under a fresh key does.
/// </summary>
public class ResendNotificationEndpoint : IEndpoint<IResult, ResendNotificationRequest, IOrderNotificationService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/notifications/{notificationId}/resend",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int notificationId, [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey, IOrderNotificationService service) =>
            {
                return await HandleAsync(new ResendNotificationRequest
                {
                    NotificationId = notificationId,
                    IdempotencyKey = idempotencyKey ?? string.Empty
                }, service);
            })
            .Produces<ResendNotificationResponse>()
            .ProducesValidationProblem()
            .Produces(StatusCodes.Status404NotFound)
            .WithTags("NotificationEndpoints");
    }

    public async Task<IResult> HandleAsync(ResendNotificationRequest request, IOrderNotificationService service)
    {
        if (string.IsNullOrWhiteSpace(request.IdempotencyKey))
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["Idempotency-Key"] = new[] { "An Idempotency-Key header is required." }
            });
        }

        var result = await service.ResendAsync(request.NotificationId, request.IdempotencyKey);
        if (result.Status == ResultStatus.NotFound)
        {
            return Results.NotFound();
        }
        if (result.Status == ResultStatus.Invalid)
        {
            return Results.ValidationProblem(result.ValidationErrors.ToDictionary(
                e => string.IsNullOrEmpty(e.Identifier) ? "notification" : e.Identifier,
                e => new[] { e.ErrorMessage }));
        }

        var notification = result.Value;
        return Results.Ok(new ResendNotificationResponse(request.CorrelationId())
        {
            NotificationId = notification.Id,
            Status = notification.ProviderStatus,
            ProviderMessageSid = notification.ProviderMessageSid
        });
    }
}
