namespace CartService.Domain;

/// <summary>
/// Thrown when a domain rule is violated.
/// The ErrorCode is used to map to the correct HTTP status and Problem Details type.
/// </summary>
public class DomainException : Exception
{
    public string ErrorCode { get; }

    public DomainException(string errorCode, string message)
        : base(message)
    {
        ErrorCode = errorCode;
    }

    public DomainException(string errorCode, string message, Exception inner)
        : base(message, inner)
    {
        ErrorCode = errorCode;
    }
}
