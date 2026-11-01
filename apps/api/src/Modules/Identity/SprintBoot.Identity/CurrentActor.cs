using Volo.Abp.DependencyInjection;
using Volo.Abp.Users;

namespace SprintBoot.Identity;

public class CurrentActor(ICurrentUser currentUser) : ICurrentActor, ITransientDependency
{
    public ActorRef Actor => new(currentUser.GetId(), ActorType.User);
}
