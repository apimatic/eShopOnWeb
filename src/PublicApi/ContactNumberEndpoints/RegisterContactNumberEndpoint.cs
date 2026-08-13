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
/// Registers a mobile contact number for the signed-in shopper. The number is validated with the
/// provider up front; an unusable destination is rejected here rather than at send time, and the
/// provider's canonical form is what gets stored.
/// </summary>
public class RegisterContactNumberEndpoint : IEndpoint<IResult, RegisterContactNumberRequest, ISmsSender, IRepository<ContactNumber>>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/contact-numbers",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (RegisterContactNumberRequest request, ClaimsPrincipal user, ISmsSender smsSender, IRepository<ContactNumber> repository) =>
            {
                var buyerId = user.GetUserName();
                if (string.IsNullOrEmpty(buyerId))
                    return Results.Unauthorized();

                request.BuyerId = buyerId;
                return await HandleAsync(request, smsSender, repository);
            })
            .Produces<RegisterContactNumberResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .WithTags("ContactNumberEndpoints");
    }

    public async Task<IResult> HandleAsync(RegisterContactNumberRequest request, ISmsSender smsSender, IRepository<ContactNumber> repository)
    {
        if (string.IsNullOrWhiteSpace(request.PhoneNumber))
            return Results.BadRequest(new { error = "A phone number is required." });

        var validation = await smsSender.ValidateNumberAsync(request.PhoneNumber);
        if (!validation.IsValid || string.IsNullOrEmpty(validation.CanonicalPhoneNumber))
            return Results.BadRequest(new { error = validation.Reason ?? "The phone number is not a valid, reachable destination." });

        var canonical = validation.CanonicalPhoneNumber;

        // Idempotent: registering a number already on file for this shopper returns the existing record.
        var existing = await repository.ListAsync(new ContactNumbersByBuyerSpecification(request.BuyerId!));
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

        var contactNumber = new ContactNumber(request.BuyerId!, canonical);
        contactNumber = await repository.AddAsync(contactNumber);

        var response = new RegisterContactNumberResponse(request.CorrelationId())
        {
            ContactNumberId = contactNumber.Id,
            PhoneNumber = contactNumber.PhoneNumber
        };
        return Results.Created($"api/contact-numbers/{contactNumber.Id}", response);
    }
}

public class RegisterContactNumberRequest : BaseRequest
{
    /// <summary>The number the caller typed, in any provider-parseable form.</summary>
    public string PhoneNumber { get; set; } = string.Empty;

    /// <summary>Owner, taken from the token — not from the request body.</summary>
    internal string? BuyerId { get; set; }
}

public class RegisterContactNumberResponse : BaseResponse
{
    public RegisterContactNumberResponse(System.Guid correlationId) : base(correlationId) { }
    public RegisterContactNumberResponse() { }

    public int ContactNumberId { get; set; }
    public string PhoneNumber { get; set; } = string.Empty;
}
