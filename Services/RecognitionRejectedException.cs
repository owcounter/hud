using Owmeta.Vision.Protocol;

namespace Owmeta.Services;

// A rejected image is not a transport/protocol failure. Only this case asks the
// backend to reuse history; worker outages and malformed replies stay errors.
public sealed class RecognitionRejectedException(string message, RecognitionResult raw) : ApiException(message)
{
    public RecognitionResult Raw { get; } = raw;
}
