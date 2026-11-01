namespace SprintBoot.Identity;

public enum ActorType
{
    User = 0,
    ServiceAccount = 1,
    Agent = 2,
}

/// <summary>Who performed or owns something. Not always a person, see the API token and AI phases.</summary>
public record ActorRef(Guid Id, ActorType Type);
