using Content.Shared._Starlight.Language; // Starlight

namespace Content.Server.Speech;

public sealed class ListenEvent : EntityEventArgs
{
    public readonly string Message;
    public readonly string? OriginalMessage;
    public readonly EntityUid Source;
    public readonly LanguagePrototype? Language; // Starlight

    public ListenEvent(string message, string? originalMessage, EntityUid source, LanguagePrototype? language) // Starlight
    {
        Message = message;
        OriginalMessage = originalMessage;
        Source = source;
        Language = language; // Starlight
    }
}

public sealed class ListenAttemptEvent : CancellableEntityEventArgs
{
    public readonly EntityUid Source;

    public ListenAttemptEvent(EntityUid source)
    {
        Source = source;
    }
}
