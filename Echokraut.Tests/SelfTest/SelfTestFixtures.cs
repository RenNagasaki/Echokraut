using System;
using System.Threading;
using System.Threading.Tasks;
using Echokraut.DataClasses;
using Echokraut.Enums;
using Echokraut.Services.SelfTest;
using Echotools.Logging.DataClasses;
using Echotools.Logging.Enums;
using Echotools.Logging.Services;
using Moq;

namespace Echokraut.Tests.SelfTest;

/// <summary>Shared builders for the self-test tests.</summary>
internal static class SelfTestFixtures
{
    public static Configuration LiveConfig() => new()
    {
        BackendSelection = TTSBackends.EchokrauTTS,
        EchokrauTts = { InstanceType = AlltalkInstanceType.Remote },
    };

    public static Configuration NoneConfig() => new()
    {
        BackendSelection = TTSBackends.EchokrauTTS,
        EchokrauTts = { InstanceType = AlltalkInstanceType.None },
    };

    public static Mock<ILogService> Log()
    {
        var log = new Mock<ILogService>();
        log.Setup(l => l.Start(It.IsAny<string>(), It.IsAny<TextSource>()))
            .Returns((string _, TextSource s) => new EchoEventId(7, s));
        return log;
    }

    /// <summary>A check whose body is a lambda.</summary>
    public sealed class FakeCheck : SelfTestCheck
    {
        private readonly Func<CancellationToken, Task<SelfTestResult>> _run;

        public FakeCheck(string id, Func<CancellationToken, Task<SelfTestResult>> run, bool interactive = false,
            TimeSpan? timeout = null, string? instruction = null)
        {
            Id = id;
            _run = run;
            Interactive = interactive;
            Timeout = timeout ?? TimeSpan.FromSeconds(5);
            Instruction = instruction;
        }

        public static FakeCheck Returning(string id, SelfTestResult result, bool interactive = false) =>
            new(id, _ => Task.FromResult(result), interactive);

        public override string Id { get; }
        public override string Name => "Fake " + Id;
        public override bool Interactive { get; }
        public override string? Instruction { get; }
        public override TimeSpan Timeout { get; }
        public int Runs { get; private set; }

        public override Task<SelfTestResult> RunAsync(CancellationToken ct)
        {
            Runs++;
            return _run(ct);
        }
    }
}
