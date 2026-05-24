using Amazon.SimpleNotificationService;
using Amazon.SimpleNotificationService.Model;
using System.Text.Json;

namespace GenerationService.Services;

public class SnsPublisherService
{
    private readonly IAmazonSimpleNotificationService _sns;
    private readonly IConfiguration _configuration;

    public SnsPublisherService(
        IAmazonSimpleNotificationService sns,
        IConfiguration configuration)
    {
        _sns = sns;
        _configuration = configuration;
    }

    public async Task PublishAsync(object contract)
    {
        await _sns.PublishAsync(new PublishRequest
        {
            TopicArn = _configuration["AWS:TopicArn"],
            Message = JsonSerializer.Serialize(contract)
        });
    }
}