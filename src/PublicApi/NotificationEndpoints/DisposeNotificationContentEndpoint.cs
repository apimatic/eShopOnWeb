using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Services;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.NotificationEndpoints;

/// <summary>
/// Operator action: disposes of a message's content at the provider (and locally) at a shopper's
/// request. Afterwards the text is no longer retrievable from the provider, while the fact a message
/// was sent and what became of it survives.
/// </summary>
public class DisposeNotificationContentEndpoint : IEndpoint<IResult, int, INotificationAdminService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapDelete("api/notifications/{notificationId}/content",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int notificationId, INotificationAdminService service) =>
            {
                try
                {
                    var result = await service.DisposeContentAsync(notificationId);
                    return result.Status == DisposeStatus.NotFound
                        ? Results.NotFound()
                        : Results.NoContent();
                }
                catch (SmsGatewayException ex)
                {
                    // The provider copy could not be disposed of — surface it; the local copy is untouched.
                    return SmsProviderProblem.ToResult(ex);
                }
            })
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status404NotFound)
            .WithTags("NotificationEndpoints");
    }

    public Task<IResult> HandleAsync(int notificationId, INotificationAdminService service) =>
        Task.FromResult(Results.NoContent());
}
