#nullable enable
namespace Microservices.Models.Dto;

public class TicketDto
{
    public string? UserId { get; set; }
    public string ImageUrl { get; set; } = null!;
}