using PolicyManager.Models.Enums;

namespace PolicyManager.DTOs;

public class PolicyDto
{
    public int Id { get; set; }

    public string PolicyNumber { get; set; }

    public decimal Premium { get; set; }

    public PolicyStatus Status { get; set; }

    public string PolicyholderName { get; set; }
}