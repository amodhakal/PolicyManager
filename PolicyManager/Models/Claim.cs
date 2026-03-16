using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using PolicyManager.Models.Enums;

namespace PolicyManager.Models;

public class Claim
{
    public int Id { get; set; }

    [Required] [MaxLength(50)] public string ClaimNumber { get; set; } = Guid.NewGuid().ToString();

    [Required] [MaxLength(500)] public string Description { get; set; } = string.Empty;

    [Column(TypeName = "decimal(10,2)")] public decimal Amount { get; set; }

    [Required] public ClaimStatus Status { get; set; } = ClaimStatus.Pending;

    public DateTime FiledAt { get; set; } = DateTime.UtcNow;

    [Required] public int PolicyId { get; set; }

    public Policy? Policy { get; set; }
}