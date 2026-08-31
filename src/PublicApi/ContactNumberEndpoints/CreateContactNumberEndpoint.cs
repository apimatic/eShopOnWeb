using System.Linq;
using System.Security.Claims;
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
/// Registers a mobile number for the signed-in shopper. The number is validated with the
/// messaging provider first; what gets stored is the provider's canonical form of it.
/// </summary>
public class CreateContactNumberEndpoint : IEndpoint<IResult, CreateContactNumberRequest, ClaimsPrincipal, IRepository<ContactNumber>>
{
    private readonly ISmsProvider _smsProvider;

    public CreateContactNumberEndpoint(ISmsProvider smsProvider)
    {
        _smsProvider = smsProvider;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/contact-numbers",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreateContactNumberRequest request, ClaimsPrincipal user, IRepository<ContactNumber> contactNumberRepository) =>
            {
                return await HandleAsync(request, user, contactNumberRepository);
            })
            .Produces<CreateContactNumberResponse>()
            .WithTags("ContactNumberEndpoints");
    }

    public async Task<IResult> HandleAsync(CreateContactNumberRequest request, ClaimsPrincipal user, IRepository<ContactNumber> contactNumberRepository)
    {
        var buyerId = user.GetBuyerId();

        if (string.IsNullOrWhiteSpace(request.PhoneNumber))
        {
            return Results.BadRequest("A phone number is required.");
        }

        var validation = await _smsProvider.ValidatePhoneNumberAsync(request.PhoneNumber);
        if (!validation.IsValid || validation.CanonicalNumber is null)
        {
            return Results.BadRequest(validation.ValidationError ?? "The phone number is not a usable destination.");
        }

        var existing = await contactNumberRepository.ListAsync(new ContactNumbersByBuyerSpecification(buyerId));
        var duplicate = existing.FirstOrDefault(c => c.PhoneNumber == validation.CanonicalNumber);
        if (duplicate is not null)
        {
            var existingResponse = new CreateContactNumberResponse(request.CorrelationId())
            {
                ContactNumberId = duplicate.Id,
                PhoneNumber = duplicate.PhoneNumber
            };
            return Results.Ok(existingResponse);
        }

        var contactNumber = await contactNumberRepository.AddAsync(new ContactNumber(buyerId, validation.CanonicalNumber));

        var response = new CreateContactNumberResponse(request.CorrelationId())
        {
            ContactNumberId = contactNumber.Id,
            PhoneNumber = contactNumber.PhoneNumber
        };
        return Results.Created($"api/contact-numbers/{contactNumber.Id}", response);
    }
}
