using System;

namespace EnvGuard
{
    public static class NetworkTiming
    {
        public const int PollIntervalMs=11000;
        public const int RequestTimeoutMs=8000;
        public const int FailureThreshold=2;
    }

    // Monotonic, start-to-start scheduling: request duration does not add to the
    // eleven-second interval. Late rounds are skipped, never queued or overlapped.
    internal sealed class NetworkSchedule
    {
        public long NextStartMs {get;private set;}
        public bool Due(long nowMs,bool inFlight){return !inFlight && nowMs>=NextStartMs;}
        public void Started(long nowMs){NextStartMs=nowMs+NetworkTiming.PollIntervalMs;}
    }
}
