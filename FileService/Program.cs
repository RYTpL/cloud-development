using FileService.Services;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

builder.Services.AddSingleton<S3Uploader>();
builder.Services.AddHostedService<SqsConsumer>();

var app = builder.Build();
app.Run();