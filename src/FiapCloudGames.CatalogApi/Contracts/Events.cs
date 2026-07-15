namespace FiapCloudGames.Contracts;

// IMPORTANTE: este arquivo deve ser identico (mesmo namespace, mesmo nome de tipo
// e mesmos campos) ao existente na PaymentsAPI. O MassTransit usa o nome
// totalmente qualificado do tipo (namespace + nome) para casar publisher e
// consumer, entao os dois lados precisam manter esses records em sincronia
// mesmo estando em repositorios/projetos separados.

public sealed record OrderPlacedEvent(
    Guid OrderId,
    Guid UserId,
    Guid GameId,
    string UserEmail,
    string GameName,
    decimal Price,
    DateTime PlacedAtUtc);

public sealed record PaymentProcessedEvent(
    Guid OrderId,
    Guid UserId,
    Guid GameId,
    string UserEmail,
    string GameName,
    decimal Price,
    string Status,
    DateTime ProcessedAtUtc);
