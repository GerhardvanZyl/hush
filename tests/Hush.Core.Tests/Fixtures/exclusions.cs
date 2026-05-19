// Manual F5 fixture: exercises the audit-logger exclusion. Ship a custom rules
// JSON pointing at this scenario, then toggle exclusions with Ctrl+Alt+M, X
// to see the audit lines flip in/out of muting independently of the rest.
namespace Hush.Fixtures;

public class ExclusionsFixture
{
    private readonly ILogger _logger = null!;
    private readonly ILogger _auditLogger = null!;

    public void Run()
    {
        _logger.LogInformation("normal log line");
        _auditLogger.LogInformation("audit line — should remain visible when exclusion is on");
        _logger.LogCritical("critical log line");
    }
}
