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
/// Operator action on a shopper's behalf: disposes of a message's content. Afterwards the text is
/// no longer retrievable at the provider either, while the fact that a message was sent — and what
/// became of it — survives.
/// </summary>
public class DeleteNotificationContentEndpoint : IEndpoint<IResult, int, IOrderNotificationService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapDelete("api/notifications/{notificationId}/content",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int notificationId, IOrderNotificationService notificationService) =>
            {
                return await HandleAsync(notificationId, notificationService);
            })
            .Produces<DeleteNotificationContentResponse>()
            .Produces(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status502BadGateway)
            .WithTags("NotificationEndpoints");
    }

    public async Task<IResult> HandleAsync(int notificationId, IOrderNotificationService notificationService)
    {
        var result = await notificationService.DisposeContentAsync(notificationId);

        if (!result.Found)
            return Results.NotFound();

        if (result.Error is not null)
            return Results.Problem(detail: result.Error, statusCode: StatusCodes.Status502BadGateway);

        return Results.Ok(new DeleteNotificationContentResponse { ProviderRedacted = result.ProviderRedacted });
    }
}

public class DeleteNotificationContentResponse : BaseResponse
{
    /// <summary>True when the message had reached the provider and its body was redacted there.</summary>
    public bool ProviderRedacted { get; set; }
}
