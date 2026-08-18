using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.PublicApi.Notifications;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.NotificationEndpoints;

public class RedactContentRequest
{
    public int NotificationId { get; set; }
    public CancellationToken Ct { get; set; }
}

/// <summary>
/// Operator action: dispose of a message's content at the shopper's request. Afterwards the text is
/// no longer retrievable from the provider either, while the fact a message was sent and what became
/// of it survives. If the provider-side disposal fails, this reports an error rather than claiming
/// success.
/// </summary>
public class RedactNotificationContentEndpoint : IEndpoint<IResult, RedactContentRequest, IOrderNotificationService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapDelete("api/notifications/{notificationId:int}/content",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int notificationId, IOrderNotificationService service, CancellationToken ct) =>
            {
                return await HandleAsync(new RedactContentRequest { NotificationId = notificationId, Ct = ct }, service);
            })
            .WithTags("NotificationEndpoints");
    }

    public async Task<IResult> HandleAsync(RedactContentRequest request, IOrderNotificationService service)
    {
        try
        {
            var found = await service.RedactContentAsync(request.NotificationId, request.Ct);
            return found ? Results.NoContent() : Results.NotFound();
        }
        catch (SmsGatewayException ex)
        {
            // Provider-side disposal failed; do not claim success.
            return SmsErrorResults.ToResult(ex);
        }
    }
}
