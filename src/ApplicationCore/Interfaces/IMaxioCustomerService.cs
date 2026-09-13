using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface IMaxioCustomerService
{
    Task<MaxioCustomerResult> EnsureCustomerExistsAsync(string email, string firstName, string lastName, CancellationToken ct);
}

public class MaxioCustomerResult
{
    public int Id { get; set; }
    public string? Email { get; set; }
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
}
