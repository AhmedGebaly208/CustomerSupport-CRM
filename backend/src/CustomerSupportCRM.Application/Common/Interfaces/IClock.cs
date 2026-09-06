namespace CustomerSupportCRM.Application.Common.Interfaces;

/// <summary>Injected rather than calling DateTimeOffset.UtcNow directly so SLA timers and
/// ticket history are deterministic under test.</summary>
public interface IClock
{
    DateTimeOffset UtcNow { get; }
}
