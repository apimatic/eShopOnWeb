using System.Collections.Generic;
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

namespace Microsoft.eShopWeb.PublicApi.ContactNumberEndpoints;

// Contact-number endpoints are shopper-scoped: every one acts only on the caller's own numbers.

/// <summary>Registers a mobile number for the signed-in shopper (rejecting unusable numbers up front).</summary>
public class RegisterContactNumberEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/contact-numbers",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async (
                RegisterContactNumberRequest request,
                ClaimsPrincipal user,
                IContactNumberService service,
                CancellationToken cancellationToken) =>
            {
                var ownerId = user.GetUserId();
                if (string.IsNullOrEmpty(ownerId))
                {
                    return Results.Unauthorized();
                }
                if (request is null || string.IsNullOrWhiteSpace(request.Number))
                {
                    return Results.BadRequest(new ContactNumberErrorResponse(new[] { "A phone number is required." }));
                }

                var result = await service.RegisterAsync(ownerId, request.Number, cancellationToken);
                if (!result.Succeeded)
                {
                    return Results.BadRequest(new ContactNumberErrorResponse(result.ValidationErrors));
                }

                var contactNumber = result.ContactNumber!;
                return Results.Created($"api/contact-numbers/{contactNumber.Id}",
                    new RegisterContactNumberResponse(contactNumber.Id, contactNumber.E164Number));
            })
            .Produces<RegisterContactNumberResponse>(StatusCodes.Status201Created)
            .Produces<ContactNumberErrorResponse>(StatusCodes.Status400BadRequest)
            .WithTags("ContactNumberEndpoints");
    }
}

/// <summary>Lists the caller's registered contact numbers.</summary>
public class ListContactNumbersEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/contact-numbers",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async (
                ClaimsPrincipal user,
                IContactNumberService service,
                CancellationToken cancellationToken) =>
            {
                var ownerId = user.GetUserId();
                if (string.IsNullOrEmpty(ownerId))
                {
                    return Results.Unauthorized();
                }

                var numbers = await service.ListAsync(ownerId, cancellationToken);
                var response = new ListContactNumbersResponse(
                    numbers.Select(n => new ContactNumberDto(n.Id, n.E164Number)).ToList());
                return Results.Ok(response);
            })
            .Produces<ListContactNumbersResponse>()
            .WithTags("ContactNumberEndpoints");
    }
}

/// <summary>Removes one of the caller's contact numbers; afterwards nothing is ever sent to it again.</summary>
public class DeleteContactNumberEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapDelete("api/contact-numbers/{contactNumberId:int}",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async (
                int contactNumberId,
                ClaimsPrincipal user,
                IContactNumberService service,
                CancellationToken cancellationToken) =>
            {
                var ownerId = user.GetUserId();
                if (string.IsNullOrEmpty(ownerId))
                {
                    return Results.Unauthorized();
                }

                var deleted = await service.DeleteAsync(ownerId, contactNumberId, cancellationToken);
                return deleted ? Results.NoContent() : Results.NotFound();
            })
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status404NotFound)
            .WithTags("ContactNumberEndpoints");
    }
}

public record RegisterContactNumberRequest(string Number);
public record RegisterContactNumberResponse(int ContactNumberId, string E164Number);
public record ContactNumberDto(int ContactNumberId, string E164Number);
public record ListContactNumbersResponse(IReadOnlyList<ContactNumberDto> ContactNumbers);
public record ContactNumberErrorResponse(IReadOnlyList<string> Errors);
