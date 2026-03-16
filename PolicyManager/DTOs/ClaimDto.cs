using PolicyManager.Models.Enums;

namespace PolicyManager.DTOs;

public class ClaimDto
{
    public int Id { get; set; }

    public int PolicyId { get; set; }

    public decimal Amount { get; set; }

    public ClaimStatus Status { get; set; }

    public DateTime FiledAt { get; set; }
}