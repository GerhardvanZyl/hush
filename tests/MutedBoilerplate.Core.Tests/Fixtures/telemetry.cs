// Manual F5 fixture: open this file inside the experimental VS instance to see
// telemetry/logging spans render muted, and method signatures dimmed.
namespace MutedBoilerplate.Fixtures;

public class TelemetryFixture
{
    private readonly TelemetryClient _telemetryClient = new();
    private readonly ILogger _logger = new ConsoleLogger();

    public int Compute(int x, int y)
    {
        _logger.LogInformation("Compute starting with {X} and {Y}", x, y);
        _telemetryClient.TrackEvent("ComputeStarted");

        var result = x + y;

        _telemetryClient.TrackMetric("ResultMagnitude", result);
        _logger.LogDebug("Compute finished: {Result}", result);
        return result;
    }
}

public class TelemetryClient
{
    public void TrackEvent(string name) { }
    public void TrackMetric(string name, double value) { }
}

public interface ILogger
{
    void LogInformation(string message, params object[] args);
    void LogDebug(string message, params object[] args);
}

public class ConsoleLogger : ILogger
{
    public void LogInformation(string message, params object[] args) => System.Console.WriteLine(message);
    public void LogDebug(string message, params object[] args) => System.Console.WriteLine(message);
}
