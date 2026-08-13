using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using Microsoft.eShopWeb.PublicApi.NotificationEndpoints;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.ContactNumberEndpoints;

// ---------------------------------------------------------------------------------------------------
// Flow 1 — the shopper's contact number. All three endpoints are shopper-scoped: a caller only ever
// sees, uses or deletes their own numbers. A number the provider does not consider a usable destination
// is rejected here, and what gets stored is the provider's own canonical E.164 form.
// ---------------------------------------------------------------------------------------------------

/// <summary>POST api/contact-numbers — register a mobile number for the signed-in shopper.</summary>
public class RegisterContactNumberEndpoint
    : IEndpoint<IResult, RegisterContactNumberRequest, IRepository<ContactNumber>, ISmsProvider>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/contact-numbers",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (RegisterContactNumberRequest request, ClaimsPrincipal user,
             IRepository<ContactNumber> repository, ISmsProvider smsProvider) =>
            {
                var buyerId = EndpointHelpers.GetBuyerId(user);
                if (string.IsNullOrEmpty(buyerId))
                {
                    return Results.Unauthorized();
                }
                request.BuyerId = buyerId;
                return await HandleAsync(request, repository, smsProvider);
            })
            .Produces<RegisterContactNumberResponse>(StatusCodes.Status201Created)
            .Produces(StatusCodes.Status400BadRequest)
            .WithTags("ContactNumberEndpoints");
    }

    public async Task<IResult> HandleAsync(RegisterContactNumberRequest request,
        IRepository<ContactNumber> repository, ISmsProvider smsProvider)
    {
        if (string.IsNullOrWhiteSpace(request.PhoneNumber))
        {
            return Results.BadRequest(new { message = "A phone number is required." });
        }

        // Reject an unusable destination now, and store the provider's canonical form of the number.
        var lookup = await smsProvider.LookupAsync(request.PhoneNumber);
        if (!lookup.IsValid || string.IsNullOrEmpty(lookup.CanonicalNumber))
        {
            return Results.BadRequest(new
            {
                message = "The number is not a usable messaging destination.",
                validationErrors = lookup.ValidationErrors
            });
        }

        var contactNumber = new ContactNumber(request.BuyerId, lookup.CanonicalNumber);
        await repository.AddAsync(contactNumber);

        var response = new RegisterContactNumberResponse(contactNumber.Id, contactNumber.PhoneNumber);
        return Results.Created($"api/contact-numbers/{contactNumber.Id}", response);
    }
}

/// <summary>GET api/contact-numbers — the caller's registered numbers.</summary>
public class ListContactNumbersEndpoint : IEndpoint<IResult, ClaimsPrincipal, IReadRepository<ContactNumber>>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/contact-numbers",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ClaimsPrincipal user, IReadRepository<ContactNumber> repository) =>
                await HandleAsync(user, repository))
            .Produces<ListContactNumbersResponse>()
            .WithTags("ContactNumberEndpoints");
    }

    public async Task<IResult> HandleAsync(ClaimsPrincipal user, IReadRepository<ContactNumber> repository)
    {
        var buyerId = EndpointHelpers.GetBuyerId(user);
        if (string.IsNullOrEmpty(buyerId))
        {
            return Results.Unauthorized();
        }

        var numbers = await repository.ListAsync(new ContactNumbersByBuyerSpecification(buyerId));
        var response = new ListContactNumbersResponse(
            numbers.Select(n => new ContactNumberDto(n.Id, n.PhoneNumber, n.RegisteredAt)).ToList());
        return Results.Ok(response);
    }
}

/// <summary>DELETE api/contact-numbers/{contactNumberId} — remove one of the caller's numbers.</summary>
public class DeleteContactNumberEndpoint : IEndpoint<IResult, DeleteContactNumberRequest, IRepository<ContactNumber>>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapDelete("api/contact-numbers/{contactNumberId:int}",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int contactNumberId, ClaimsPrincipal user, IRepository<ContactNumber> repository) =>
            {
                var buyerId = EndpointHelpers.GetBuyerId(user);
                if (string.IsNullOrEmpty(buyerId))
                {
                    return Results.Unauthorized();
                }
                return await HandleAsync(new DeleteContactNumberRequest(contactNumberId, buyerId), repository);
            })
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status404NotFound)
            .WithTags("ContactNumberEndpoints");
    }

    public async Task<IResult> HandleAsync(DeleteContactNumberRequest request, IRepository<ContactNumber> repository)
    {
        var contactNumber = await repository.GetByIdAsync(request.ContactNumberId);

        // Another shopper's number must be indistinguishable from one that does not exist.
        if (contactNumber is null || contactNumber.BuyerId != request.BuyerId)
        {
            return Results.NotFound();
        }

        await repository.DeleteAsync(contactNumber);
        return Results.NoContent();
    }
}

// ----- DTOs -------------------------------------------------------------------------------------------

public class RegisterContactNumberRequest
{
    /// <summary>Set from the token, never from the request body.</summary>
    public string BuyerId { get; set; } = string.Empty;

    public string PhoneNumber { get; set; } = string.Empty;
}

public record RegisterContactNumberResponse(int ContactNumberId, string PhoneNumber);

public record DeleteContactNumberRequest(int ContactNumberId, string BuyerId);

public record ContactNumberDto(int ContactNumberId, string PhoneNumber, DateTimeOffset RegisteredAt);

public record ListContactNumbersResponse(IReadOnlyList<ContactNumberDto> ContactNumbers);
