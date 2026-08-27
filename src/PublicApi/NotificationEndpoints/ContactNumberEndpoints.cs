using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.NotificationEndpoints;

public sealed class ContactNumberEndpoints : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/contact-numbers",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (RegisterContactNumberRequest request, HttpContext httpContext, IOrderNotificationService service, CancellationToken cancellationToken) =>
                await NotificationEndpointResults.ExecuteAsync(async () =>
                {
                    ContactNumberDto contact = await service.RegisterContactNumberAsync(
                        Buyer(httpContext), request.PhoneNumber ?? string.Empty, cancellationToken);
                    return Results.Created($"/api/contact-numbers/{contact.ContactNumberId}", new
                    {
                        contactNumberId = contact.ContactNumberId,
                        phoneNumber = contact.PhoneNumber,
                        createdAt = contact.CreatedAt
                    });
                }))
            .Produces(StatusCodes.Status201Created)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .WithTags("ContactNumbers");

        app.MapGet("api/contact-numbers",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (HttpContext httpContext, IOrderNotificationService service, CancellationToken cancellationToken) =>
            {
                IReadOnlyList<ContactNumberDto> numbers = await service.GetContactNumbersAsync(Buyer(httpContext), cancellationToken);
                return Results.Ok(new { contactNumbers = numbers });
            })
            .Produces(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized)
            .WithTags("ContactNumbers");

        app.MapDelete("api/contact-numbers/{contactNumberId:int}",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int contactNumberId, HttpContext httpContext, IOrderNotificationService service, CancellationToken cancellationToken) =>
            {
                bool removed = await service.DeleteContactNumberAsync(Buyer(httpContext), contactNumberId, cancellationToken);
                return removed ? Results.NoContent() : Results.NotFound();
            })
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status401Unauthorized)
            .WithTags("ContactNumbers");
    }

    private static string Buyer(HttpContext context) => context.User.Identity?.Name ?? string.Empty;
}

public sealed record RegisterContactNumberRequest(string? PhoneNumber);
