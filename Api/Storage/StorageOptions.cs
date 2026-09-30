namespace TenNet.Api.Storage;

public sealed class StorageOptions
{
    public const string SectionName = "Storage";

    public string? ServiceUrl { get; set; }
    public string Region { get; set; } = "us-east-1";
    public string? AccessKey { get; set; }
    public string? SecretKey { get; set; }
    public string BucketName { get; set; } = "tennet-inventory";
    public bool ForcePathStyle { get; set; } = true;
}
