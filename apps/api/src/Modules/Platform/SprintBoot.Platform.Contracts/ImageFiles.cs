namespace SprintBoot.Platform;

/// <summary>Raster images people may upload as pictures. SVG is left out: it can carry script.</summary>
public static class ImageFiles
{
    /// <summary>What the bytes are, whatever the upload claimed, so nothing but an image is ever served.</summary>
    public static string? Sniff(ReadOnlySpan<byte> head) => head switch
    {
        [0x89, 0x50, 0x4E, 0x47, ..] => "image/png",
        [0xFF, 0xD8, 0xFF, ..] => "image/jpeg",
        [0x47, 0x49, 0x46, 0x38, ..] => "image/gif",
        [0x52, 0x49, 0x46, 0x46, _, _, _, _, 0x57, 0x45, 0x42, 0x50, ..] => "image/webp",
        _ => null,
    };
}
