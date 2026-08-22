using System.Collections.Generic;
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

public class RedactNotificationContentRequest : BaseRequest
{
    public int NotificationId { get; init; }

    public RedactNotificationContentRequest(int notificationId)
    {
        NotificationId = notificationId;
    }
}

public class RedactNotificationContentEndpoint : IEndpoint<IResult, RedactNotificationContentRequest, IOrderNotificationService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapDelete("api/notifications/{notificationId}/content",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int notificationId, IOrderNotificationService notifications) =>
            {
                return await HandleAsync(new RedactNotificationContentRequest(notificationId), notifications);
            })
            .Produces(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound)
            .WithTags("NotificationEndpoints");
    }

    public async Task<IResult> HandleAsync(RedactNotificationContentRequest request, IOrderNotificationService notifications)
    {
        try
        {
            await notifications.RedactContentAsync(request.NotificationId, default);
            return Results.Ok(new { notificationId = request.NotificationId, contentRedacted = true });
        }
        catch (KeyNotFoundException)
        {
            return Results.NotFound();
        }
        catch (MessagingProviderException ex) when (ex.StatusCode == 404)
        {
            return Results.Json(
                new { message = "The message is not yet available for content disposal. Retry shortly." },
                statusCode: StatusCodes.Status409Conflict);
        }
        catch (MessagingProviderException ex) when (ex.StatusCode is >= 400 and < 500 and not 401 and not 403)
        {
            return Results.Json(new { message = ex.Message }, statusCode: ex.StatusCode.Value);
        }
        catch (MessagingProviderException)
        {
            return Results.Json(new { message = "The messaging provider is unavailable." }, statusCode: StatusCodes.Status502BadGateway);
        }
    }
}
