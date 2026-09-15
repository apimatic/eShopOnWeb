using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.Infrastructure.Messaging;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.NotificationEndpoints;

public sealed class GetContactNumbersEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/contact-numbers",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async (
                HttpContext context,
                OrderNotificationService service,
                CancellationToken cancellationToken) =>
                Results.Ok(new { contactNumbers = await service.GetContactNumbersAsync(context.User.Identity!.Name!, cancellationToken) }))
            .WithTags("ContactNumbers");
    }
}
