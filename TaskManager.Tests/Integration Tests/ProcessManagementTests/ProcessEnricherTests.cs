using System.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using TaskManager.Domain.Services;
using TaskManager.Utility.Utility;
using WinProcess = System.Diagnostics.Process;

namespace TaskManager.Tests
{
    public class ProcessEnricherTests
    {
        private static readonly ProcessEnricher Enricher = new(NullLogger<ProcessEnricher>.Instance);

        [Fact]
        public void SelfProcess_EnrichesWithPathAndConcreteBitness()
        {
            bool ok = Enricher.TryEnrich(Environment.ProcessId, out var enrichment);

            ok.ShouldBeTrue();
            enrichment.Path.ShouldBe(WinProcess.GetCurrentProcess().MainModule!.FileName!);
            enrichment.Architecture.ShouldNotBe(ArchitectureType.Unknown);
        }

        [Fact]
        public void StalePid_ReturnsFalseWithoutThrowing()
        {
            bool ok = Enricher.TryEnrich(GetUnusedPid(), out var enrichment);

            ok.ShouldBeFalse();
            enrichment.Path.ShouldBe(string.Empty);
        }

        private static int GetUnusedPid()
        {
            var livePids = WinProcess.GetProcesses().Select(p => p.Id).ToHashSet();
            for (int pid = 4; pid < 100_000; pid++)
            {
                if (!livePids.Contains(pid))
                {
                    return pid;
                }
            }

            throw new InvalidOperationException("Could not find an unused PID.");
        }
    }
}
