using Amazon;
using Amazon.Runtime;
using Amazon.SimpleNotificationService;
using Amazon.SimpleNotificationService.Model;
using GenerationService.Models;
using System.Text.Json;

namespace GenerationService.Services;

public class SnsPublisher
{
    private readonly IAmazonSimpleNotificationService _snsClient;
    private readonly string _topicArn;
    private readonly ILogger<SnsPublisher> _logger;

    public SnsPublisher(IConfiguration config, ILogger<SnsPublisher> logger)
    {
        _logger = logger;

        var localstackUrl = config["LocalStack:Url"] ?? "http://localhost:4566";

        _snsClient = new AmazonSimpleNotificationServiceClient(
            new BasicAWSCredentials("test", "test"),
            new AmazonSimpleNotificationServiceConfig
            {
                ServiceURL = localstackUrl,
                RegionEndpoint = RegionEndpoint.USEast1
            });

        _topicArn = config["SNS:TopicArn"] ?? "arn:aws:sns:us-east-1:000000000000:contracts-topic";
    }

    public async Task PublishContractsAsync(List<SoftwareProjectContract> contracts)
    {
        try
        {
            var message = JsonSerializer.Serialize(contracts);

            var request = new PublishRequest
            {
                TopicArn = _topicArn,
                Message = message,
                Subject = "New Contracts Generated"
            };

            var response = await _snsClient.PublishAsync(request);
            _logger.LogInformation("Published to SNS. MessageId: {MessageId}", response.MessageId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to publish to SNS");
        }
    }
}