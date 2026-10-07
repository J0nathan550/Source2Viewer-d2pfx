using System.Threading;
using GUI.Utils;

namespace GUI.Types.GLViewers
{
    /// <summary>
    /// A single thread that draws whichever GL viewer is currently visible.
    /// </summary>
    static class RenderLoopThread
    {
        private static int threadHash;
        private static int instances;
        private static Thread? loopThread;
        private static GLBaseControl? currentGLControl;
        private static readonly ManualResetEventSlim renderSignal = new(initialState: true);
        private static readonly HashSet<object> activeWindows = [];
        private static volatile bool appActive = true;

        /// <summary>Tracks whether one of our windows has focus, rendering pauses while the app is in the background.</summary>
        public static void SetWindowActive(object window, bool active)
        {
            lock (activeWindows)
            {
                if (active)
                {
                    activeWindows.Add(window);
                }
                else
                {
                    activeWindows.Remove(window);
                }

                // Focus moving between our own windows reports deactivation before activation
                appActive = activeWindows.Count > 0;
            }

            if (active && currentGLControl != null)
            {
                renderSignal.Set();
            }
        }

        public static void RegisterInstance()
        {
            if (Interlocked.Increment(ref instances) == 1)
            {
                renderSignal.Reset();
                Start();
            }

#if DEBUG
            Log.Debug(nameof(RenderLoop), $"Registered GL instance, current count: {instances}");
#endif
        }

        public static void UnregisterInstance()
        {
            if (Interlocked.Decrement(ref instances) == 0)
            {
                Interlocked.Increment(ref threadHash);
                renderSignal.Set();
                loopThread = null; // The thread should quit on its own

                var detached = Interlocked.Exchange(ref currentGLControl, null);
                detached?.OnDetachedFromRenderLoop();
            }

#if DEBUG
            Log.Debug(nameof(RenderLoop), $"Unregistered GL instance, current count: {instances}");
#endif
        }

        public static bool IsCurrentGLControl(GLBaseControl glControl) => currentGLControl == glControl;

        public static void SetCurrentGLControl(GLBaseControl glControl)
        {
            var originalGlControl = Interlocked.Exchange(ref currentGLControl, glControl);

            if (loopThread == null)
            {
                Start();
            }

            if (originalGlControl != null && originalGlControl != glControl)
            {
                originalGlControl.OnDetachedFromRenderLoop();
            }

            renderSignal.Set();
        }

        public static void UnsetCurrentGLControl(GLBaseControl glControl)
        {
            Interlocked.CompareExchange(ref currentGLControl, null, glControl);

            glControl.OnDetachedFromRenderLoop();

            // With no instances left the loop has been told to quit.
            if (currentGLControl == null && Volatile.Read(ref instances) > 0)
            {
                renderSignal.Reset();
            }
        }

        private static void Start()
        {
            loopThread = new Thread(RenderLoop)
            {
                Name = nameof(RenderLoop),
                IsBackground = true,
                Priority = ThreadPriority.AboveNormal,
            };
            loopThread.Start();
        }

        private static void RenderLoop()
        {
            var localHash = threadHash;

#if DEBUG
            Log.Debug(nameof(RenderLoop), $"Thread started (#{localHash})");
#endif

            while (threadHash == localHash)
            {
                var control = currentGLControl;

                if (control == null)
                {
                    renderSignal.Wait();
                    continue;
                }

                if (control.TryPrewarm())
                {
                    continue;
                }

                if (control.GLControl is not { IsShown: true } viewport)
                {
                    // The viewport was removed from the visible UI, for example its tab was switched away
                    UnsetCurrentGLControl(control);
                    continue;
                }

                var isPaused = !renderSignal.IsSet;

                if (!isPaused && !appActive)
                {
                    isPaused = true;
                    renderSignal.Reset();
                }

                bool presented;

                try
                {
                    presented = control.Draw(isPaused);
                }
                catch (Exception e)
                {
                    // A broken frame must not kill the render thread for every other viewer
                    Log.Error(nameof(RenderLoop), e.ToString());
                    UnsetCurrentGLControl(control);
                    continue;
                }

                // Outside of the GL lock, so the UI thread can still take it while the frame is shown
                if (presented && Settings.Config.Vsync != 0)
                {
                    viewport.WaitForPresentation();
                }

                if (!renderSignal.IsSet)
                {
                    if (threadHash != localHash)
                    {
                        break;
                    }

                    renderSignal.Wait();
                    continue;
                }

                if (!presented)
                {
                    Thread.Sleep(1);
                }
            }

#if DEBUG
            Log.Debug(nameof(RenderLoop), $"Thread quit (#{localHash})");
#endif
        }
    }
}
