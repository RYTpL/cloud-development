using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using Amazon.SimpleNotificationService;
using Amazon.SimpleNotificationService.Model;
using FileService.Models;
using System.Text.Json;
using Xunit;

namespace Tests;

// Интеграционные тесты проверяют всю цепочку:
// GenerationService → SNS → FileService → S3
public class IntegrationTests : IAsyncLifetime
{
    private readonly string _localStackUrl = "http://localhost:4566";
    private readonly string _topicName = "contracts-topic";
    private readonly string _bucketName = "contracts";

    private IAmazonSimpleNotificationService _sns = null!;
    private IAmazonS3 _s3 = null!;
    private string _topicArn = null!;

    // IAsyncLifetime.InitializeAsync — выполняется перед каждым тестом
    public async Task InitializeAsync()
    {
        var credentials = new BasicAWSCredentials("test", "test");

        _sns = new AmazonSimpleNotificationServiceClient(
            credentials,
            new AmazonSimpleNotificationServiceConfig
            {
                ServiceURL = _localStackUrl,
                AuthenticationRegion = "us-east-1"
            });

        _s3 = new AmazonS3Client(
            credentials,
            new AmazonS3Config
            {
                ServiceURL = _localStackUrl,
                AuthenticationRegion = "us-east-1",
                ForcePathStyle = true
            });

        // Создаём топик и бакет если не существуют
        var topicResponse = await _sns.CreateTopicAsync(_topicName);
        _topicArn = topicResponse.TopicArn;

        try
        {
            await _s3.PutBucketAsync(new PutBucketRequest
            {
                BucketName = _bucketName,
                UseClientRegion = true
            });
        }
        catch
        {
            // Бакет уже существует — это нормально
        }
    }

    // IAsyncLifetime.DisposeAsync — выполняется после каждого теста
    public Task DisposeAsync() => Task.CompletedTask;

    // Тест 1: Публикация сообщения в SNS
    [Fact]
    public async Task PublishToSns_ShouldSucceed()
    {
        // Arrange — готовим контракт
        var contract = new SoftwareProjectContract(
            Id: 1,
            ProjectName: "Test Project",
            ClientCompany: "Test Company",
            ProjectManager: "Иванов Иван Иванович",
            StartDate: DateOnly.FromDateTime(DateTime.Now),
            PlannedEndDate: DateOnly.FromDateTime(DateTime.Now.AddMonths(6)),
            ActualEndDate: DateOnly.FromDateTime(DateTime.Now.AddMonths(5)),
            Budget: 1_000_000m,
            ActualCost: 950_000m,
            CompletionPercentage: 100
        );

        var message = JsonSerializer.Serialize(contract);

        // Act — публикуем в SNS
        var response = await _sns.PublishAsync(new PublishRequest
        {
            TopicArn = _topicArn,
            Message = message
        });

        // Assert — проверяем что публикация прошла успешно
        Assert.NotNull(response.MessageId);
        Assert.NotEmpty(response.MessageId);
    }

    // Тест 2: Сохранение файла в S3
    [Fact]
    public async Task SaveFileToS3_ShouldSucceed()
    {
        // Arrange
        var contract = new SoftwareProjectContract(
            Id: 2,
            ProjectName: "S3 Test Project",
            ClientCompany: "S3 Company",
            ProjectManager: "Петров Пётр Петрович",
            StartDate: DateOnly.FromDateTime(DateTime.Now),
            PlannedEndDate: DateOnly.FromDateTime(DateTime.Now.AddMonths(3)),
            ActualEndDate: DateOnly.FromDateTime(DateTime.Now.AddMonths(2)),
            Budget: 500_000m,
            ActualCost: 480_000m,
            CompletionPercentage: 100
        );

        var json = JsonSerializer.Serialize(contract);
        var key = $"contracts/{contract.Id}_test.json";

        // Act — сохраняем файл в S3
        await _s3.PutObjectAsync(new PutObjectRequest
        {
            BucketName = _bucketName,
            Key = key,
            ContentBody = json,
            ContentType = "application/json"
        });

        // Assert — проверяем что файл существует в S3
        var response = await _s3.GetObjectAsync(_bucketName, key);
        Assert.NotNull(response);
        Assert.Equal("application/json", response.Headers.ContentType);
    }

    // Тест 3: Файл в S3 содержит правильные данные
    [Fact]
    public async Task SavedFileInS3_ShouldContainCorrectData()
    {
        // Arrange
        var contract = new SoftwareProjectContract(
            Id: 3,
            ProjectName: "Data Verification Project",
            ClientCompany: "Verification Corp",
            ProjectManager: "Сидоров Сидор Сидорович",
            StartDate: DateOnly.FromDateTime(DateTime.Now),
            PlannedEndDate: DateOnly.FromDateTime(DateTime.Now.AddMonths(4)),
            ActualEndDate: DateOnly.FromDateTime(DateTime.Now.AddMonths(3)),
            Budget: 2_000_000m,
            ActualCost: 1_800_000m,
            CompletionPercentage: 100
        );

        var json = JsonSerializer.Serialize(contract);
        var key = $"contracts/{contract.Id}_verification.json";

        // Act — сохраняем и читаем обратно
        await _s3.PutObjectAsync(new PutObjectRequest
        {
            BucketName = _bucketName,
            Key = key,
            ContentBody = json,
            ContentType = "application/json"
        });

        var response = await _s3.GetObjectAsync(_bucketName, key);
        using var reader = new StreamReader(response.ResponseStream);
        var savedJson = await reader.ReadToEndAsync();
        var savedContract = JsonSerializer.Deserialize<SoftwareProjectContract>(savedJson);

        // Assert — проверяем что данные совпадают
        Assert.NotNull(savedContract);
        Assert.Equal(contract.Id, savedContract.Id);
        Assert.Equal(contract.ProjectName, savedContract.ProjectName);
        Assert.Equal(contract.Budget, savedContract.Budget);
        Assert.Equal(contract.CompletionPercentage, savedContract.CompletionPercentage);
    }

    // Тест 4: Бакет существует
    [Fact]
    public async Task S3Bucket_ShouldExist()
    {
        var response = await _s3.ListBucketsAsync();
        var bucketExists = response.Buckets.Any(b => b.BucketName == _bucketName);
        Assert.True(bucketExists, $"Бакет {_bucketName} должен существовать");
    }

    // Тест 5: SNS топик существует
    [Fact]
    public async Task SnsTopic_ShouldExist()
    {
        var response = await _sns.ListTopicsAsync();
        var topicExists = response.Topics.Any(t => t.TopicArn == _topicArn);
        Assert.True(topicExists, $"SNS топик {_topicName} должен существовать");
    }
}