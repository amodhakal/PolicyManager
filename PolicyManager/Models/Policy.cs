using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using PolicyManager.Models.Enums;

namespace PolicyManager.Models;

public class Policy
{
    public int Id { get; set; }

    [Required] [MaxLength(50)] public string PolicyNumber { get; set; } = Guid.NewGuid().ToString();

    [Required] public PolicyType Type { get; set; } = PolicyType.Auto;

    [Required] public PolicyStatus Status { get; set; } = PolicyStatus.Active;

    [Column(TypeName = "decimal(10,2)")] public decimal Premium { get; set; }

    public DateTime StartDate { get; set; }

    public DateTime EndDate { get; set; }

    [Required] public int PolicyHolderId { get; set; }

    public PolicyHolder? PolicyHolder { get; set; }

    public ICollection<Claim> Claims { get; set; } = new List<Claim>();
}