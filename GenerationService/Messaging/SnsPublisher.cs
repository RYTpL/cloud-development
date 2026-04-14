using Amazon.SimpleNotificationService;
using Amazon.SimpleNotificationService.Model;
using GenerationService.Models;
using System.Text.Json;

namespace GenerationService.Messaging;

public class SnsPublisher
{
    private readonly IAmazonSimpleNotificationService _sns;
    private readonly string _topicArn;
    private readonly ILogger<SnsPublisher> _logger;

    public SnsPublisher(
        IAmazonSimpleNotificationService sns,
        IConfiguration configuration,
        ILogger<SnsPublisher> logger)
    {
        _sns = sns;
        _topicArn = configuration["Sns__TopicArn"] ?? string.Empty;
        _logger = logger;
    }

    public async Task PublishAsync(SoftwareProjectContract contract)
    {
        if (string.IsNullOrEmpty(_topicArn))
        {
            _logger.LogWarning("SNS TopicArn не настроен — пропускаем публикацию");
            return;
        }

        var message = JsonSerializer.Serialize(contract);

        var request = new PublishRequest
        {
            TopicArn = _topicArn,
            Message = message
        };

        await _sns.PublishAsync(request);
        _logger.LogInformation("Контракт {Id} опубликован в SNS", contract.Id);
    }
}