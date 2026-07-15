using FiapCloudGames.CatalogApi.Services;
using FiapCloudGames.Contracts;
using MassTransit;

namespace FiapCloudGames.CatalogApi.Consumers;

public class PaymentProcessedConsumer(OrderService orderService, ILogger<PaymentProcessedConsumer> logger)
    : IConsumer<PaymentProcessedEvent>
{
    public async Task Consume(ConsumeContext<PaymentProcessedEvent> context)
    {
        var evento = context.Message;

        logger.LogInformation(
            "PaymentProcessedEvent recebido: pedido {OrderId}, status {Status}",
            evento.OrderId, evento.Status);

        await orderService.ProcessPaymentResultAsync(
            evento.OrderId,
            evento.Status,
            evento.ProcessedAtUtc,
            context.CancellationToken);
    }
}
