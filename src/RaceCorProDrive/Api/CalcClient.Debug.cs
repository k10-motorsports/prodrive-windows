#if DEBUG
using System;
using System.Diagnostics;
using System.Threading.Tasks;

namespace RaceCorProDrive.Api
{
    /// <summary>
    /// Calls every calc endpoint with the smallest valid input and logs
    /// success / failure. Use this in DEBUG builds to verify
    /// <see cref="CalcClient"/> is wired up end-to-end (network, auth,
    /// JSON serialization) without committing to specific UX yet.
    ///
    /// Safe to fire-and-forget on app launch:
    /// <code>
    /// #if DEBUG
    ///     _ = Task.Run(CalcClientSmokeTest.RunAsync);
    /// #endif
    /// </code>
    ///
    /// race-summary and next-race-ideas require non-empty inputs to
    /// produce useful responses; they're skipped in the smoke test.
    /// race-summary in particular validates a session payload and
    /// returns 400 on empty bodies.
    /// </summary>
    public static class CalcClientSmokeTest
    {
        public static async Task RunAsync()
        {
            var client = CalcClient.Shared;
            Debug.WriteLine("[CalcClient] smoke test starting");

            await RunOne("dna",      () => client.FetchDriverDnaAsync(Array.Empty<DnaSession>(), Array.Empty<DnaRating>()));
            await RunOne("mastery",  () => client.FetchMasteryAsync(Array.Empty<MasterySession>()));
            await RunOne("moments",  () => client.FetchMomentsAsync(Array.Empty<MomentSession>(), Array.Empty<MomentRating>()));
            await RunOne("scatter",  () => client.FetchScatterAsync(Array.Empty<ScatterSession>()));
            await RunOne("when",     () => client.FetchWhenAsync(Array.Empty<WhenSession>(), Array.Empty<WhenRating>()));
            await RunOne("race-now", () => client.FetchRaceNowAsync());

            Debug.WriteLine("[CalcClient] smoke test done (race-summary + next-race-ideas skipped — require non-empty inputs)");
        }

        private static async Task RunOne<T>(string name, Func<Task<T>> block)
        {
            try
            {
                _ = await block();
                Debug.WriteLine($"[CalcClient] ✓ {name}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[CalcClient] ✗ {name}: {ex.Message}");
            }
        }
    }
}
#endif
