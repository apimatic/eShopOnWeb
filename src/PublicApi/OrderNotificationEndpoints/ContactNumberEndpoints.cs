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
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderNotificationEndpoints;

public sealed class RegisterContactNumberEndpoint : IEndpoint<IResult, RegisterContactNumberRequest, ClaimsPrincipal>
{
    private readonly IOrderNotificationService _notifications;

    public RegisterContactNumberEndpoint(IOrderNotificationService notifications) => _notifications = notifications;

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/contact-numbers",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (RegisterContactNumberRequest request, ClaimsPrincipal user, CancellationToken cancellationToken) =>
                await HandleAsync(request, user, cancellationToken))
            .Produces(StatusCodes.Status201Created)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status409Conflict)
            .Produces(StatusCodes.Status502BadGateway)
            .WithTags("OrderNotificationEndpoints");
    }

    public async Task<IResult> HandleAsync(
        RegisterContactNumberRequest request,
        ClaimsPrincipal user,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.PhoneNumber))
        {
            return Results.BadRequest(new { error = "phoneNumber is required." });
        }

        var result = await _notifications.RegisterContactAsync(BuyerId(user), request.PhoneNumber, cancellationToken);
        return result.Outcome switch
        {
            ContactRegistrationOutcome.Created => Results.Created(
                $"/api/contact-numbers/{result.ContactNumber!.Id}",
                new { contactNumberId = result.ContactNumber.Id, phoneNumber = result.ContactNumber.CanonicalNumber }),
            ContactRegistrationOutcome.Duplicate => Results.Conflict(new { error = result.Error }),
            ContactRegistrationOutcome.Invalid => Results.BadRequest(new { error = result.Error }),
            _ => Results.Json(new { error = result.Error }, statusCode: StatusCodes.Status502BadGateway)
        };
    }

    public Task<IResult> HandleAsync(RegisterContactNumberRequest request, ClaimsPrincipal user) =>
        HandleAsync(request, user, CancellationToken.None);

    internal static string BuyerId(ClaimsPrincipal user) =>
        user.Identity?.Name ?? throw new InvalidOperationException("The authenticated token has no name claim.");
}

public sealed class ListContactNumbersEndpoint : IEndpoint<IResult, ClaimsPrincipal>
{
    private readonly IOrderNotificationService _notifications;

    public ListContactNumbersEndpoint(IOrderNotificationService notifications) => _notifications = notifications;

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/contact-numbers",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ClaimsPrincipal user, CancellationToken cancellationToken) => await HandleAsync(user, cancellationToken))
            .Produces(StatusCodes.Status200OK)
            .WithTags("OrderNotificationEndpoints");
    }

    public async Task<IResult> HandleAsync(ClaimsPrincipal user, CancellationToken cancellationToken = default)
    {
        var contacts = await _notifications.ListContactsAsync(RegisterContactNumberEndpoint.BuyerId(user), cancellationToken);
        return Results.Ok(new
        {
            contactNumbers = contacts.Select(x => new
            {
                contactNumberId = x.Id,
                phoneNumber = x.CanonicalNumber,
                createdAt = x.CreatedAt
            })
        });
    }

    public Task<IResult> HandleAsync(ClaimsPrincipal user) => HandleAsync(user, CancellationToken.None);
}

public sealed class DeleteContactNumberEndpoint : IEndpoint<IResult, int, ClaimsPrincipal>
{
    private readonly IOrderNotificationService _notifications;

    public DeleteContactNumberEndpoint(IOrderNotificationService notifications) => _notifications = notifications;

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapDelete("api/contact-numbers/{contactNumberId:int}",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int contactNumberId, ClaimsPrincipal user, CancellationToken cancellationToken) =>
                await HandleAsync(contactNumberId, user, cancellationToken))
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status502BadGateway)
            .WithTags("OrderNotificationEndpoints");
    }

    public async Task<IResult> HandleAsync(
        int contactNumberId,
        ClaimsPrincipal user,
        CancellationToken cancellationToken = default)
    {
        var result = await _notifications.DeleteContactAsync(
            RegisterContactNumberEndpoint.BuyerId(user),
            contactNumberId,
            cancellationToken);
        return result.Outcome switch
        {
            ContactDeletionOutcome.Deleted => Results.NoContent(),
            ContactDeletionOutcome.NotFound => Results.NotFound(),
            _ => Results.Json(
                new { error = "The provider could not confirm cancellation of scheduled messages; the number was not removed." },
                statusCode: StatusCodes.Status502BadGateway)
        };
    }

    public Task<IResult> HandleAsync(int contactNumberId, ClaimsPrincipal user) =>
        HandleAsync(contactNumberId, user, CancellationToken.None);
}
