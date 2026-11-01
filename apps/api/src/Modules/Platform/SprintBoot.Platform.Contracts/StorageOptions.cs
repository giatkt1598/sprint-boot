namespace SprintBoot.Platform;

public class StorageOptions
{
    public const string SectionName = "Storage";

    public string Provider { get; set; } = "Local";

    public LocalStorageOptions Local { get; set; } = new();

    public S3StorageOptions S3 { get; set; } = new();

    /// <summary>Returns the required keys missing for the selected provider.</summary>
    public IReadOnlyList<string> GetMissingKeys()
    {
        var missing = new List<string>();

        switch (Provider)
        {
            case "Local":
                AddIfEmpty(missing, Local.Path, "Storage:Local:Path");
                break;
            case "S3":
                AddIfEmpty(missing, S3.Bucket, "Storage:S3:Bucket");
                AddIfEmpty(missing, S3.AccessKey, "Storage:S3:AccessKey");
                AddIfEmpty(missing, S3.SecretKey, "Storage:S3:SecretKey");
                if (string.IsNullOrEmpty(S3.Endpoint))
                {
                    AddIfEmpty(missing, S3.Region, "Storage:S3:Region");
                }

                break;
            default:
                throw new InvalidOperationException($"Invalid Storage:Provider '{Provider}': expected Local or S3.");
        }

        return missing;
    }

    private static void AddIfEmpty(List<string> missing, string? value, string key)
    {
        if (string.IsNullOrEmpty(value))
        {
            missing.Add(key);
        }
    }
}

public class LocalStorageOptions
{
    public string Path { get; set; } = string.Empty;
}

public class S3StorageOptions
{
    public string Endpoint { get; set; } = string.Empty;

    public string Region { get; set; } = string.Empty;

    public string Bucket { get; set; } = string.Empty;

    public string AccessKey { get; set; } = string.Empty;

    public string SecretKey { get; set; } = string.Empty;
}
