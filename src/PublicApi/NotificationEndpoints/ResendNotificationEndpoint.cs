using System;
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
/// Operator action: re-sends a message that did not reach the shopper. The caller-supplied idempotency
/// key makes a repeat under the same key send nothing new, while a fresh key is a legitimate new attempt.
/// The response returns the identifier of the message the resend produced.
/// </summary>
public class ResendNotificationEndpoint : IEndpoint<IResult, int, ResendNotificationRequest, IOrderNotificationService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/notifications/{notificationId}/resend",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int notificationId, ResendNotificationRequest request, IOrderNotificationService service) =>
            {
                return await HandleAsync(notificationId, request, service);
            })
            .Produces<ResendNotificationResponse>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status404NotFound)
            .WithTags("NotificationEndpoints");
    }

    public async Task<IResult> HandleAsync(int notificationId, ResendNotificationRequest request, IOrderNotificationService service)
    {
        var result = await service.ResendAsync(notificationId, request?.IdempotencyKey ?? string.Empty);

        return result.Outcome switch
        {
            ResendOutcome.Sent => Results.Ok(new ResendNotificationResponse { NotificationId = result.NotificationId!.Value }),
            ResendOutcome.DuplicateIgnored => Results.Ok(new ResendNotificationResponse { NotificationId = result.NotificationId!.Value }),
            ResendOutcome.NotFound => Results.NotFound(),
            _ => Results.BadRequest(new { error = result.Error })
        };
    }
}

public class ResendNotificationRequest : BaseRequest
{
    /// <summary>Caller-supplied key that makes the resend idempotent.</summary>
    public string? IdempotencyKey { get; set; }
}

public class ResendNotificationResponse : BaseResponse
{
    public ResendNotificationResponse(Guid correlationId) : base(correlationId) { }
    public ResendNotificationResponse() { }

    /// <summary>Identifier of the message this resend produced.</summary>
    public int NotificationId { get; set; }
}
