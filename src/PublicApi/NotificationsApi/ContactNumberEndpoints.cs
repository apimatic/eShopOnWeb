using System.Linq;
using System.Security.Claims;
using System.Threading;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;
using Swashbuckle.AspNetCore.Annotations;

namespace Microsoft.eShopWeb.PublicApi.NotificationsApi;

// ------------------------------------------------------------------------------------
// Flow 1 — the shopper's contact number. All three endpoints are shopper-scoped: a
// shopper only ever sees, uses or removes their own numbers.
// ------------------------------------------------------------------------------------

public record RegisterContactNumberRequest(string PhoneNumber);

public record ContactNumberDto(int ContactNumberId, string PhoneNumber, string CreatedDate);

/// <summary>POST /api/contact-numbers — register a mobile number for the signed-in shopper.</summary>
public class RegisterContactNumberEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/contact-numbers",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (RegisterContactNumberRequest request, ClaimsPrincipal user,
                   IContactNumberService service, CancellationToken ct) =>
            {
                var ownerId = user.GetUserId();
                if (string.IsNullOrEmpty(ownerId)) return Results.Unauthorized();

                var result = await service.RegisterAsync(ownerId, request?.PhoneNumber ?? string.Empty, ct);
                if (!result.Succeeded)
                {
                    // Rejected here rather than at send time. 422: the number is not a usable destination.
                    return Results.UnprocessableEntity(new { error = result.RejectionReason });
                }

                var c = result.ContactNumber!;
                return Results.Created($"api/contact-numbers/{c.Id}",
                    new { contactNumberId = c.Id, phoneNumber = c.PhoneNumber });
            })
            .Produces(StatusCodes.Status201Created)
            .Produces(StatusCodes.Status422UnprocessableEntity)
            .WithTags("ContactNumberEndpoints")
            .WithMetadata(new SwaggerOperationAttribute("Register a mobile number for the signed-in shopper"));
    }
}

/// <summary>GET /api/contact-numbers — the caller's registered numbers.</summary>
public class ListContactNumbersEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/contact-numbers",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (ClaimsPrincipal user, IContactNumberService service, CancellationToken ct) =>
            {
                var ownerId = user.GetUserId();
                if (string.IsNullOrEmpty(ownerId)) return Results.Unauthorized();

                var numbers = await service.ListAsync(ownerId, ct);
                var dtos = numbers
                    .Select(n => new ContactNumberDto(n.Id, n.PhoneNumber, n.CreatedDate.ToString("o")))
                    .ToList();
                return Results.Ok(new { contactNumbers = dtos });
            })
            .Produces(StatusCodes.Status200OK)
            .WithTags("ContactNumberEndpoints")
            .WithMetadata(new SwaggerOperationAttribute("List the caller's registered numbers"));
    }
}

/// <summary>DELETE /api/contact-numbers/{contactNumberId} — remove one of the caller's numbers.</summary>
public class DeleteContactNumberEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapDelete("api/contact-numbers/{contactNumberId:int}",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (int contactNumberId, ClaimsPrincipal user,
                   IContactNumberService service, CancellationToken ct) =>
            {
                var ownerId = user.GetUserId();
                if (string.IsNullOrEmpty(ownerId)) return Results.Unauthorized();

                var removed = await service.RemoveAsync(ownerId, contactNumberId, ct);
                return removed ? Results.NoContent() : Results.NotFound();
            })
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status404NotFound)
            .WithTags("ContactNumberEndpoints")
            .WithMetadata(new SwaggerOperationAttribute("Remove one of the caller's numbers"));
    }
}
