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
using Microsoft.eShopWeb.PublicApi.Notifications;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.ContactNumberEndpoints;

/// <summary>
/// Registers a mobile number for the signed-in shopper. The number is validated with the provider and
/// its canonical E.164 form is what gets stored; a number the provider does not consider a usable
/// destination is rejected here, not later when a message fails to go out.
/// </summary>
public class RegisterContactNumberEndpoint : IEndpoint<IResult, RegisterContactNumberRequest, ClaimsPrincipal>
{
    private readonly ITwilioMessagingClient _twilio;
    private readonly IRepository<ContactNumber> _repository;

    public RegisterContactNumberEndpoint(ITwilioMessagingClient twilio, IRepository<ContactNumber> repository)
    {
        _twilio = twilio;
        _repository = repository;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/contact-numbers",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (RegisterContactNumberRequest request, ClaimsPrincipal user) => await HandleAsync(request, user))
            .Produces<RegisterContactNumberResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .WithTags("ContactNumberEndpoints");
    }

    public async Task<IResult> HandleAsync(RegisterContactNumberRequest request, ClaimsPrincipal user)
    {
        var buyerId = user.GetBuyerId();
        if (string.IsNullOrEmpty(buyerId))
            return Results.Unauthorized();

        if (string.IsNullOrWhiteSpace(request.PhoneNumber))
            return Results.BadRequest(new { error = "A phone number is required." });

        var lookup = await _twilio.LookupAsync(request.PhoneNumber.Trim());
        if (!lookup.Valid || string.IsNullOrEmpty(lookup.PhoneNumber))
        {
            return Results.BadRequest(new
            {
                error = "The number is not a usable destination and was not registered.",
                validationErrors = lookup.ValidationErrors
            });
        }

        var canonical = lookup.PhoneNumber;

        // Don't store the same number twice for a shopper; return the existing registration if present.
        var existing = await _repository.ListAsync(new ContactNumbersByBuyerSpecification(buyerId));
        var already = existing.FirstOrDefault(c => c.PhoneNumber == canonical);
        if (already is not null)
        {
            var existingResponse = new RegisterContactNumberResponse(request.CorrelationId())
            {
                ContactNumberId = already.Id,
                PhoneNumber = already.PhoneNumber
            };
            return Results.Ok(existingResponse);
        }

        var contactNumber = new ContactNumber(buyerId, canonical);
        contactNumber = await _repository.AddAsync(contactNumber);

        var response = new RegisterContactNumberResponse(request.CorrelationId())
        {
            ContactNumberId = contactNumber.Id,
            PhoneNumber = contactNumber.PhoneNumber
        };
        return Results.Created($"api/contact-numbers/{contactNumber.Id}", response);
    }
}
