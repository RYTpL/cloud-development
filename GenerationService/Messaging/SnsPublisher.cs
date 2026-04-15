using Amazon.SimpleNotificationService;
using Amazon.SimpleNotificationService.Model;
using GenerationService.Models;
using System.Text.Json;

namespace GenerationService.Messaging;

public class SnsPublisher
{
    private readonly IAmazonSimpleNotificationService _sns;
    private readonly string _topicName;
    private readonly ILogger<SnsPublisher> _logger;
    private string? _topicArn;

    public SnsPublisher(
        IAmazonSimpleNotificationService sns,
        IConfiguration configuration,
        ILogger<SnsPublisher> logger)
    {
        _sns = sns;
        _topicName = configuration["Sns__TopicName"] ?? "contracts-topic";
        _logger = logger;
    }

    // Получаем ARN топика по имени (создаём если не существует)
    private async Task<string> GetTopicArnAsync()
    {
        if (_topicArn is not null) return _topicArn;

        var response = await _sns.CreateTopicAsync(_topicName);
        _topicArn = response.TopicArn;
        _logger.LogInformation("SNS топик ARN: {TopicArn}", _topicArn);
        return _topicArn;
    }

    public async Task PublishAsync(SoftwareProjectContract contract)
    {
        try
        {
            var topicArn = await GetTopicArnAsync();
            var message = JsonSerializer.Serialize(contract);

            await _sns.PublishAsync(new PublishRequest
            {
                TopicArn = topicArn,
                Message = message
            });

            _logger.LogInformation("Контракт {Id} опубликован в SNS", contract.Id);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Ошибка публикации в SNS");
        }
    }
}