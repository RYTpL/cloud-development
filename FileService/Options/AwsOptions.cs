namespace FileService.Options;

public class S3Options
{
    /// <summary>URL эндпоинта (http://localhost:4566 для LocalStack, пусто для AWS)</summary>
    public string ServiceUrl { get; set; } = string.Empty;

    /// <summary>Имя бакета для хранения файлов контрактов</summary>
    public string BucketName { get; set; } = "contracts";

    public string AccessKey { get; set; } = "test";
    public string SecretKey { get; set; } = "test";
    public string Region { get; set; } = "us-east-1";
}

public class SnsOptions
{
    /// <summary>URL эндпоинта (http://localhost:4566 для LocalStack, пусто для AWS)</summary>
    public string ServiceUrl { get; set; } = string.Empty;

    /// <summary>ARN топика SNS, на который GenerationService публикует контракты</summary>
    public string TopicArn { get; set; } = string.Empty;

    /// <summary>URL очереди SQS, подписанной на топик (FileService читает из неё)</summary>
    public string QueueUrl { get; set; } = string.Empty;

    public string AccessKey { get; set; } = "test";
    public string SecretKey { get; set; } = "test";
    public string Region { get; set; } = "us-east-1";
}
