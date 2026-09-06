namespace CustomerSupportCRM.Application.Common.Interfaces;

/// <summary>Default clock. Registered in DI; overridden in tests.</summary>
public sealed class SystemClock : IClock
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}
