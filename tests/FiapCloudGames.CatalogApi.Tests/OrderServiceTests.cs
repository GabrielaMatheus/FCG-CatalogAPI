using FiapCloudGames.CatalogApi.Data;
using FiapCloudGames.CatalogApi.Domain;
using FiapCloudGames.CatalogApi.Exceptions;
using FiapCloudGames.CatalogApi.Services;
using FiapCloudGames.Contracts;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace FiapCloudGames.CatalogApi.Tests;

public class OrderServiceTests
{
    private static CatalogDbContext CriarContexto()
    {
        var options = new DbContextOptionsBuilder<CatalogDbContext>()
            .UseSqlite("Data Source=:memory:")
            .Options;

        var db = new CatalogDbContext(options);
        db.Database.OpenConnection();
        db.Database.EnsureCreated();
        return db;
    }

    [Fact]
    public async Task PlaceOrderAsync_deve_publicar_OrderPlacedEvent_e_criar_pedido_pendente()
    {
        await using var db = CriarContexto();
        var jogo = new Game { Name = "Jogo Teste", Price = 50m };
        db.Games.Add(jogo);
        await db.SaveChangesAsync();

        var publishMock = new Mock<IPublishEndpoint>();
        var service = new OrderService(db, publishMock.Object, NullLogger<OrderService>.Instance);

        var order = await service.PlaceOrderAsync(Guid.NewGuid(), jogo.Id, "user@teste.com");

        Assert.Equal(OrderStatus.Pending, order.Status);
        publishMock.Verify(p => p.Publish(It.Is<OrderPlacedEvent>(e =>
            e.OrderId == order.Id &&
            e.GameId == jogo.Id &&
            e.UserEmail == "user@teste.com" &&
            e.GameName == jogo.Name &&
            e.Price == jogo.Price), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task PlaceOrderAsync_deve_lancar_conflito_se_usuario_ja_possui_o_jogo()
    {
        await using var db = CriarContexto();
        var userId = Guid.NewGuid();
        var jogo = new Game { Name = "Jogo Teste", Price = 50m };
        db.Games.Add(jogo);
        db.LibraryItems.Add(new LibraryItem { UserId = userId, GameId = jogo.Id });
        await db.SaveChangesAsync();

        var publishMock = new Mock<IPublishEndpoint>();
        var service = new OrderService(db, publishMock.Object, NullLogger<OrderService>.Instance);

        await Assert.ThrowsAsync<ConflictException>(() => service.PlaceOrderAsync(userId, jogo.Id, "user@teste.com"));
    }

    [Fact]
    public async Task ProcessPaymentResultAsync_aprovado_deve_adicionar_jogo_na_biblioteca()
    {
        await using var db = CriarContexto();
        var userId = Guid.NewGuid();
        var jogo = new Game { Name = "Jogo Teste", Price = 50m };
        db.Games.Add(jogo);
        await db.SaveChangesAsync();

        var publishMock = new Mock<IPublishEndpoint>();
        var service = new OrderService(db, publishMock.Object, NullLogger<OrderService>.Instance);
        var order = await service.PlaceOrderAsync(userId, jogo.Id, "user@teste.com");

        await service.ProcessPaymentResultAsync(order.Id, "Approved", DateTime.UtcNow);

        var possuiJogo = await db.LibraryItems.AnyAsync(l => l.UserId == userId && l.GameId == jogo.Id);
        Assert.True(possuiJogo);
    }

    [Fact]
    public async Task ProcessPaymentResultAsync_rejeitado_nao_deve_adicionar_jogo_na_biblioteca()
    {
        await using var db = CriarContexto();
        var userId = Guid.NewGuid();
        var jogo = new Game { Name = "Jogo Teste", Price = 50m };
        db.Games.Add(jogo);
        await db.SaveChangesAsync();

        var publishMock = new Mock<IPublishEndpoint>();
        var service = new OrderService(db, publishMock.Object, NullLogger<OrderService>.Instance);
        var order = await service.PlaceOrderAsync(userId, jogo.Id, "user@teste.com");

        await service.ProcessPaymentResultAsync(order.Id, "Rejected", DateTime.UtcNow);

        var possuiJogo = await db.LibraryItems.AnyAsync(l => l.UserId == userId && l.GameId == jogo.Id);
        Assert.False(possuiJogo);
    }
}
