using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;
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

// ----- DTOs -----

public class RegisterContactNumberRequest
{
    /// <summary>The mobile number as the shopper typed it (any format the provider can parse).</summary>
    public string PhoneNumber { get; set; } = string.Empty;

    /// <summary>Owner (token name claim), assigned server-side — never bound from the body.</summary>
    [JsonIgnore]
    public string OwnerId { get; set; } = string.Empty;
}

public record RegisterContactNumberResponse(int ContactNumberId, string PhoneNumber);

public record ContactNumberDto(int ContactNumberId, string PhoneNumber, System.DateTimeOffset RegisteredAt);

public record ContactNumberListResponse(IReadOnlyList<ContactNumberDto> ContactNumbers);

// ----- Endpoints -----

/// <summary>POST /api/contact-numbers — register a mobile number for the signed-in shopper.</summary>
public class ContactNumberRegisterEndpoint
    : IEndpoint<IResult, RegisterContactNumberRequest, IContactNumberService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/contact-numbers",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (RegisterContactNumberRequest request, HttpContext http, IContactNumberService service,
             CancellationToken ct) =>
            {
                var owner = http.GetUserName();
                if (string.IsNullOrEmpty(owner))
                {
                    return Results.Unauthorized();
                }
                request.OwnerId = owner;
                return await Execute(request, service, ct);
            })
            .Produces<RegisterContactNumberResponse>(StatusCodes.Status201Created)
            .WithTags("ContactNumberEndpoints");
    }

    public Task<IResult> HandleAsync(RegisterContactNumberRequest request, IContactNumberService service)
        => Execute(request, service, CancellationToken.None);

    private static async Task<IResult> Execute(RegisterContactNumberRequest request,
        IContactNumberService service, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.PhoneNumber))
        {
            return Results.BadRequest(new { message = "A phone number is required." });
        }

        var result = await service.RegisterAsync(request.OwnerId, request.PhoneNumber, ct);
        return result.Outcome switch
        {
            RegisterContactNumberOutcome.Registered => Results.Created(
                $"api/contact-numbers/{result.ContactNumberId}",
                new RegisterContactNumberResponse(result.ContactNumberId!.Value, result.CanonicalNumber!)),
            RegisterContactNumberOutcome.RejectedUnusable => Results.BadRequest(new { message = result.Message }),
            _ => Results.Json(new { message = result.Message }, statusCode: StatusCodes.Status502BadGateway)
        };
    }
}

/// <summary>GET /api/contact-numbers — the caller's registered numbers.</summary>
public class ContactNumberListEndpoint
    : IEndpoint<IResult, ContactNumberListRequest, IContactNumberService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/contact-numbers",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (HttpContext http, IContactNumberService service, CancellationToken ct) =>
            {
                var owner = http.GetUserName();
                if (string.IsNullOrEmpty(owner))
                {
                    return Results.Unauthorized();
                }
                return await Execute(new ContactNumberListRequest { OwnerId = owner }, service, ct);
            })
            .Produces<ContactNumberListResponse>()
            .WithTags("ContactNumberEndpoints");
    }

    public Task<IResult> HandleAsync(ContactNumberListRequest request, IContactNumberService service)
        => Execute(request, service, CancellationToken.None);

    private static async Task<IResult> Execute(ContactNumberListRequest request,
        IContactNumberService service, CancellationToken ct)
    {
        var numbers = await service.ListAsync(request.OwnerId, ct);
        var dtos = numbers
            .Select(n => new ContactNumberDto(n.Id, n.E164Number, n.RegisteredAt))
            .ToList();
        return Results.Ok(new ContactNumberListResponse(dtos));
    }
}

public class ContactNumberListRequest
{
    [JsonIgnore]
    public string OwnerId { get; set; } = string.Empty;
}

/// <summary>DELETE /api/contact-numbers/{contactNumberId} — remove one of the caller's numbers.</summary>
public class ContactNumberDeleteEndpoint
    : IEndpoint<IResult, ContactNumberDeleteRequest, IContactNumberService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapDelete("api/contact-numbers/{contactNumberId}",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int contactNumberId, HttpContext http, IContactNumberService service, CancellationToken ct) =>
            {
                var owner = http.GetUserName();
                if (string.IsNullOrEmpty(owner))
                {
                    return Results.Unauthorized();
                }
                return await Execute(
                    new ContactNumberDeleteRequest { OwnerId = owner, ContactNumberId = contactNumberId }, service, ct);
            })
            .WithTags("ContactNumberEndpoints");
    }

    public Task<IResult> HandleAsync(ContactNumberDeleteRequest request, IContactNumberService service)
        => Execute(request, service, CancellationToken.None);

    private static async Task<IResult> Execute(ContactNumberDeleteRequest request,
        IContactNumberService service, CancellationToken ct)
    {
        var removed = await service.DeleteAsync(request.OwnerId, request.ContactNumberId, ct);
        return removed ? Results.NoContent() : Results.NotFound();
    }
}

public class ContactNumberDeleteRequest
{
    public int ContactNumberId { get; set; }

    [JsonIgnore]
    public string OwnerId { get; set; } = string.Empty;
}
