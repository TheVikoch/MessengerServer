namespace MessengerServer
{
    public class UserAlreadyExistsException : Exception
    {
        public UserAlreadyExistsException(string email)
            : base($"User with email {email} already exists") { }
    }

    public class DisplayNameAlreadyExistsException : Exception
    {
        public DisplayNameAlreadyExistsException(string displayName)
            : base($"Display name {displayName} already exists") { }
    }
}
