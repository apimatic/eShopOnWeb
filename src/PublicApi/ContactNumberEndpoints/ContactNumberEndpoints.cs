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
using Microsoft.eShopWeb.Infrastructure.Services;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.ContactNumberEndpoints;

public class ContactNumberEndpoints : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/contact-numbers", RegisterAsync)
            .RequireAuthorization(ShopperPolicy())
            .Produces(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .WithTags("ContactNumberEndpoints");

        app.MapGet("api/contact-numbers", ListAsync)
            .RequireAuthorization(ShopperPolicy())
            .WithTags("ContactNumberEndpoints");

        app.MapDelete("api/contact-numbers/{contactNumberId:int}", DeleteAsync)
            .RequireAuthorization(ShopperPolicy())
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .WithTags("ContactNumberEndpoints");
    }

    private static async Task<IResult> RegisterAsync(
        RegisterContactNumberRequest request,
        ClaimsPrincipal user,
        OrderNotificationService service,
        CancellationToken cancellationToken)
    {
        try
        {
            var contact = await service.RegisterContactNumberAsync(UserName(user), request.PhoneNumber, cancellationToken);
            return Results.Created($"/api/contact-numbers/{contact.Id}", new
            {
                contactNumberId = contact.Id,
                phoneNumber = contact.PhoneNumber
            });
        }
        catch (Exception exception)
        {
            return EndpointProblem.From(exception);
        }
    }

    private static async Task<IResult> ListAsync(
        ClaimsPrincipal user,
        OrderNotificationService service,
        CancellationToken cancellationToken)
    {
        var contacts = await service.GetContactNumbersAsync(UserName(user), cancellationToken);
        return Results.Ok(contacts.Select(x => new
        {
            contactNumberId = x.Id,
            phoneNumber = x.PhoneNumber,
            createdAt = x.CreatedAt
        }));
    }

    private static async Task<IResult> DeleteAsync(
        int contactNumberId,
        ClaimsPrincipal user,
        OrderNotificationService service,
        CancellationToken cancellationToken)
    {
        try
        {
            await service.DeleteContactNumberAsync(UserName(user), contactNumberId, cancellationToken);
            return Results.NoContent();
        }
        catch (Exception exception)
        {
            return EndpointProblem.From(exception);
        }
    }

    private static AuthorizeAttribute ShopperPolicy() => new()
    {
        AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme
    };

    private static string UserName(ClaimsPrincipal user) =>
        user.Identity?.Name ?? throw new UnauthorizedAccessException();
}

public sealed record RegisterContactNumberRequest(string PhoneNumber);
