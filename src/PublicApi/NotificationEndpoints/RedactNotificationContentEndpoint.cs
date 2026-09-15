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

/// <summary>
/// Operator action: disposes of a message's content at the provider on a shopper's request.
/// Afterwards the message text can no longer be retrieved from the provider, while the fact that a
/// message was sent, and what became of it, survives.
/// </summary>
public class RedactNotificationContentEndpoint : IEndpoint<IResult, RedactNotificationContentRequest, IOrderNotificationService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapDelete("api/notifications/{notificationId}/content",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int notificationId, IOrderNotificationService service) =>
            {
                return await HandleAsync(new RedactNotificationContentRequest(notificationId), service);
            })
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status404NotFound)
            .WithTags("NotificationEndpoints");
    }

    public async Task<IResult> HandleAsync(RedactNotificationContentRequest request, IOrderNotificationService service)
    {
        try
        {
            var found = await service.RedactNotificationContentAsync(request.NotificationId);
            return found ? Results.NoContent() : Results.NotFound();
        }
        catch (SmsNotificationException ex)
        {
            // The disposal genuinely did not happen at the provider — surface it.
            return ProviderErrorResults.From(ex);
        }
    }
}

public class RedactNotificationContentRequest : BaseRequest
{
    public RedactNotificationContentRequest(int notificationId)
    {
        NotificationId = notificationId;
    }

    public int NotificationId { get; set; }
}
