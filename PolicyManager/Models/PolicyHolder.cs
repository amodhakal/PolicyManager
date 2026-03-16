using System.ComponentModel.DataAnnotations;

namespace PolicyManager.Models;

public class PolicyHolder
{
    public int Id { get; set; }

    [Required] [MaxLength(100)] public string FirstName { get; set; } = string.Empty;

    [Required] [MaxLength(100)] public string LastName { get; set; } = string.Empty;

    [Required] [EmailAddress] public string Email { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public ICollection<Policy> Policies { get; set; } = new List<Policy>();
}