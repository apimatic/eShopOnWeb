using System;
using System.ComponentModel.DataAnnotations;

namespace Microsoft.eShopWeb.Infrastructure.Data;

/// <summary>
/// An in-flight claim on a billing write. The primary key is what refuses a second concurrent caller;
/// the row is deleted once the write has been settled.
/// </summary>
public class MaxioClaim
{
    [Key]
    [MaxLength(256)]
    public string Key { get; set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; set; }
}
