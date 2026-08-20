using System.Threading;
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

public class CreateContactNumberEndpoint : IEndpoint<IResult, CreateContactNumberRequest>
{
    private readonly IContactNumberService _contactNumbers;

    public CreateContactNumberEndpoint(IContactNumberService contactNumbers)
    {
        _contactNumbers = contactNumbers;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/contact-numbers",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreateContactNumberRequest request, HttpContext httpContext) =>
            {
                var unauthorized = HttpCaller.UnauthorizedIfAnonymous(httpContext);
                if (unauthorized is not null)
                {
                    return unauthorized;
                }

                request.BuyerId = HttpCaller.BuyerId(httpContext)!;
                request.CancellationToken = httpContext.RequestAborted;
                return await HandleAsync(request);
            })
            .Produces<CreateContactNumberResponse>()
            .Produces(StatusCodes.Status400BadRequest)
            .WithTags("ContactNumberEndpoints");
    }

    public async Task<IResult> HandleAsync(CreateContactNumberRequest request)
    {
        try
        {
            var created = await _contactNumbers.RegisterAsync(request.BuyerId, request.PhoneNumber, request.CancellationToken);
            return Results.Created($"api/contact-numbers/{created.Id}", new CreateContactNumberResponse
            {
                ContactNumberId = created.Id,
                PhoneNumber = created.CanonicalNumber
            });
        }
        catch (InvalidContactNumberException ex)
        {
            return Results.BadRequest(new { message = ex.Message });
        }
        catch (OrderMessagingException)
        {
            return Results.Json(new { message = "The messaging provider is unavailable." }, statusCode: StatusCodes.Status502BadGateway);
        }
    }
}

public class CreateContactNumberRequest : BaseRequest
{
    public string PhoneNumber { get; set; } = string.Empty;
    internal string BuyerId { get; set; } = string.Empty;
    internal CancellationToken CancellationToken { get; set; }
}

public class CreateContactNumberResponse : BaseResponse
{
    public int ContactNumberId { get; set; }
    public string PhoneNumber { get; set; } = string.Empty;
}
