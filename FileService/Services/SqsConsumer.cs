using Amazon;
using Amazon.Runtime;
using Amazon.SQS;
using Amazon.SQS.Model;
using System.Text.Json;

namespace FileService.Services;

public class SqsConsumer : BackgroundService
{
    private readonly IAmazonSQS _sqsClient;
    private readonly S3Uploader _s3Uploader;
    private readonly ILogger<SqsConsumer> _logger;
    private readonly string _queueUrl;

    public SqsConsumer(IConfiguration config, S3Uploader s3Uploader, ILogger<SqsConsumer> logger)
    {
        _s3Uploader = s3Uploader;
        _logger = logger;

        var localstackUrl = config["LocalStack:Url"] ?? "http://localhost:4566";

        _sqsClient = new AmazonSQSClient(
            new BasicAWSCredentials("test", "test"),
            new AmazonSQSConfig
            {
                ServiceURL = localstackUrl,
                RegionEndpoint = RegionEndpoint.USEast1
            });

        _queueUrl = config["SQS:QueueUrl"] ?? "http://localhost:4566/000000000000/contracts-queue";
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("SQS Consumer started on queue: {QueueUrl}", _queueUrl);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var request = new ReceiveMessageRequest
                {
                    QueueUrl = _queueUrl,
                    MaxNumberOfMessages = 10,
                    WaitTimeSeconds = 20,
                    MessageAttributeNames = new List<string> { "All" }
                };

                var response = await _sqsClient.ReceiveMessageAsync(request, stoppingToken);

                foreach (var message in response.Messages)
                {
                    try
                    {
                        _logger.LogInformation("Received message: {MessageId}", message.MessageId);

                        // SNS сообщение обёрнуто в JSON
                        var snsMessage = JsonSerializer.Deserialize<SnsMessage>(message.Body);
                        var contractsJson = snsMessage?.Message ?? message.Body;

                        _logger.LogDebug("Message body: {Body}", contractsJson);

                        await _s3Uploader.UploadContractsAsync(contractsJson);

                        await _sqsClient.DeleteMessageAsync(new DeleteMessageRequest
                        {
                            QueueUrl = _queueUrl,
                            ReceiptHandle = message.ReceiptHandle
                        }, stoppingToken);

                        _logger.LogInformation("Processed and deleted message: {MessageId}", message.MessageId);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error processing message {MessageId}", message.MessageId);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error receiving messages from SQS");
                await Task.Delay(5000, stoppingToken);
            }
        }
    }
}

public class SnsMessage
{
    public string Message { get; set; } = "";
    public string MessageId { get; set; } = "";
    public string Subject { get; set; } = "";
}