using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.Infrastructure.Messaging;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.NotificationEndpoints;

public sealed class DeleteContactNumberEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapDelete("api/contact-numbers/{contactNumberId:int}",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async (
                int contactNumberId,
                HttpContext context,
                OrderNotificationService service,
                CancellationToken cancellationToken) =>
                EndpointResultMapper.Map(await service.DeleteContactNumberAsync(
                    context.User.Identity!.Name!, contactNumberId, cancellationToken)))
            .WithTags("ContactNumbers");
    }
}
