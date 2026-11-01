namespace SprintBoot.Identity;

public class LoginInput
{
    public required string Email { get; set; }

    public required string Password { get; set; }
}

public class CurrentUserDto
{
    public required Guid Id { get; set; }

    public required string Email { get; set; }

    public required string Name { get; set; }

    public required string[] Roles { get; set; }
}
