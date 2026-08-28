using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.Infrastructure.Messaging;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.NotificationEndpoints;

public sealed class RegisterContactNumberEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/contact-numbers",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async (
                RegisterContactNumberRequest request,
                HttpContext context,
                OrderNotificationService service,
                CancellationToken cancellationToken) =>
            {
                try
                {
                    var contact = await service.RegisterContactNumberAsync(
                        context.User.Identity!.Name!,
                        request.PhoneNumber,
                        request.CountryCode,
                        cancellationToken);
                    return Results.Created($"/api/contact-numbers/{contact.ContactNumberId}", new
                    {
                        contactNumberId = contact.ContactNumberId,
                        canonicalNumber = contact.CanonicalNumber
                    });
                }
                catch (ContactNumberValidationException ex)
                {
                    return Results.BadRequest(new { error = ex.Message, validationErrors = ex.ValidationErrors });
                }
                catch (Exception ex) when (ex is TwilioApiException or HttpRequestException or TaskCanceledException or InvalidOperationException)
                {
                    return Results.Json(new { error = "The phone number could not be validated by the provider." }, statusCode: 503);
                }
            })
            .WithTags("ContactNumbers")
            .Produces(StatusCodes.Status201Created)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized);
    }
}
