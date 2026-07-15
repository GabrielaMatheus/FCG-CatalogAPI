namespace FiapCloudGames.CatalogApi.Domain;

// Representa um jogo que ja pertence a biblioteca de um usuario.
// So e criado quando o pagamento correspondente e aprovado.
public class LibraryItem
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public Guid GameId { get; set; }
    public DateTime AddedAtUtc { get; set; } = DateTime.UtcNow;

    public Game? Game { get; set; }
}
