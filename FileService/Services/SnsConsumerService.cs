using System.Text.Json;
using Amazon.SQS;
using Amazon.SQS.Model;
using FileService.Options;
using Microsoft.Extensions.Options;

namespace FileService.Services;

/// <summary>
/// Фоновый сервис: читает сообщения из SQS-очереди (подписанной на SNS-топик),
/// десериализует контракты и сохраняет их в S3 через S3FileStorageService.
/// </summary>
public class SnsConsumerService : BackgroundService
{
    private readonly IAmazonSQS _sqs;
    private readonly S3FileStorageService _storage;
    private readonly string _queueUrl;
    private readonly ILogger<SnsConsumerService> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public SnsConsumerService(IAmazonSQS sqs, S3FileStorageService storage,
        IOptions<SnsOptions> options, ILogger<SnsConsumerService> logger)
    {
        _sqs = sqs;
        _storage = storage;
        _queueUrl = options.Value.QueueUrl;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("SnsConsumerService запущен, очередь: {Queue}", _queueUrl);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await PollOnceAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при чтении очереди SQS");
                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
            }
        }
    }

    private async Task PollOnceAsync(CancellationToken ct)
    {
        var request = new ReceiveMessageRequest
        {
            QueueUrl = _queueUrl,
            MaxNumberOfMessages = 10,
            WaitTimeSeconds = 5   // long polling
        };

        var response = await _sqs.ReceiveMessageAsync(request, ct);

        if (response.Messages.Count == 0)
            return;

        _logger.LogInformation("Получено {Count} сообщений из SQS", response.Messages.Count);

        foreach (var message in response.Messages)
        {
            await ProcessMessageAsync(message, ct);
        }
    }

    private async Task ProcessMessageAsync(Message message, CancellationToken ct)
    {
        try
        {
            // SNS оборачивает тело в конверт { "Message": "..." }
            var envelope = JsonSerializer.Deserialize<SnsEnvelope>(message.Body, JsonOptions);
            var payload = envelope?.Message ?? message.Body;

            // Десериализуем контракт (используем динамический JsonElement)
            var doc = JsonDocument.Parse(payload);
            var id = doc.RootElement.GetProperty("id").GetInt32();

            var key = $"contracts/{id}.json";
            await _storage.SaveAsync(key, doc, ct);

            // Удаляем сообщение из очереди после успешной обработки
            await _sqs.DeleteMessageAsync(_queueUrl, message.ReceiptHandle, ct);

            _logger.LogInformation("Контракт {Id} сохранён в S3 по ключу {Key}", id, key);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Ошибка обработки сообщения {MessageId}", message.MessageId);
        }
    }

    private sealed class SnsEnvelope
    {
        public string? Message { get; set; }
    }
}
