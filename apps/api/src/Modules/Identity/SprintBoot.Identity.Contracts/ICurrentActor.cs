namespace SprintBoot.Identity;

/// <summary>The only way business code learns who is calling. Independent of the auth mechanism.</summary>
public interface ICurrentActor
{
    ActorRef Actor { get; }
}
