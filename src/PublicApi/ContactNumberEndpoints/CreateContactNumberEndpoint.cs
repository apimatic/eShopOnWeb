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
using Microsoft.eShopWeb.PublicApi.NotificationsFeature;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.ContactNumberEndpoints;

/// <summary>Registers a mobile number for the signed-in shopper.</summary>
public class CreateContactNumberEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/contact-numbers",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreateContactNumberRequest request, ClaimsPrincipal user, ISmsGateway smsGateway, IRepository<ContactNumber> repository) =>
                await HandleAsync(request, user, smsGateway, repository))
            .Produces<CreateContactNumberResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .WithTags("ContactNumberEndpoints");
    }

    public static async Task<IResult> HandleAsync(
        CreateContactNumberRequest request,
        ClaimsPrincipal user,
        ISmsGateway smsGateway,
        IRepository<ContactNumber> repository)
    {
        var buyerId = user.GetBuyerId();
        if (string.IsNullOrEmpty(buyerId))
        {
            return Results.Unauthorized();
        }

        if (string.IsNullOrWhiteSpace(request.PhoneNumber))
        {
            return Results.Problem("A phone number is required.", statusCode: StatusCodes.Status400BadRequest);
        }

        // Reject a number the provider does not consider a usable destination here, at
        // registration, rather than when a message later fails to go out. Store the provider's
        // own canonical form of it, not whatever the caller typed.
        var lookup = await smsGateway.ValidateAndCanonicalizeAsync(request.PhoneNumber);
        if (!lookup.IsValid || string.IsNullOrEmpty(lookup.CanonicalE164))
        {
            return Results.Problem("The phone number is not a usable SMS destination.", statusCode: StatusCodes.Status400BadRequest);
        }

        var canonical = lookup.CanonicalE164!;
        var existingForOwner = await repository.ListAsync(new ContactNumbersByOwnerSpecification(buyerId));
        var duplicate = existingForOwner.FirstOrDefault(c => c.PhoneNumber == canonical);
        if (duplicate is not null)
        {
            return Results.Ok(new CreateContactNumberResponse(duplicate.Id, duplicate.PhoneNumber));
        }

        var contactNumber = new ContactNumber(buyerId, canonical);
        contactNumber = await repository.AddAsync(contactNumber);

        var response = new CreateContactNumberResponse(contactNumber.Id, contactNumber.PhoneNumber);
        return Results.Created($"api/contact-numbers/{contactNumber.Id}", response);
    }
}

/// <summary>The mobile number to register, as the caller typed it.</summary>
public record CreateContactNumberRequest(string PhoneNumber);

/// <summary>Carries the new number's identifier as a top-level field, plus its canonical form.</summary>
public record CreateContactNumberResponse(int ContactNumberId, string PhoneNumber);
