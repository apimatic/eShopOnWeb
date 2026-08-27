using System;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Data;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.ContactNumberEndpoints;

public sealed record RegisterContactNumberRequest(string PhoneNumber, string? CountryCode);
public sealed record ContactNumberCreatedResponse(int ContactNumberId, string PhoneNumber);
public sealed record ContactNumberResponse(int ContactNumberId, string PhoneNumber, DateTimeOffset CreatedAt);

public sealed class RegisterContactNumberEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/contact-numbers",
                [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async (
                    RegisterContactNumberRequest request,
                    ClaimsPrincipal principal,
                    CatalogContext context,
                    ISmsProvider provider,
                    TimeProvider timeProvider,
                    CancellationToken cancellationToken) =>
                {
                    var buyerId = principal.Identity?.Name;
                    if (string.IsNullOrWhiteSpace(buyerId)) return Results.Unauthorized();
                    if (string.IsNullOrWhiteSpace(request.PhoneNumber))
                        return Results.BadRequest(new { error = "phoneNumber is required." });

                    PhoneNumberValidation validation;
                    try
                    {
                        validation = await provider.ValidateDestinationAsync(
                            request.PhoneNumber.Trim(), request.CountryCode, cancellationToken);
                    }
                    catch (Exception) when (!cancellationToken.IsCancellationRequested)
                    {
                        return Results.Problem(
                            "The phone number could not be validated by the messaging provider.",
                            statusCode: StatusCodes.Status502BadGateway);
                    }

                    if (!validation.IsValid || string.IsNullOrWhiteSpace(validation.CanonicalNumber))
                    {
                        return Results.BadRequest(new
                        {
                            error = "The messaging provider does not consider this a valid destination.",
                            validationErrors = validation.ValidationErrors
                        });
                    }

                    var existing = await context.RegisteredContactNumbers.FirstOrDefaultAsync(contact =>
                        contact.BuyerId == buyerId &&
                        contact.CanonicalNumber == validation.CanonicalNumber &&
                        contact.RemovedAt == null,
                        cancellationToken);
                    if (existing is not null)
                    {
                        return Results.Ok(new ContactNumberCreatedResponse(existing.Id, existing.CanonicalNumber));
                    }

                    var contact = new RegisteredContactNumber(buyerId, validation.CanonicalNumber, timeProvider.GetUtcNow());
                    context.RegisteredContactNumbers.Add(contact);
                    try
                    {
                        await context.SaveChangesAsync(cancellationToken);
                    }
                    catch (DbUpdateException)
                    {
                        context.Entry(contact).State = EntityState.Detached;
                        existing = await context.RegisteredContactNumbers.SingleOrDefaultAsync(item =>
                            item.BuyerId == buyerId &&
                            item.CanonicalNumber == validation.CanonicalNumber &&
                            item.RemovedAt == null,
                            cancellationToken);
                        if (existing is null) throw;
                        return Results.Ok(new ContactNumberCreatedResponse(existing.Id, existing.CanonicalNumber));
                    }
                    return Results.Created(
                        $"/api/contact-numbers/{contact.Id}",
                        new ContactNumberCreatedResponse(contact.Id, contact.CanonicalNumber));
                })
            .Produces<ContactNumberCreatedResponse>(StatusCodes.Status201Created)
            .ProducesValidationProblem()
            .WithTags("ContactNumberEndpoints");
    }
}

public sealed class ListContactNumbersEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/contact-numbers",
                [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async (
                    ClaimsPrincipal principal,
                    CatalogContext context,
                    CancellationToken cancellationToken) =>
                {
                    var buyerId = principal.Identity?.Name;
                    if (string.IsNullOrWhiteSpace(buyerId)) return Results.Unauthorized();

                    var contacts = await context.RegisteredContactNumbers
                        .AsNoTracking()
                        .Where(contact => contact.BuyerId == buyerId && contact.RemovedAt == null)
                        .OrderBy(contact => contact.Id)
                        .Select(contact => new ContactNumberResponse(contact.Id, contact.CanonicalNumber, contact.CreatedAt))
                        .ToListAsync(cancellationToken);
                    return Results.Ok(contacts);
                })
            .Produces<ContactNumberResponse[]>()
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
                    ClaimsPrincipal principal,
                    CatalogContext context,
                    IOrderNotificationService notificationService,
                    TimeProvider timeProvider,
                    CancellationToken cancellationToken) =>
                {
                    var buyerId = principal.Identity?.Name;
                    if (string.IsNullOrWhiteSpace(buyerId)) return Results.Unauthorized();

                    var contact = await context.RegisteredContactNumbers.FirstOrDefaultAsync(item =>
                        item.Id == contactNumberId && item.BuyerId == buyerId && item.RemovedAt == null,
                        cancellationToken);
                    if (contact is null) return Results.NotFound();

                    contact.Remove(timeProvider.GetUtcNow());
                    var scheduled = await context.OrderNotifications.Where(notification =>
                        notification.ContactNumberId == contact.Id &&
                        notification.Kind == NotificationKind.DeliveryFollowUp &&
                        notification.ProviderStatus != NotificationDeliveryStatus.Canceled)
                        .ToListAsync(cancellationToken);
                    foreach (var notification in scheduled)
                    {
                        notification.RequestCancellation(timeProvider.GetUtcNow());
                    }
                    await context.SaveChangesAsync(cancellationToken);

                    try
                    {
                        await notificationService.CancelOutstandingScheduledMessagesAsync(cancellationToken);
                    }
                    catch
                    {
                        // Durable cancellation requests are retried by the cancellation worker.
                    }

                    return Results.NoContent();
                })
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status404NotFound)
            .WithTags("ContactNumberEndpoints");
    }
}
