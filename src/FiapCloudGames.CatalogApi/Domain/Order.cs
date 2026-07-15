namespace FiapCloudGames.CatalogApi.Domain;

// Representa uma solicitacao de compra. Nasce como Pending quando o
// OrderPlacedEvent e publicado e e atualizada quando o CatalogAPI recebe
// o PaymentProcessedEvent vindo da PaymentsAPI.
public class Order
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public Guid GameId { get; set; }
    public string UserEmail { get; set; } = string.Empty;
    public string GameName { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public OrderStatus Status { get; set; } = OrderStatus.Pending;
    public DateTime PlacedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? ProcessedAtUtc { get; set; }
}
