using PolicyManager.Models.Enums;

namespace PolicyManager.DTOs;

public class UpdatePolicyDto
{
    public decimal Premium { get; set; }

    public PolicyStatus Status { get; set; }
}