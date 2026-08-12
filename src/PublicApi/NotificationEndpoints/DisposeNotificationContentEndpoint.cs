using System;
using System.Linq;
using System.Threading.Tasks;
using Ardalis.Result;
using IResult = Microsoft.AspNetCore.Http.IResult;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.NotificationEndpoints;

public class DisposeNotificationContentRequest : BaseRequest
{
    public int NotificationId { get; set; }
}

public class DisposeNotificationContentResponse : BaseResponse
{
    public DisposeNotificationContentResponse(Guid correlationId) : base(correlationId) { }
    public DisposeNotificationContentResponse() { }

    public int NotificationId { get; set; }
    public string Status { get; set; } = "content_disposed";
}

/// <summary>
/// Operator action: disposes of a message's content at the shopper's request. The text is redacted at
/// the provider and cleared here; the fact a message was sent, and what became of it, survives.
/// </summary>
public class DisposeNotificationContentEndpoint : IEndpoint<IResult, DisposeNotificationContentRequest, IOrderNotificationService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapDelete("api/notifications/{notificationId}/content",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int notificationId, IOrderNotificationService service) =>
            {
                return await HandleAsync(new DisposeNotificationContentRequest { NotificationId = notificationId }, service);
            })
            .Produces<DisposeNotificationContentResponse>()
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status502BadGateway)
            .WithTags("NotificationEndpoints");
    }

    public async Task<IResult> HandleAsync(DisposeNotificationContentRequest request, IOrderNotificationService service)
    {
        var result = await service.DisposeContentAsync(request.NotificationId);
        if (result.Status == ResultStatus.NotFound)
        {
            return Results.NotFound();
        }
        if (result.Status == ResultStatus.Error)
        {
            // Disposal must succeed at the provider; if it did not, do not report success.
            return Results.Problem(
                detail: string.Join("; ", result.Errors),
                statusCode: StatusCodes.Status502BadGateway,
                title: "Content disposal failed at the provider.");
        }

        return Results.Ok(new DisposeNotificationContentResponse(request.CorrelationId())
        {
            NotificationId = request.NotificationId
        });
    }
}
