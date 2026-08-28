using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.eShopWeb.Infrastructure.Services;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.ContactNumberEndpoints;

public sealed class ContactNumberEndpoints : IEndpoint
{
    private const string AuthenticationScheme = JwtBearerDefaults.AuthenticationScheme;

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/contact-numbers",
            [Authorize(AuthenticationSchemes = AuthenticationScheme)] async (
                RegisterContactNumberRequest request,
                HttpContext httpContext,
                CatalogContext db,
                ITwilioMessagingService twilio,
                TimeProvider clock,
                CancellationToken cancellationToken) =>
            {
                var ownerId = httpContext.User.Identity!.Name!;
                string? normalized;
                try
                {
                    normalized = await twilio.ValidateAndNormalizeAsync(request.PhoneNumber ?? string.Empty, cancellationToken);
                }
                catch
                {
                    return Results.Problem("The phone-number validation provider is unavailable.", statusCode: StatusCodes.Status503ServiceUnavailable);
                }

                if (normalized is null)
                {
                    return Results.ValidationProblem(new Dictionary<string, string[]> { ["phoneNumber"] = new[] { "The destination is not a valid phone number." } });
                }

                var existing = await db.ContactNumbers.SingleOrDefaultAsync(
                    x => x.OwnerId == ownerId && x.PhoneNumber == normalized && x.DeletedAt == null,
                    cancellationToken);
                if (existing is not null)
                {
                    return Results.Ok(new ContactNumberCreatedResponse(existing.Id, existing.PhoneNumber));
                }

                var contactNumber = new ContactNumber(ownerId, normalized, clock.GetUtcNow());
                db.ContactNumbers.Add(contactNumber);
                await db.SaveChangesAsync(cancellationToken);
                return Results.Created($"/api/contact-numbers/{contactNumber.Id}",
                    new ContactNumberCreatedResponse(contactNumber.Id, contactNumber.PhoneNumber));
            })
            .Produces<ContactNumberCreatedResponse>(StatusCodes.Status201Created)
            .ProducesValidationProblem()
            .RequireAuthorization()
            .WithTags("ContactNumberEndpoints");

        app.MapGet("api/contact-numbers",
            [Authorize(AuthenticationSchemes = AuthenticationScheme)] async (
                HttpContext httpContext,
                CatalogContext db,
                CancellationToken cancellationToken) =>
            {
                var ownerId = httpContext.User.Identity!.Name!;
                var numbers = await db.ContactNumbers
                    .Where(x => x.OwnerId == ownerId && x.DeletedAt == null)
                    .OrderBy(x => x.Id)
                    .Select(x => new ContactNumberResponse(x.Id, x.PhoneNumber, x.CreatedAt))
                    .ToListAsync(cancellationToken);
                return Results.Ok(new ContactNumberListResponse(numbers));
            })
            .RequireAuthorization()
            .WithTags("ContactNumberEndpoints");

        app.MapDelete("api/contact-numbers/{contactNumberId:int}",
            [Authorize(AuthenticationSchemes = AuthenticationScheme)] async (
                int contactNumberId,
                HttpContext httpContext,
                CatalogContext db,
                OrderNotificationManager notificationManager,
                TimeProvider clock,
                CancellationToken cancellationToken) =>
            {
                var ownerId = httpContext.User.Identity!.Name!;
                var contactNumber = await db.ContactNumbers.SingleOrDefaultAsync(
                    x => x.Id == contactNumberId && x.OwnerId == ownerId && x.DeletedAt == null,
                    cancellationToken);
                if (contactNumber is null)
                {
                    return Results.NotFound();
                }

                contactNumber.Delete(clock.GetUtcNow());
                await db.SaveChangesAsync(cancellationToken);
                await notificationManager.RequestCancellationForContactAsync(contactNumber.Id, cancellationToken);
                return Results.NoContent();
            })
            .RequireAuthorization()
            .WithTags("ContactNumberEndpoints");
    }
}

public sealed record RegisterContactNumberRequest(string? PhoneNumber);
public sealed record ContactNumberCreatedResponse(int ContactNumberId, string PhoneNumber);
public sealed record ContactNumberResponse(int ContactNumberId, string PhoneNumber, DateTimeOffset CreatedAt);
public sealed record ContactNumberListResponse(System.Collections.Generic.IReadOnlyList<ContactNumberResponse> ContactNumbers);
