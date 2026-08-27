using System;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using Microsoft.eShopWeb.ApplicationCore.Twilio;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.ContactNumberEndpoints;

/// <summary>
/// Registers a mobile number for the signed-in shopper. The number is validated
/// through the provider's lookup API and stored in the provider's canonical form.
/// </summary>
public class RegisterContactNumberEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/contact-numbers",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (RegisterContactNumberRequest request, ClaimsPrincipal user, IRepository<ContactNumber> contactNumberRepository, ITwilioLookupClient lookupClient) =>
            {
                return await HandleAsync(request, user, contactNumberRepository, lookupClient);
            })
            .Produces<RegisterContactNumberResponse>(StatusCodes.Status201Created)
            .Produces(StatusCodes.Status400BadRequest)
            .WithTags("ContactNumberEndpoints");
    }

    public async Task<IResult> HandleAsync(RegisterContactNumberRequest request, ClaimsPrincipal user, IRepository<ContactNumber> contactNumberRepository, ITwilioLookupClient lookupClient)
    {
        var buyerId = user.Identity?.Name;
        if (string.IsNullOrEmpty(buyerId))
        {
            return Results.Unauthorized();
        }
        if (string.IsNullOrWhiteSpace(request.PhoneNumber))
        {
            return Results.BadRequest(new { message = "A phone number is required." });
        }

        TwilioLookupResult lookup;
        try
        {
            lookup = await lookupClient.FetchPhoneNumberAsync(request.PhoneNumber);
        }
        catch (TwilioApiException)
        {
            return Results.Problem("The phone number validation provider could not be reached.", statusCode: StatusCodes.Status502BadGateway);
        }

        if (!lookup.Valid || string.IsNullOrEmpty(lookup.PhoneNumber))
        {
            return Results.BadRequest(new { message = "The phone number is not a usable destination." });
        }

        var existing = await contactNumberRepository.ListAsync(new ContactNumbersByBuyerSpecification(buyerId));
        if (existing.Any(c => c.PhoneNumber == lookup.PhoneNumber))
        {
            return Results.Conflict(new { message = "This number is already registered." });
        }

        var contactNumber = await contactNumberRepository.AddAsync(new ContactNumber(buyerId, lookup.PhoneNumber));

        var response = new RegisterContactNumberResponse(request.CorrelationId())
        {
            ContactNumberId = contactNumber.Id,
            PhoneNumber = contactNumber.PhoneNumber,
            CreatedAt = contactNumber.CreatedAt
        };
        return Results.Created($"api/contact-numbers/{contactNumber.Id}", response);
    }
}
