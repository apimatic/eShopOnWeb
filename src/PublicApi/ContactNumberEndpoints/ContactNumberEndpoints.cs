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

public sealed record RegisterContactNumberRequest(string PhoneNumber, string? CountryCode);

public sealed class RegisterContactNumberEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/contact-numbers",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async (
                RegisterContactNumberRequest request,
                HttpContext httpContext,
                CatalogContext db,
                ISmsProvider provider,
                CancellationToken cancellationToken) =>
            {
                if (string.IsNullOrWhiteSpace(request.PhoneNumber))
                {
                    return Results.ValidationProblem(new Dictionary<string, string[]> { ["phoneNumber"] = new[] { "A phone number is required." } });
                }

                PhoneNumberValidationResult validation;
                try
                {
                    validation = await provider.ValidatePhoneNumberAsync(
                        request.PhoneNumber,
                        request.CountryCode,
                        cancellationToken);
                }
                catch (SmsProviderException)
                {
                    return Results.Problem(
                        "The messaging provider could not validate the number.",
                        statusCode: StatusCodes.Status503ServiceUnavailable);
                }

                if (!validation.IsValid || string.IsNullOrWhiteSpace(validation.E164Number))
                {
                    return Results.ValidationProblem(new Dictionary<string, string[]>
                    {
                        ["phoneNumber"] = validation.ValidationErrors.Count == 0
                            ? new[] { "The provider does not consider this a valid destination." }
                            : validation.ValidationErrors.ToArray()
                    });
                }

                var buyerId = CurrentUser.BuyerId(httpContext);
                var exists = await db.ContactNumbers.AnyAsync(
                    x => x.BuyerId == buyerId && x.E164Number == validation.E164Number,
                    cancellationToken);
                if (exists)
                {
                    return Results.Conflict(new { message = "That contact number is already registered." });
                }

                var contact = new ContactNumber(buyerId, validation.E164Number);
                db.ContactNumbers.Add(contact);
                await db.SaveChangesAsync(cancellationToken);
                return Results.Created("/api/contact-numbers", new
                {
                    contactNumberId = contact.Id,
                    phoneNumber = contact.E164Number
                });
            })
            .Produces(StatusCodes.Status201Created)
            .ProducesValidationProblem()
            .RequireAuthorization()
            .WithTags("ContactNumberEndpoints");
    }
}

public sealed class ListContactNumbersEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/contact-numbers",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async (
                HttpContext httpContext,
                CatalogContext db,
                CancellationToken cancellationToken) =>
            {
                var buyerId = CurrentUser.BuyerId(httpContext);
                var contacts = await db.ContactNumbers
                    .AsNoTracking()
                    .Where(x => x.BuyerId == buyerId)
                    .OrderBy(x => x.Id)
                    .Select(x => new { contactNumberId = x.Id, phoneNumber = x.E164Number })
                    .ToListAsync(cancellationToken);
                return Results.Ok(contacts);
            })
            .RequireAuthorization()
            .WithTags("ContactNumberEndpoints");
    }
}

public sealed class DeleteContactNumberEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapDelete("api/contact-numbers/{contactNumberId:int}",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async (
                int contactNumberId,
                HttpContext httpContext,
                CatalogContext db,
                OrderNotificationService notifications,
                CancellationToken cancellationToken) =>
            {
                var buyerId = CurrentUser.BuyerId(httpContext);
                var contact = await db.ContactNumbers.SingleOrDefaultAsync(
                    x => x.Id == contactNumberId && x.BuyerId == buyerId,
                    cancellationToken);
                if (contact is null)
                {
                    return Results.NotFound();
                }

                if (!await notifications.CancelScheduledMessagesForContactAsync(contact.Id, cancellationToken))
                {
                    return Results.Problem(
                        "A scheduled message could not yet be cancelled; the number remains registered.",
                        statusCode: StatusCodes.Status503ServiceUnavailable);
                }

                db.ContactNumbers.Remove(contact);
                await db.SaveChangesAsync(cancellationToken);
                return Results.NoContent();
            })
            .RequireAuthorization()
            .WithTags("ContactNumberEndpoints");
    }
}
