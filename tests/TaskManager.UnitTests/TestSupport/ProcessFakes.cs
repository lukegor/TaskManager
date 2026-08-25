using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using TaskManager.Domain.Abstractions;
using TaskManager.Domain.Models;
using TaskManager.Domain.Primitives;
using TaskManager.Domain.Services;

namespace TaskManager.UnitTests.TestSupport
{
    /// <summary>Queue-driven enumerator: each Capture consumes the next scripted result.</summary>
    internal sealed class ScriptedEnumerator : ISystemProcessEnumerator
    {
        private readonly Queue<Func<IReadOnlyList<ProcessSnapshot>>> _scripts = new();
        public int CallCount { get; private set; }
        private Exception? _throwOnce;

        public static ScriptedEnumerator Of(params ProcessSnapshot[] items)
        {
            var e = new ScriptedEnumerator();
            e.Queue(items);
            return e;
        }

        public void Queue(params ProcessSnapshot[] items) => Queue(() => items);

        public void Queue(Func<IReadOnlyList<ProcessSnapshot>> script) => _scripts.Enqueue(script);

        public void ThrowNext(Exception ex) => _throwOnce = ex;

        public IReadOnlyList<ProcessSnapshot> Capture()
        {
            CallCount++;
            if (_throwOnce is not null)
            {
                var ex = _throwOnce;
                _throwOnce = null;
                throw ex;
            }

            return _scripts.Dequeue()();
        }
    }

    /// <summary>Enricher that never touches the OS and counts probes.</summary>
    internal sealed class CountingEnricher : ProcessEnricher
    {
        public int Calls { get; private set; }

        public CountingEnricher() : base(NullLogger<ProcessEnricher>.Instance)
        {
        }

        public override bool TryEnrich(int pid, out ProcessEnrichment enrichment)
        {
            Calls++;
            enrichment = new ProcessEnrichment($@"C:\fake-{pid}-{Calls}.exe", ArchitectureType.Bit64);
            return true;
        }
    }

    internal static class ProcessFakes
    {
        public static ProcessSnapshot Snap(int pid, string name = "n", int threadCount = 1) =>
            new(pid, name, ThreadCount: threadCount, Ppid: 4, BasePriority: 8);

        public static ProcessSnapshot SelfSnap() =>
            new(Environment.ProcessId, "self", ThreadCount: 1, Ppid: null, BasePriority: 8);
    }

    internal static class PollingTestHelper
    {
        /// <summary>
        /// Awaits until the enumerator has been entered N times. Happy path is instant;
        /// the real-time deadline is purely a failure backstop against lost ticks.
        /// </summary>
        public static async Task WaitForCaptureCountAsync(ScriptedEnumerator enumerator, int expected)
        {
            var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
            while (enumerator.CallCount < expected && DateTime.UtcNow < deadline)
            {
                await Task.Delay(10);
            }

            enumerator.CallCount.ShouldBeGreaterThanOrEqualTo(expected);
        }
    }
}
