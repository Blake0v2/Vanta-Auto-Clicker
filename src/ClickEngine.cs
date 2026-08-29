using System;
using System.Diagnostics;
using System.Threading;

namespace Vanta
{
    public interface IClickOutput
    {
        void MoveTo(SequencePoint point);
        void Press(ClickButton button);
        void Release(ClickButton button);
    }

    public sealed class ClickEngine : IDisposable
    {
        private readonly object gate = new object();
        private readonly IClickOutput output;
        private readonly Random random;
        private readonly Stopwatch clock = new Stopwatch();
        private readonly ManualResetEvent stop = new ManualResetEvent(false);
        private Thread worker;
        private long count;
        private volatile bool running;
        private bool disposed;
        public event Action Finished;
        public string LastError { get; private set; }
        public string StopReason { get; private set; }
        public bool IsRunning { get { return running; } }
        public long Clicks { get { return Interlocked.Read(ref count); } }
        public double ElapsedSeconds { get { lock (gate) return clock.Elapsed.TotalSeconds; } }

        public ClickEngine(IClickOutput output) : this(output, new Random()) { }
        public ClickEngine(IClickOutput output, Random random) { this.output = output; this.random = random; }

        public void Start(ClickSettings source, int startDelayMs)
        {
            var settings = source.Copy();
            string error = settings.Validate(true);
            if (error != null) throw new ArgumentException(error);
            if (startDelayMs < 0 || startDelayMs > 10000) throw new ArgumentOutOfRangeException("startDelayMs");
            lock (gate)
            {
                if (disposed) throw new ObjectDisposedException("ClickEngine");
                if (running || (worker != null && worker.IsAlive)) throw new InvalidOperationException("The previous run is still stopping.");
                Interlocked.Exchange(ref count, 0);
                LastError = null;
                StopReason = "Stopped";
                clock.Reset();
                stop.Reset();
                running = true;
                worker = new Thread(() => Run(settings, startDelayMs));
                worker.Name = "Vanta click engine";
                worker.IsBackground = true;
                worker.Start();
            }
        }

        public void Stop() { lock (gate) { if (!disposed) stop.Set(); } }

        public bool WaitForStop(int timeoutMs)
        {
            Thread thread;
            lock (gate) thread = worker;
            return thread == null || thread.Join(timeoutMs);
        }

        public static double VaryInterval(double intervalMs, double percent, double sample)
        {
            return Math.Max(1, intervalMs * (1 + (sample * 2 - 1) * percent / 100));
        }

        private void Run(ClickSettings settings, int startDelayMs)
        {
            bool pressed = false;
            bool timerActive = false;
            double deadline = settings.LimitEnabled && settings.Limit == LimitMode.Seconds ? settings.LimitValue * 1000 : Double.PositiveInfinity;
            long maximum = settings.LimitEnabled && settings.Limit == LimitMode.Clicks ? (long)settings.LimitValue : Int64.MaxValue;
            try
            {
                if (stop.WaitOne(startDelayMs)) return;
                timerActive = NativeMethods.timeBeginPeriod(1) == 0;
                lock (gate) clock.Restart();
                int pointIndex = 0;
                while (!stop.WaitOne(0) && Clicks < maximum && clock.Elapsed.TotalMilliseconds < deadline)
                {
                    double start = clock.Elapsed.TotalMilliseconds;
                    double interval = settings.VariationEnabled ? VaryInterval(settings.IntervalMs, settings.VariationPercent, random.NextDouble()) : settings.IntervalMs;
                    int repetitions = settings.DoubleClickEnabled ? 2 : 1;
                    // A double click is one cycle with two clicks. A long gap extends the cycle.
                    double gap = settings.DoubleClickEnabled ? settings.DoubleClickGapMs : 0;
                    double hold = Math.Max(0, (interval - gap) / repetitions) * settings.DurationPercent / 100;
                    if (settings.SequenceEnabled) output.MoveTo(settings.Points[pointIndex]);
                    for (int click = 0; click < repetitions && Clicks < maximum; click++)
                    {
                        if (stop.WaitOne(0) || clock.Elapsed.TotalMilliseconds >= deadline) break;
                        output.Press(settings.Button);
                        pressed = true;
                        bool completed = WaitUntil(Math.Min(clock.Elapsed.TotalMilliseconds + hold, deadline));
                        output.Release(settings.Button);
                        pressed = false;
                        Interlocked.Increment(ref count);
                        if (!completed || clock.Elapsed.TotalMilliseconds >= deadline) break;
                        if (click + 1 < repetitions && Clicks < maximum && !WaitUntil(Math.Min(clock.Elapsed.TotalMilliseconds + gap, deadline))) break;
                    }
                    if (settings.SequenceEnabled) pointIndex = (pointIndex + 1) % settings.Points.Count;
                    if (Clicks >= maximum) { StopReason = "Click limit reached"; break; }
                    if (!WaitUntil(Math.Min(start + interval, deadline))) break;
                }
                if (!stop.WaitOne(0) && clock.Elapsed.TotalMilliseconds >= deadline) StopReason = "Time limit reached";
            }
            catch (Exception ex)
            {
                LastError = ex.Message;
                StopReason = "Stopped safely";
            }
            finally
            {
                if (pressed)
                {
                    try { output.Release(settings.Button); }
                    catch (Exception ex) { LastError = "Could not release the mouse button: " + ex.Message; }
                }
                if (timerActive) NativeMethods.timeEndPeriod(1);
                lock (gate) clock.Stop();
                running = false;
                var handler = Finished;
                if (handler != null) handler();
            }
        }

        private bool WaitUntil(double targetMs)
        {
            while (true)
            {
                if (stop.WaitOne(0)) return false;
                double remaining = targetMs - clock.Elapsed.TotalMilliseconds;
                if (remaining <= 0) return true;
                // Cancellable waits keep long delays and mouse-down durations interruptible.
                if (stop.WaitOne((int)Math.Max(1, Math.Min(Int32.MaxValue, Math.Ceiling(remaining))))) return false;
            }
        }

        public void Dispose()
        {
            Stop();
            if (!WaitForStop(3000)) return; // Never dispose a wait handle still used by the worker.
            lock (gate)
            {
                if (disposed) return;
                disposed = true;
                stop.Dispose();
            }
        }
    }
}
