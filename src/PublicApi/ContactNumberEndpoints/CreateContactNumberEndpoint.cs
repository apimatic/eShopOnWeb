using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.ContactNumberEndpoints;

public class CreateContactNumberEndpoint : IEndpoint<IResult, CreateContactNumberRequest, IContactNumberService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/contact-numbers",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (CreateContactNumberRequest request, HttpContext httpContext, IContactNumberService contactNumbers) =>
            {
                return await HandleAsync(request with { BuyerId = httpContext.User.GetRequiredBuyerId() }, contactNumbers);
            })
            .Produces<CreateContactNumberResponse>(StatusCodes.Status201Created)
            .Produces(StatusCodes.Status400BadRequest)
            .WithTags("ContactNumberEndpoints");
    }

    public async Task<IResult> HandleAsync(CreateContactNumberRequest request, IContactNumberService contactNumbers)
    {
        if (string.IsNullOrWhiteSpace(request.PhoneNumber))
        {
            return Results.BadRequest(new { message = "phoneNumber is required." });
        }

        try
        {
            var created = await contactNumbers.RegisterAsync(request.BuyerId, request.PhoneNumber);
            var response = new CreateContactNumberResponse
            {
                ContactNumberId = created.Id,
                PhoneNumber = created.PhoneNumber
            };
            return Results.Created($"api/contact-numbers/{created.Id}", response);
        }
        catch (ContactNumberAlreadyRegisteredException ex)
        {
            return Results.Ok(new CreateContactNumberResponse
            {
                ContactNumberId = ex.Existing.Id,
                PhoneNumber = ex.Existing.PhoneNumber
            });
        }
    }
}

public record CreateContactNumberRequest(string PhoneNumber)
{
    public string BuyerId { get; init; } = string.Empty;
}

public class CreateContactNumberResponse
{
    public int ContactNumberId { get; set; }
    public string PhoneNumber { get; set; } = string.Empty;
}
