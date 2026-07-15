using FiapCloudGames.CatalogApi.Data;
using FiapCloudGames.CatalogApi.Domain;
using FiapCloudGames.CatalogApi.Exceptions;
using FiapCloudGames.Contracts;
using MassTransit;
using Microsoft.EntityFrameworkCore;

namespace FiapCloudGames.CatalogApi.Services;

public class OrderService(CatalogDbContext db, IPublishEndpoint publishEndpoint, ILogger<OrderService> logger)
{
    public async Task<List<Game>> ListLibraryAsync(Guid userId, CancellationToken ct = default)
        => await db.LibraryItems
            .AsNoTracking()
            .Where(l => l.UserId == userId)
            .Include(l => l.Game)
            .Select(l => l.Game!)
            .ToListAsync(ct);

    // Inicia o fluxo de compra: cria o pedido como Pending e publica o
    // OrderPlacedEvent para a PaymentsAPI processar de forma assincrona.
    public async Task<Order> PlaceOrderAsync(Guid userId, Guid gameId, string userEmail, CancellationToken ct = default)
    {
        var game = await db.Games.AsNoTracking().FirstOrDefaultAsync(g => g.Id == gameId, ct)
                   ?? throw new NotFoundException("Jogo nao encontrado.");

        var jaPossuiJogo = await db.LibraryItems.AnyAsync(l => l.UserId == userId && l.GameId == gameId, ct);
        if (jaPossuiJogo)
            throw new ConflictException("Usuario ja possui esse jogo na biblioteca.");

        var order = new Order
        {
            UserId = userId,
            GameId = gameId,
            UserEmail = userEmail,
            GameName = game.Name,
            Price = game.Price,
            Status = OrderStatus.Pending
        };

        db.Orders.Add(order);
        await db.SaveChangesAsync(ct);

        await publishEndpoint.Publish(new OrderPlacedEvent(
            order.Id,
            order.UserId,
            order.GameId,
            order.UserEmail,
            order.GameName,
            order.Price,
            order.PlacedAtUtc), ct);

        logger.LogInformation("OrderPlacedEvent publicado para o pedido {OrderId}", order.Id);

        return order;
    }

    // Chamado pelo consumer do PaymentProcessedEvent. Atualiza o pedido e,
    // se aprovado, adiciona o jogo na biblioteca do usuario.
    public async Task ProcessPaymentResultAsync(Guid orderId, string status, DateTime processedAtUtc, CancellationToken ct = default)
    {
        var order = await db.Orders.FirstOrDefaultAsync(o => o.Id == orderId, ct);
        if (order is null)
        {
            logger.LogWarning("PaymentProcessedEvent recebido para pedido desconhecido {OrderId}", orderId);
            return;
        }

        if (order.Status != OrderStatus.Pending)
        {
            logger.LogInformation("Pedido {OrderId} ja havia sido processado, ignorando evento duplicado.", orderId);
            return;
        }

        order.Status = Enum.TryParse<OrderStatus>(status, ignoreCase: true, out var parsed)
            ? parsed
            : OrderStatus.Rejected;
        order.ProcessedAtUtc = processedAtUtc;

        if (order.Status == OrderStatus.Approved)
        {
            var jaExiste = await db.LibraryItems.AnyAsync(l => l.UserId == order.UserId && l.GameId == order.GameId, ct);
            if (!jaExiste)
            {
                db.LibraryItems.Add(new LibraryItem { UserId = order.UserId, GameId = order.GameId });
            }
        }

        await db.SaveChangesAsync(ct);
    }
}
