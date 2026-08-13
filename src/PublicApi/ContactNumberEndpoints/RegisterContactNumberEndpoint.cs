using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.ContactNumberAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.ContactNumberEndpoints;

/// <summary>
/// Registers a mobile number for the signed-in shopper. The provider must consider it a usable
/// destination (rejected here, not when a later message fails), and what is stored is the provider's
/// own canonical form of the number, not whatever the caller typed.
/// </summary>
public class RegisterContactNumberEndpoint : IEndpoint<IResult, RegisterContactNumberRequest, ISmsGateway, IRepository<ContactNumber>>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/contact-numbers",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (RegisterContactNumberRequest request, System.Security.Claims.ClaimsPrincipal user,
                ISmsGateway sms, IRepository<ContactNumber> repository) =>
            {
                var owner = CallerIdentity.GetUserName(user);
                if (string.IsNullOrEmpty(owner))
                {
                    return Results.Unauthorized();
                }
                request.OwnerId = owner;
                return await HandleAsync(request, sms, repository);
            })
            .Produces<RegisterContactNumberResponse>(StatusCodes.Status201Created)
            .Produces(StatusCodes.Status400BadRequest)
            .WithTags("ContactNumberEndpoints");
    }

    public async Task<IResult> HandleAsync(RegisterContactNumberRequest request, ISmsGateway sms,
        IRepository<ContactNumber> repository)
    {
        var response = new RegisterContactNumberResponse(request.CorrelationId());

        if (string.IsNullOrWhiteSpace(request.PhoneNumber))
        {
            return Results.BadRequest("A phone number is required.");
        }

        var lookup = await sms.LookupAsync(request.PhoneNumber);
        if (!lookup.IsValid || string.IsNullOrEmpty(lookup.CanonicalNumber))
        {
            // Reject an unusable destination up front, not when a message later fails to go out.
            return Results.BadRequest(new
            {
                Message = "The number is not a usable destination.",
                ValidationErrors = lookup.ValidationErrors
            });
        }

        var canonical = lookup.CanonicalNumber!;

        // A shopper re-registering a number they already have on file simply gets that registration back.
        var existing = await repository.FirstOrDefaultAsync(
            new ContactNumberByOwnerAndNumberSpecification(request.OwnerId, canonical));
        if (existing is not null)
        {
            response.ContactNumberId = existing.Id;
            response.PhoneNumber = existing.PhoneNumber;
            return Results.Ok(response);
        }

        var contactNumber = new ContactNumber(request.OwnerId, canonical);
        contactNumber = await repository.AddAsync(contactNumber);

        response.ContactNumberId = contactNumber.Id;
        response.PhoneNumber = contactNumber.PhoneNumber;
        return Results.Created($"api/contact-numbers/{contactNumber.Id}", response);
    }
}

public class RegisterContactNumberRequest : BaseRequest
{
    /// <summary>The number to register, in any form the provider can canonicalise.</summary>
    public string PhoneNumber { get; set; } = string.Empty;

    /// <summary>The signed-in shopper; set from the token, ignored if supplied by the caller.</summary>
    public string OwnerId { get; set; } = string.Empty;
}

public class RegisterContactNumberResponse : BaseResponse
{
    public RegisterContactNumberResponse(Guid correlationId) : base(correlationId) { }

    /// <summary>Identifier of the registered number, returned as a top-level field.</summary>
    public int ContactNumberId { get; set; }

    /// <summary>The provider's canonical E.164 form that was stored.</summary>
    public string PhoneNumber { get; set; } = string.Empty;
}
