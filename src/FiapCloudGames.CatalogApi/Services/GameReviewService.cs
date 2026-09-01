using FiapCloudGames.CatalogApi.Data;
using FiapCloudGames.CatalogApi.Domain;
using FiapCloudGames.CatalogApi.Exceptions;
using Microsoft.EntityFrameworkCore;
using MongoDB.Driver;

namespace FiapCloudGames.CatalogApi.Services;

public sealed class GameReviewService(CatalogDbContext db, IMongoDatabase mongoDatabase)
{
    private readonly IMongoCollection<GameReview> _reviews =
        mongoDatabase.GetCollection<GameReview>("game_reviews");

    public async Task<List<GameReview>> ListByGameAsync(Guid gameId, CancellationToken ct = default)
    {
        await EnsureGameExistsAsync(gameId, ct);

        return await _reviews
            .Find(review => review.GameId == gameId)
            .SortByDescending(review => review.CreatedAtUtc)
            .ToListAsync(ct);
    }

    public async Task<GameReview> CreateAsync(
        Guid gameId,
        Guid userId,
        string userEmail,
        int rating,
        string comment,
        Dictionary<string, string>? metadata,
        CancellationToken ct = default)
    {
        await EnsureGameExistsAsync(gameId, ct);
        Validate(rating, comment);

        var review = new GameReview
        {
            GameId = gameId,
            UserId = userId,
            UserEmail = userEmail,
            Rating = rating,
            Comment = comment.Trim(),
            Metadata = metadata ?? new Dictionary<string, string>(),
            CreatedAtUtc = DateTime.UtcNow
        };

        await _reviews.InsertOneAsync(review, cancellationToken: ct);
        return review;
    }

    private async Task EnsureGameExistsAsync(Guid gameId, CancellationToken ct)
    {
        var exists = await db.Games.AsNoTracking().AnyAsync(game => game.Id == gameId, ct);
        if (!exists) throw new NotFoundException("Jogo nao encontrado.");
    }

    private static void Validate(int rating, string comment)
    {
        if (rating is < 1 or > 5)
            throw new ArgumentException("A avaliacao deve estar entre 1 e 5.");

        if (string.IsNullOrWhiteSpace(comment))
            throw new ArgumentException("Comentario e obrigatorio.");
    }
}
