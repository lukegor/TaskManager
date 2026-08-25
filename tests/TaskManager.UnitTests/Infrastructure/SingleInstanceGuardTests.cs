using TaskManager.Infrastructure;

namespace TaskManager.UnitTests.Infrastructure
{
    [Collection("SingleInstanceGuard")]
    public class SingleInstanceGuardTests : IDisposable
    {
        // The guard under test claims a session-wide mutex name, so ANY other
        // process constructing it (most notably a still-tearing-down test host
        // from a previous `dotnet test` invocation) races with these tests.
        // This gate is taken only by this test class in every host, serializing
        // them against each other for the duration of each test.
        private static readonly Mutex TestExecutionGate =
            new(initiallyOwned: false, "Local\\TaskManager.SingleInstance.TestGate");

        private const string GuardMutexName = "Local\\TaskManager.SingleInstance";

        private readonly bool _ownsGate;

        public SingleInstanceGuardTests()
        {
            try
            {
                _ownsGate = TestExecutionGate.WaitOne(TimeSpan.FromSeconds(30));
            }
            catch (AbandonedMutexException)
            {
                // previous holder died; ownership transferred to us
                _ownsGate = true;
            }

            // A host killed while owning the guard mutex leaves it abandoned,
            // which would let the handoff waiter adopt it instantly and break
            // the mid-wait assertion. Adopt-and-release any residue so every
            // test starts from a known-free mutex.
            if (Mutex.TryOpenExisting(GuardMutexName, out var stale))
            {
                try
                {
                    using var held = stale;
                    if (held.WaitOne(TimeSpan.FromSeconds(11)))
                    {
                        held.ReleaseMutex();
                    }
                }
                catch (AbandonedMutexException)
                {
                    // adopted from a dead predecessor; closing the last handle destroys it
                }
            }
        }

        [Fact]
        public void FirstConstruction_OwnsMutex()
        {
            using var guard = new SingleInstanceGuard(waitForExistingRelease: false);

            guard.IsFirstInstance.ShouldBeTrue();
        }

        [Fact]
        public void SecondConcurrentConstruction_IsNotFirstInstance()
        {
            using var first = new SingleInstanceGuard(waitForExistingRelease: false);
            using var second = new SingleInstanceGuard(waitForExistingRelease: false);

            first.IsFirstInstance.ShouldBeTrue();
            second.IsFirstInstance.ShouldBeFalse();
        }

        [Fact]
        public void Dispose_AllowsImmediateReacquisition()
        {
            var first = new SingleInstanceGuard(waitForExistingRelease: false);
            first.Dispose();

            using var second = new SingleInstanceGuard(waitForExistingRelease: false);

            second.IsFirstInstance.ShouldBeTrue();
        }

        [Fact]
        public async Task HandoffMode_WaitsForPredecessorRelease()
        {
            const int maxAttempts = 3;
            for (var attempt = 1; ; attempt++)
            {
                var outcome = await TryRunHandoffScenarioAsync();
                if (outcome is null)
                {
                    return;
                }

                if (attempt >= maxAttempts)
                {
                    throw new ShouldAssertException(
                        $"handoff scenario failed after {attempt} attempts (last: {outcome})");
                }
            }
        }

        /// <returns>null on success; otherwise a description of what went wrong.</returns>
        private static async Task<string?> TryRunHandoffScenarioAsync()
        {
            using var first = new SingleInstanceGuard(waitForExistingRelease: false);
            if (!first.IsFirstInstance)
            {
                return "predecessor could not acquire the mutex";
            }

            var successorTask = Task.Run(() => new SingleInstanceGuard(waitForExistingRelease: true));

            // Deliberately synchronous: Win32 mutexes are thread-affine, so both the
            // acquisition below AND the releasing Dispose must run on THIS thread.
            // An `await` here could resume on another thread and silently skip the
            // release (ReleaseMutex throws for non-owners), starving the successor.
            Thread.Sleep(200);
            var waitedWhileHeld = !successorTask.IsCompleted; // still waiting on the held mutex

            first.Dispose(); // predecessor releases

            using var successor = await successorTask;
            return (waitedWhileHeld, successor.IsFirstInstance) switch
            {
                (true, true) => null,
                (false, _) => "successor finished before the predecessor released",
                (_, false) => "successor timed out instead of acquiring",
            };
        }

        public void Dispose()
        {
            // best-effort cleanup so a failed test does not poison later runs
            try
            {
                using var cleanup = new SingleInstanceGuard(waitForExistingRelease: false);
            }
            catch
            {
                /* ignore */
            }

            if (_ownsGate)
            {
                try
                {
                    TestExecutionGate.ReleaseMutex();
                }
                catch (ApplicationException)
                {
                    // ownership lost via thread switch edge cases; nothing to release
                }
            }
        }
    }
}
