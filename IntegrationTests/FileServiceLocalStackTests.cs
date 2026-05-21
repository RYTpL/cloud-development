using System.Text;
using System.Text.Json;
using Amazon;
using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using Amazon.SimpleNotificationService;
using Amazon.SQS;
using Amazon.SQS.Model;
using FileService.Options;
using FileService.Services;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Testcontainers.LocalStack;
using Xunit;

namespace IntegrationTests;

/// <summary>
/// Интеграционные тесты FileService с реальным LocalStack (S3 + SNS + SQS).
/// LocalStack поднимается в Docker через Testcontainers.
/// </summary>
public class FileServiceLocalStackTests : IAsyncLifetime
{
    // LocalStack поднимает S3, SNS, SQS на порту 4566
    private readonly LocalStackContainer _localStack = new LocalStackBuilder()
        .WithImage("localstack/localstack:3")
        .Build();

    private IAmazonS3 _s3 = null!;
    private IAmazonSimpleNotificationService _sns = null!;
    private IAmazonSQS _sqs = null!;
    private S3FileStorageService _storage = null!;

    private const string BucketName = "test-contracts";
    private const string TopicName = "contracts-topic";
    private const string QueueName = "contracts-queue";

    private string _topicArn = string.Empty;
    private string _queueUrl = string.Empty;

    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public async Task InitializeAsync()
    {
        await _localStack.StartAsync();

        var endpoint = _localStack.GetConnectionString(); // http://localhost:{mappedPort}
        var credentials = new BasicAWSCredentials("test", "test");
        var region = RegionEndpoint.USEast1;

        // S3
        _s3 = new AmazonS3Client(credentials, new AmazonS3Config
        {
            ServiceURL = endpoint,
            ForcePathStyle = true,
            RegionEndpoint = region
        });

        // SNS
        _sns = new AmazonSimpleNotificationServiceClient(credentials,
            new AmazonSimpleNotificationServiceConfig
            {
                ServiceURL = endpoint,
                RegionEndpoint = region
            });

        // SQS
        _sqs = new AmazonSQSClient(credentials, new AmazonSQSConfig
        {
            ServiceURL = endpoint,
            RegionEndpoint = region
        });

        // Создаём бакет
        await _s3.PutBucketAsync(BucketName);

        // Создаём SNS-топик
        var topicResponse = await _sns.CreateTopicAsync(TopicName);
        _topicArn = topicResponse.TopicArn;

        // Создаём SQS-очередь
        var queueResponse = await _sqs.CreateQueueAsync(QueueName);
        _queueUrl = queueResponse.QueueUrl;

        // Подписываем SQS на SNS-топик
        var queueAttrs = await _sqs.GetQueueAttributesAsync(_queueUrl,
            ["QueueArn"]);
        var queueArn = queueAttrs.QueueARN;
        await _sns.SubscribeQueueAsync(_topicArn, _sqs, _queueUrl);

        // Создаём S3FileStorageService
        var s3Options = Options.Create(new S3Options
        {
            BucketName = BucketName,
            ServiceUrl = endpoint,
            AccessKey = "test",
            SecretKey = "test",
            Region = "us-east-1"
        });

        _storage = new S3FileStorageService(_s3, s3Options,
            NullLogger<S3FileStorageService>.Instance);
    }

    public async Task DisposeAsync()
    {
        _s3.Dispose();
        _sns.Dispose();
        _sqs.Dispose();
        await _localStack.StopAsync();
    }

    // ────────────────── S3 тесты ──────────────────

    [Fact]
    public async Task S3_SaveAsync_ShouldPersistJsonFile()
    {
        // Arrange
        var contract = new SoftwareProjectContract
        {
            Id = 1,
            ProjectName = "Test Project",
            ClientCompany = "Test Corp",
            Budget = 500_000m
        };

        // Act
        await _storage.SaveAsync("contracts/1.json", contract);

        // Assert — файл должен появиться в S3
        var saved = await _storage.GetAsync<SoftwareProjectContract>("contracts/1.json");

        saved.Should().NotBeNull();
        saved!.Id.Should().Be(1);
        saved.ProjectName.Should().Be("Test Project");
        saved.Budget.Should().Be(500_000m);
    }

    [Fact]
    public async Task S3_GetAsync_NonExistentKey_ShouldReturnNull()
    {
        // Act
        var result = await _storage.GetAsync<SoftwareProjectContract>("contracts/nonexistent.json");

        // Assert
        result.Should().BeNull("несуществующий ключ должен вернуть null без исключения");
    }

    [Fact]
    public async Task S3_SaveAsync_MultipleContracts_AllShouldBeStored()
    {
        // Arrange & Act
        var contracts = Enumerable.Range(10, 5).Select(id => new SoftwareProjectContract
        {
            Id = id,
            ProjectName = $"Project {id}",
            Budget = id * 100_000m
        }).ToList();

        foreach (var c in contracts)
            await _storage.SaveAsync($"contracts/{c.Id}.json", c);

        // Assert
        foreach (var c in contracts)
        {
            var loaded = await _storage.GetAsync<SoftwareProjectContract>($"contracts/{c.Id}.json");
            loaded.Should().NotBeNull();
            loaded!.Id.Should().Be(c.Id);
            loaded.Budget.Should().Be(c.Budget);
        }
    }

    [Fact]
    public async Task S3_EnsureBucketExists_ShouldNotThrowIfAlreadyExists()
    {
        // Бакет уже создан в InitializeAsync — повторный вызов не должен падать
        var act = async () => await _storage.EnsureBucketExistsAsync();
        await act.Should().NotThrowAsync();
    }

    // ────────────────── SNS → SQS тесты ──────────────────

    [Fact]
    public async Task Sns_Publish_ShouldDeliverMessageToSqsQueue()
    {
        // Arrange
        var payload = JsonSerializer.Serialize(new { id = 42, projectName = "SNS Test" });

        // Act — публикуем в SNS-топик
        await _sns.PublishAsync(_topicArn, payload);

        // Assert — сообщение должно появиться в SQS-очереди
        var messages = await ReceiveMessagesWithRetryAsync(expectedCount: 1);

        messages.Should().HaveCount(1, "одно сообщение должно дойти до очереди");
        messages[0].Body.Should().Contain("SNS Test",
            "тело сообщения должно содержать опубликованный payload");
    }

    [Fact]
    public async Task Sns_PublishMultiple_AllShouldArriveInQueue()
    {
        // Arrange — публикуем 3 контракта
        for (var i = 1; i <= 3; i++)
        {
            var payload = JsonSerializer.Serialize(new { id = 100 + i, projectName = $"Batch {i}" });
            await _sns.PublishAsync(_topicArn, payload);
        }

        // Assert — все 3 должны появиться в очереди
        var messages = await ReceiveMessagesWithRetryAsync(expectedCount: 3);

        messages.Should().HaveCountGreaterOrEqualTo(3,
            "все опубликованные сообщения должны дойти до очереди");
    }

    [Fact]
    public async Task SnsConsumer_ProcessMessage_ShouldSaveContractToS3()
    {
        // Arrange — готовим SnsConsumerService с тестовым LocalStack
        var snsOptions = Options.Create(new SnsOptions
        {
            QueueUrl = _queueUrl,
            TopicArn = _topicArn,
            ServiceUrl = _localStack.GetConnectionString(),
            AccessKey = "test",
            SecretKey = "test",
            Region = "us-east-1"
        });

        var consumer = new SnsConsumerService(
            _sqs,
            _storage,
            snsOptions,
            NullLogger<SnsConsumerService>.Instance);

        // Публикуем тестовый контракт в SNS
        var contract = new { id = 999, projectName = "Consumer Test", budget = 123456 };
        await _sns.PublishAsync(_topicArn, JsonSerializer.Serialize(contract));

        // Act — запускаем consumer на 3 секунды
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        try { await consumer.StartAsync(cts.Token); } catch (OperationCanceledException) { }

        await Task.Delay(TimeSpan.FromSeconds(2));

        // Assert — файл должен появиться в S3
        var saved = await _storage.GetAsync<JsonDocument>("contracts/999.json");
        saved.Should().NotBeNull("consumer должен сохранить контракт в S3");
    }

    // ────────────────── Helper ──────────────────

    /// <summary>
    /// Polling SQS с повторными попытками для компенсации задержки SNS→SQS.
    /// </summary>
    private async Task<List<Amazon.SQS.Model.Message>> ReceiveMessagesWithRetryAsync(
        int expectedCount, int maxAttempts = 6)
    {
        var all = new List<Amazon.SQS.Model.Message>();

        for (var attempt = 0; attempt < maxAttempts && all.Count < expectedCount; attempt++)
        {
            var response = await _sqs.ReceiveMessageAsync(new ReceiveMessageRequest
            {
                QueueUrl = _queueUrl,
                MaxNumberOfMessages = 10,
                WaitTimeSeconds = 2
            });

            all.AddRange(response.Messages);

            if (all.Count < expectedCount)
                await Task.Delay(500);
        }

        return all;
    }
}
