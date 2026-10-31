namespace SprintBoot.Platform;

public class SystemPingDto
{
    public required string Status { get; set; }

    public required string Version { get; set; }

    public required DateTime ServerTime { get; set; }
}
