using System.Diagnostics;
using System.Drawing;
using System.Threading;
using System.Windows.Forms;
using Avalonia.Input;
using Avalonia.Threading;
using GUI.Controls;
using GUI.Utils;
using OpenTK.Graphics.OpenGL;
using OpenTK.Windowing.Common;
using OpenTK.Windowing.Desktop;
using ValveResourceFormat.Renderer;
using ValveResourceFormat.Renderer.Input;
using AvaloniaControl = Avalonia.Controls.Control;
using AvaloniaCursor = Avalonia.Input.Cursor;

namespace GUI.Types.GLViewers;

/// <summary>
/// Base of every GL viewer. Owns a hidden GLFW window whose context the viewer renders with, and an Avalonia
/// <see cref="GLViewport"/> that shows the frames and supplies input. Derived viewers are shared with the
/// WinForms GUI, so the protected surface here matches the WinForms GLBaseControl.
/// </summary>
internal abstract class GLBaseControl : IDisposable
{
    protected RendererControl? UiControl;

    protected NativeWindow? GLNativeWindow;

    /// <summary>The command stream this control records into, over <see cref="GLNativeWindow"/>.</summary>
    protected GraphicsContext? GraphicsContext;

    public GLViewport? GLControl { get; private set; }

    private Avalonia.Controls.Window? fullScreenWindow;
    public bool IsFullScreen => fullScreenWindow != null;

    public bool MouseOverRenderArea;
    public Point MouseDelta;
    protected Point MousePreviousPosition;
    protected Point InitialMousePosition;
    protected TrackedKeys CurrentlyPressedKeys;
    public Point LastMouseDelta { get; protected set; }

    /// <summary>Whether the mouse has moved while a button was held since the last mouse down.</summary>
    protected bool MouseDragged;

    /// <summary>
    /// Set when the viewport lets go of the cursor (focus lost, or escape in walk mode) so mouse look
    /// does not take it straight back; cleared by clicking back into the viewport.
    /// </summary>
    protected bool MouseReleased;

    private readonly Lock inputStateLock = new();
    private Point pendingMouseDelta;
    private int pendingMouseWheelDelta;
    private bool cursorHiddenForDrag;
    private bool currentDragIsTouch;
    private bool mouseLookNeedsRebase;
    private Point mouseLookRestorePosition;

    public bool GrabbedMouse
    {
        get;
        set
        {
            if (field != value)
            {
                if (value)
                {
                    mouseLookNeedsRebase = true;
                    Dispatcher.UIThread.Post(() => HideCursorForMouseLook(MousePreviousPosition));
                }
                else
                {
                    Dispatcher.UIThread.Post(RestoreCursorAfterDrag);
                }
            }

            field = value;
        }
    }

#if DEBUG
    public ShaderHotReload ShaderHotReload;
    public static ContextFlags Flags => ContextFlags.ForwardCompatible | ContextFlags.Debug;
#else
    public static ContextFlags Flags => ContextFlags.ForwardCompatible;
#endif

    protected readonly Lock glLock = new();

    private int MaxSamples;
    protected int NumSamples => Math.Max(1, Math.Min(Settings.Config.AntiAliasingSamples, MaxSamples));

    private bool FirstPaint = true;
    public long LastUpdate { get; protected set; }
    private long lastPresentTimestamp;
    public bool Paused = true;

    /// <summary>
    /// Pauses this control.
    /// </summary>
    public virtual void OnDetachedFromRenderLoop()
    {
        Paused = true;

        if (PrewarmPending)
        {
            PrewarmPending = false;
            prewarmed.Set();
        }
    }
    protected long lastFpsUpdate;

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Usage", "CA2213:Disposable fields should be disposed", Justification = "RendererContext is disposed in Dispose method")]
    protected RendererContext RendererContext;

    /// <summary>
    /// What the viewer presents into. On WinForms this is the window's default framebuffer, here it is an
    /// offscreen framebuffer object that <see cref="GLViewport"/> reads back.
    /// </summary>
    protected Framebuffer? GLDefaultFramebuffer;
    protected Framebuffer? MainFramebuffer;

    public GLBaseControl(RendererContext rendererContext)
    {
        LastUpdate = Stopwatch.GetTimestamp();
        RendererContext = rendererContext;

#if DEBUG
        ShaderHotReload = new ShaderHotReload(this, rendererContext.ShaderLoader);
#endif
    }

    public AvaloniaControl InitializeUiControls(bool isPreview = false)
    {
        GLControl = new GLViewport(this);

        UiControl = new RendererControl(isPreview);
        UiControl.GLControlContainer.Children.Add(GLControl);

#if DEBUG
        ShaderHotReload.Start();

        // We want reload shaders to be the top most button
        if (ShowReloadShadersButton)
        {
            var button = new Avalonia.Controls.Button { Content = "Reload shaders" };
            button.Click += (_, _) => ShaderHotReload.ReloadShaders();
            UiControl.AddControl(button);
        }
#endif

        AddUiControls();

        return UiControl;
    }

    /// <summary>
    /// Force the GL control to redraw. Used when the viewer becomes visible after being obscured (e.g. by a
    /// loading panel).
    /// </summary>
    public virtual void NotifyVisible()
    {
        GLControl?.Invalidate();
    }

    /// <summary>Whether the debug sidebar gets the shader hot-reload button; viewers that never
    /// compile scene shaders turn it off.</summary>
    protected virtual bool ShowReloadShadersButton => true;

    protected virtual void AddUiControls()
    {
        // Implemented in derived classes
    }

    internal void HandleKeyDown(KeyEventArgs e)
    {
        System.Windows.Forms.Control.UpdateModifierKeys(e.KeyModifiers);

        var key = KeyMapping.ToKeys(e.Key);

        // Swallow everything so arrows and tab do not move focus out of the viewport
        e.Handled = true;

        if (key == Keys.None)
        {
            return;
        }

        OnKeyDown(key | KeyMapping.ToModifiers(e.KeyModifiers));
    }

    internal void HandleKeyUp(KeyEventArgs e)
    {
        System.Windows.Forms.Control.UpdateModifierKeys(e.KeyModifiers);

        var key = KeyMapping.ToKeys(e.Key);
        e.Handled = true;

        if (key != Keys.None)
        {
            OnKeyUp(key);
        }
    }

    protected virtual void OnKeyDown(Keys keyData)
    {
        var keyCode = keyData & Keys.KeyCode;

        using (inputStateLock.EnterScope())
        {
            CurrentlyPressedKeys |= RemapKey(keyCode);
        }

        if (keyData == (Keys.Control | Keys.C))
        {
            Program.MainForm.SetStatus("Copying image to clipboard...");

            using var bitmap = ReadPixelsToBitmap();
            if (bitmap == null)
            {
                Log.Error(nameof(GLBaseControl), "Failed to copy image to clipboard, bitmap was null");
            }
            else
            {
                AppClipboard.SetImage(bitmap);
            }

            Program.MainForm.SetStatus("Copied image to clipboard");

            return;
        }

        if ((keyCode == Keys.Escape || keyCode == Keys.F11) && fullScreenWindow != null)
        {
            fullScreenWindow.Close();
            return;
        }

        if (keyCode == Keys.F11)
        {
            EnterFullScreen();
        }
    }

    protected virtual void OnKeyUp(Keys keyCode)
    {
        using var _ = inputStateLock.EnterScope();
        CurrentlyPressedKeys &= ~RemapKey(keyCode);
    }

    private void EnterFullScreen()
    {
        if (GLControl == null || UiControl == null)
        {
            return;
        }

        UiControl.GLControlContainer.Children.Remove(GLControl);

        fullScreenWindow = new Avalonia.Controls.Window
        {
            Title = "Source 2 Viewer Fullscreen",
            Icon = Program.MainForm.Icon,
            WindowState = Avalonia.Controls.WindowState.FullScreen,
            WindowDecorations = Avalonia.Controls.WindowDecorations.None,
            Content = GLControl,
        };

        fullScreenWindow.Activated += (_, _) => RenderLoopThread.SetWindowActive(fullScreenWindow, true);
        fullScreenWindow.Deactivated += (_, _) => RenderLoopThread.SetWindowActive(fullScreenWindow, false);
        fullScreenWindow.Closed += OnFullScreenWindowClosed;
        fullScreenWindow.Show(Program.MainForm);
        GLControl.Focus();
    }

    protected void ExitFullScreen() => fullScreenWindow?.Close();

    private void OnFullScreenWindowClosed(object? sender, EventArgs e)
    {
        if (sender is Avalonia.Controls.Window window)
        {
            window.Closed -= OnFullScreenWindowClosed;
            RenderLoopThread.SetWindowActive(window, false);
            window.Content = null;
        }

        fullScreenWindow = null;

        if (GLControl != null && UiControl != null)
        {
            UiControl.GLControlContainer.Children.Add(GLControl);
            GLControl.Focus();
        }
    }

    protected virtual void OnResize(int w, int h)
    {
        if (w <= 0 || h <= 0)
        {
            return;
        }

        if (GLDefaultFramebuffer is null || MainFramebuffer is null)
        {
            return;
        }

        GLDefaultFramebuffer.Resize(w, h);

        if (MainFramebuffer != GLDefaultFramebuffer)
        {
            MainFramebuffer.Resize(w, h, NumSamples);
        }
    }

    public void OnLostFocus(object? sender, EventArgs e)
    {
        CurrentlyPressedKeys = TrackedKeys.None;
        MouseDelta = Point.Empty;
        currentDragIsTouch = false;
        mouseLookNeedsRebase = true;
        MouseReleased = true;
        GrabbedMouse = false;
        RestoreCursorAfterDrag();
        OnViewportLostFocus();
    }

    /// <summary>Called on the UI thread when the viewport loses keyboard focus.</summary>
    protected virtual void OnViewportLostFocus()
    {
    }

    private static TrackedKeys RemapKey(Keys key) => key switch
    {
        Keys.W => TrackedKeys.W,
        Keys.A => TrackedKeys.A,
        Keys.S => TrackedKeys.S,
        Keys.D => TrackedKeys.D,
        Keys.Q => TrackedKeys.Q,
        Keys.Z => TrackedKeys.Z,
        Keys.Up => TrackedKeys.W,
        Keys.Down => TrackedKeys.S,
        Keys.Left => TrackedKeys.A,
        Keys.Right => TrackedKeys.D,
        Keys.ControlKey => TrackedKeys.Control,
        Keys.ShiftKey or Keys.LShiftKey => TrackedKeys.Shift,
        Keys.Menu or Keys.LMenu => TrackedKeys.Alt,
        Keys.Space => TrackedKeys.Space,
        Keys.X => TrackedKeys.X,
        Keys.D1 => TrackedKeys.Slot1,
        Keys.D2 => TrackedKeys.Slot2,
        Keys.D3 => TrackedKeys.Slot3,
        Keys.D4 => TrackedKeys.Slot4,
        Keys.E => TrackedKeys.E,
        Keys.F => TrackedKeys.F,
        Keys.Escape => TrackedKeys.Escape,
        _ => TrackedKeys.None,
    };

    public virtual void Dispose()
    {
        RendererContext.CancelLoading();

        using var lockedGl = glLock.EnterScope();

        RenderLoopThread.UnregisterInstance();

        if (GLControl is not null)
        {
            RenderLoopThread.UnsetCurrentGLControl(this);

            var viewport = GLControl;
            Dispatcher.UIThread.Post(() =>
            {
                RestoreCursorAfterDrag();
                viewport.DisposeFrames();
            });
        }

#if DEBUG
        ShaderHotReload?.Dispose();
#endif

        prewarmed.Dispose();

        if (fullScreenWindow is { } window)
        {
            Dispatcher.UIThread.Post(window.Close);
        }

        DestroyNativeWindow();
        RendererContext.Dispose();
    }

    private void DestroyNativeWindow()
    {
        var window = GLNativeWindow;
        GLNativeWindow = null;

        if (window == null)
        {
            return;
        }

        // GLFW windows must be destroyed on the thread that created them, which is the UI thread
        if (Dispatcher.UIThread.CheckAccess())
        {
            NativeWindowFactory.Destroy(window);
        }
        else
        {
            Dispatcher.UIThread.Post(() => NativeWindowFactory.Destroy(window));
        }
    }

    internal void HandlePointerEntered()
    {
        MouseOverRenderArea = true;
    }

    internal void HandlePointerExited()
    {
        MouseOverRenderArea = false;
    }

    private static MouseButtons ToMouseButton(PointerUpdateKind kind) => kind switch
    {
        PointerUpdateKind.LeftButtonPressed or PointerUpdateKind.LeftButtonReleased => MouseButtons.Left,
        PointerUpdateKind.RightButtonPressed or PointerUpdateKind.RightButtonReleased => MouseButtons.Right,
        PointerUpdateKind.MiddleButtonPressed or PointerUpdateKind.MiddleButtonReleased => MouseButtons.Middle,
        PointerUpdateKind.XButton1Pressed or PointerUpdateKind.XButton1Released => MouseButtons.XButton1,
        PointerUpdateKind.XButton2Pressed or PointerUpdateKind.XButton2Released => MouseButtons.XButton2,
        _ => MouseButtons.None,
    };

    internal void HandlePointerPressed(PointerPressedEventArgs e)
    {
        Debug.Assert(GLControl != null);

        System.Windows.Forms.Control.UpdateModifierKeys(e.KeyModifiers);

        var point = e.GetCurrentPoint(GLControl);
        var button = ToMouseButton(point.Properties.PointerUpdateKind);
        var position = GLControl.ToPixels(point.Position);

        currentDragIsTouch = e.Pointer.Type != PointerType.Mouse;

        if (button is MouseButtons.Left or MouseButtons.Right)
        {
            e.Pointer.Capture(GLControl);
        }

        GLControl.Focus();
        e.Handled = true;

        OnMouseDown(GLControl, new MouseEventArgs(button, e.ClickCount, position.X, position.Y, 0));
    }

    internal void HandlePointerReleased(PointerReleasedEventArgs e)
    {
        Debug.Assert(GLControl != null);

        System.Windows.Forms.Control.UpdateModifierKeys(e.KeyModifiers);

        var point = e.GetCurrentPoint(GLControl);
        var button = ToMouseButton(point.Properties.PointerUpdateKind);
        var position = GLControl.ToPixels(point.Position);

        OnMouseUp(GLControl, new MouseEventArgs(button, 1, position.X, position.Y, 0));

        if ((CurrentlyPressedKeys & TrackedKeys.MouseLeftOrRight) == 0)
        {
            e.Pointer.Capture(null);
        }
    }

    internal void HandlePointerCaptureLost()
    {
        using var _ = inputStateLock.EnterScope();

        CurrentlyPressedKeys &= ~TrackedKeys.MouseLeftOrRight;
        pendingMouseDelta = Point.Empty;
        mouseLookNeedsRebase = true;

        if (!GrabbedMouse)
        {
            RestoreCursorAfterDrag();
        }
    }

    internal void HandlePointerMoved(PointerEventArgs e)
    {
        Debug.Assert(GLControl != null);

        System.Windows.Forms.Control.UpdateModifierKeys(e.KeyModifiers);

        var position = GLControl.ToPixels(e.GetPosition(GLControl));
        OnMouseMove(position.X, position.Y);
    }

    internal void HandlePointerWheel(PointerWheelEventArgs e)
    {
        Debug.Assert(GLControl != null);

        System.Windows.Forms.Control.UpdateModifierKeys(e.KeyModifiers);

        // WinForms reports 120 per notch
        var delta = (int)Math.Round(e.Delta.Y * 120);

        if (delta != 0)
        {
            OnMouseWheel(delta, GLControl.ToPixels(e.GetPosition(GLControl)));
        }

        e.Handled = true;
    }

    protected virtual void OnMouseDown(object? sender, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left && e.Button != MouseButtons.Right)
        {
            return;
        }

        InitialMousePosition = new Point(e.X, e.Y);
        MouseDelta = Point.Empty;
        MouseDragged = false;
        MouseReleased = false;
        mouseLookNeedsRebase = true;
        MousePreviousPosition = InitialMousePosition;

        if (e.Button == MouseButtons.Left)
        {
            CurrentlyPressedKeys |= TrackedKeys.MouseLeft;
        }
        else if (e.Button == MouseButtons.Right)
        {
            CurrentlyPressedKeys |= TrackedKeys.MouseRight;
        }
    }

    protected virtual void OnMouseUp(object? sender, MouseEventArgs e)
    {
        using var _ = inputStateLock.EnterScope();
        if (e.Button == MouseButtons.Left)
        {
            CurrentlyPressedKeys &= ~TrackedKeys.MouseLeft;
        }
        else if (e.Button == MouseButtons.Right)
        {
            CurrentlyPressedKeys &= ~TrackedKeys.MouseRight;
        }

        if ((CurrentlyPressedKeys & TrackedKeys.MouseLeftOrRight) == 0)
        {
            pendingMouseDelta = Point.Empty;
            MouseDelta = Point.Empty;
            currentDragIsTouch = false;
            mouseLookNeedsRebase = true;

            if (!GrabbedMouse)
            {
                RestoreCursorAfterDrag();
            }
        }
    }

    private void HideCursorForMouseLook(Point restorePosition)
    {
        if (cursorHiddenForDrag || GLControl == null)
        {
            return;
        }

        cursorHiddenForDrag = true;
        mouseLookRestorePosition = restorePosition;
        mouseLookNeedsRebase = true;
        GLControl.Cursor = new AvaloniaCursor(StandardCursorType.None);
    }

    private void RestoreCursorAfterDrag()
    {
        if (!cursorHiddenForDrag)
        {
            return;
        }

        cursorHiddenForDrag = false;
        mouseLookNeedsRebase = true;

        if (GLControl != null)
        {
            CursorWarp.TryWarp(GLControl.PixelsToScreen(mouseLookRestorePosition));
            GLControl.Cursor = AvaloniaCursor.Default;
        }
    }

    private Point CenterOfRenderArea()
    {
        Debug.Assert(GLControl != null);
        return new Point(GLControl.PixelWidth / 2, GLControl.PixelHeight / 2);
    }

    protected virtual void OnMouseMove(int x, int y)
    {
        if (GLControl == null)
        {
            return;
        }

        var dragging = (CurrentlyPressedKeys & TrackedKeys.MouseLeftOrRight) != 0;
        var touch = currentDragIsTouch;

        if (!dragging && !(GrabbedMouse && !touch))
        {
            return;
        }

        using var _ = inputStateLock.EnterScope();

        var position = new Point(x, y);

        if (mouseLookNeedsRebase)
        {
            mouseLookNeedsRebase = false;
            MousePreviousPosition = position;
            return;
        }

        var delta = new Point(
            position.X - MousePreviousPosition.X,
            position.Y - MousePreviousPosition.Y
        );

        pendingMouseDelta.X += delta.X;
        pendingMouseDelta.Y += delta.Y;
        MousePreviousPosition = position;

        if (delta == Point.Empty)
        {
            return;
        }

        MouseDragged = true;

        if (touch)
        {
            // Touch and pen are absolute digitizers: warping the cursor doesn't move the contact point
            return;
        }

        HideCursorForMouseLook(new Point(position.X - delta.X, position.Y - delta.Y));

        // Relative mouse: pin the cursor so the look can continue past the screen edges
        var center = CenterOfRenderArea();

        if (CursorWarp.TryWarp(GLControl.PixelsToScreen(center)))
        {
            MousePreviousPosition = center;
        }
    }

    protected virtual void OnMouseWheel(int delta, Point location)
    {
        using var _ = inputStateLock.EnterScope();
        if (delta > 0)
        {
            CurrentlyPressedKeys |= TrackedKeys.MouseWheelUp;
        }
        else if (delta < 0)
        {
            CurrentlyPressedKeys |= TrackedKeys.MouseWheelDown;
        }

        pendingMouseWheelDelta += delta;
    }

    protected Point ConsumePendingMouseDelta()
    {
        using var _ = inputStateLock.EnterScope();
        var delta = pendingMouseDelta;
        pendingMouseDelta = Point.Empty;
        MouseDelta = Point.Empty;
        return delta;
    }

    protected int ConsumePendingMouseWheelDelta()
    {
        using var _ = inputStateLock.EnterScope();
        var wheelDelta = pendingMouseWheelDelta;
        pendingMouseWheelDelta = 0;
        return wheelDelta;
    }

    protected TrackedKeys ConsumeCurrentlyPressedKeysForUpdate()
    {
        using var _ = inputStateLock.EnterScope();
        var keys = CurrentlyPressedKeys;

        // Clear mouse wheel events after processing (they're one-time events)
        CurrentlyPressedKeys &= ~(TrackedKeys.MouseWheelUp | TrackedKeys.MouseWheelDown);

        return keys;
    }

    /// <summary>Makes this the viewer the render loop draws. Called on the UI thread when the viewport is shown.</summary>
    public void AttachToRenderLoop()
    {
        if (GraphicsContext == null || GLControl is not { IsShown: true })
        {
            return;
        }

        if (!RenderLoopThread.IsCurrentGLControl(this))
        {
            ApplySettingsToRenderState();
        }

        RenderLoopThread.SetCurrentGLControl(this);
    }

    /// <summary>Push user settings into the render state.</summary>
    private void ApplySettingsToRenderState()
    {
        using var lockedGl = MakeCurrent();

        if (this is GLSceneViewer viewer)
        {
            RendererContext.FieldOfView = Settings.Config.FieldOfView;
            RendererContext.ViewmodelFieldOfView = Settings.Config.ViewmodelFieldOfView;
            viewer.Renderer.Camera.FieldOfView = Settings.Config.FieldOfView;
            viewer.Renderer.Camera.CreateProjectionMatrix();

            // The input camera frames objects using its own field of view, so it follows the setting too
            viewer.Input.Camera.FieldOfView = Settings.Config.FieldOfView;
            viewer.Input.Camera.CreateProjectionMatrix();
        }
    }

    protected bool ShouldResize;

    protected bool SkipBufferSwap;

    internal void OnViewportSizeChanged() => OnSizeChanged(GLControl, EventArgs.Empty);

    protected virtual void OnSizeChanged(object? sender, EventArgs e)
    {
        ShouldResize = GLControl is not null && GLControl.PixelWidth > 0 && GLControl.PixelHeight > 0;
    }

    protected virtual void OnFirstPaint()
    {
        var elapsed = Stopwatch.GetElapsedTime(LastUpdate, Stopwatch.GetTimestamp());

        Log.Debug(nameof(GLBaseControl), $"First paint: {elapsed}");
    }

    protected virtual void OnUpdate(float frameTime)
    {
        //
    }

    protected virtual void OnPaint(float frameTime)
    {
        //
    }

    public void InitializeLoad()
    {
        using var loading = RendererContext.BeginLoading();

        InitializeLoadCore();
    }

    private void InitializeLoadCore()
    {
        // GLFW requires windows to be created on the main thread, which is the Avalonia UI thread.
        // The window is never shown, it only exists to own the GL context. Loading then makes the
        // context current on the calling (background) thread, and the render loop thread takes it over
        // afterwards. A context may only be current on one thread at a time, glLock enforces that.
        Dispatcher.UIThread.Invoke(() =>
        {
            Debug.Assert(GLNativeWindow is null);

            var settings = new NativeWindowSettings()
            {
                APIVersion = GLEnvironment.RequiredVersion,
                Flags = Flags,
                Profile = ContextProfile.Core,
                RedBits = 8,
                GreenBits = 8,
                BlueBits = 8,
                AlphaBits = 0,
                DepthBits = 0,
                StencilBits = 0,
                StartFocused = false,
                StartVisible = false,
                ClientSize = new(4, 4),
                AutoLoadBindings = false,
                AutoIconify = false,
                WindowBorder = OpenTK.Windowing.Common.WindowBorder.Hidden,
                WindowState = OpenTK.Windowing.Common.WindowState.Normal,
                Title = "Source 2 Viewer OpenGL",
            };
            GLNativeWindow = NativeWindowFactory.Create(settings);

            GLNativeWindow.Context.MakeNoneCurrent();
        });

        Debug.Assert(GLNativeWindow is not null);

        RenderLoopThread.RegisterInstance();

        GraphicsContext = RendererContext.Device.CreateContext(new GLFWSurface(GLNativeWindow.Context));

        LoadGLResources();

        if (PrewarmsRenderer)
        {
            PrewarmPending = true;

            RenderLoopThread.SetCurrentGLControl(this);
            prewarmed.Wait();
            RenderLoopThread.UnsetCurrentGLControl(this);
        }
    }

    private void LoadGLResources()
    {
        Debug.Assert(GLNativeWindow is not null);

        using var lockedGl = MakeCurrent();

        if (!loadedBindings)
        {
            LoadOpenGLBindings();
            loadedBindings = true;
        }

        GL.Enable(EnableCap.DebugOutput);
        GL.DebugMessageCallback(OpenGLDebugMessageDelegate, IntPtr.Zero);

#if DEBUG
        GL.Enable(EnableCap.DebugOutputSynchronous);

        // Filter out performance warnings
        GL.DebugMessageControl(DebugSourceControl.DebugSourceApi, DebugTypeControl.DebugTypeOther, DebugSeverityControl.DebugSeverityNotification, 0, Array.Empty<int>(), false);

        // Filter out debug group push/pops
        GL.DebugMessageControl(DebugSourceControl.DebugSourceApplication, DebugTypeControl.DontCare, DebugSeverityControl.DebugSeverityNotification, 0, Array.Empty<int>(), false);
#else
        // Only log high severity messages in release builds
        GL.DebugMessageControl(DebugSourceControl.DontCare, DebugTypeControl.DontCare, DebugSeverityControl.DontCare, 0, Array.Empty<int>(), false);
        GL.DebugMessageControl(DebugSourceControl.DontCare, DebugTypeControl.DontCare, DebugSeverityControl.DebugSeverityHigh, 0, Array.Empty<int>(), true);
#endif

        GLEnvironment.Initialize(VrfGuiContext.Logger);
        GLEnvironment.SetDefaultRenderState();

        MaxSamples = GL.GetInteger(GetPName.MaxSamples);

        // Presentation target, read back by the viewport. It plays the part of the window's default framebuffer.
        GLDefaultFramebuffer = Framebuffer.Prepare(nameof(GLDefaultFramebuffer), 4, 4, 0, ImageFormat.RGBA8888, null);
        GLDefaultFramebuffer.ClearMask = ClearBufferMask.ColorBufferBit;
        GLDefaultFramebuffer.Initialize();

        // Framebuffer used to draw geometry
        MainFramebuffer = Framebuffer.Prepare(nameof(MainFramebuffer),
            4, 4,
            NumSamples,
            ImageFormat.RGBA16161616F,
            ImageFormat.D32
        );

        MainFramebuffer.Initialize();

        OnGLLoad();

        RendererContext.ShaderLoader.LinkLoadedShaders();
    }

    /// <summary>Reports how long presenting the frame blocked the render thread.</summary>
    protected virtual void OnBufferSwapped(double blockedMs, double framePeriodMs) { }

    protected virtual void OnGLLoad()
    {
        //
    }

    /// <summary>Whether loading waits for the warm-up; such viewers must be loaded off the UI thread.</summary>
    protected virtual bool PrewarmsRenderer => false;

    protected virtual void PrewarmRenderer()
    {
    }

    private readonly ManualResetEventSlim prewarmed = new(false);

    private bool PrewarmPending { get; set; }

    /// <summary>Runs the warm-up loading is waiting on, if any, and reports whether it did. Called by the
    /// render loop thread, which is the one the driver has to see the draws come from.</summary>
    public bool TryPrewarm()
    {
        if (!PrewarmPending)
        {
            return false;
        }

        try
        {
            using var lockedGl = MakeCurrent();
            PrewarmRenderer();
        }
        finally
        {
            PrewarmPending = false;
            prewarmed.Set();
        }

        return true;
    }

    protected void SetMoveSpeedOrZoomLabel(string text)
    {
        if (UiControl is { } uiControl)
        {
            Dispatcher.UIThread.Post(() => uiControl.SetMoveSpeed(text));
        }
    }

    private static void OnDebugMessage(DebugSource source, DebugType type, int id, DebugSeverity severity, int length, IntPtr pMessage, IntPtr pUserParam)
    {
        var severityStr = severity.ToString().Replace("DebugSeverity", string.Empty, StringComparison.Ordinal);
        var sourceStr = source.ToString().Replace("DebugSource", string.Empty, StringComparison.Ordinal);
        var typeStr = type.ToString().Replace("DebugType", string.Empty, StringComparison.Ordinal);
        var message = System.Runtime.InteropServices.Marshal.PtrToStringUTF8(pMessage, length);
        var error = $"[{severityStr} {sourceStr} {typeStr}] {message}";

        switch (type)
        {
            case DebugType.DebugTypeError: Log.Error("OpenGL", error); break;
            default: Log.Debug("OpenGL", error); break;
        }

#if DEBUG
        if (type == DebugType.DebugTypeError && source != DebugSource.DebugSourceShaderCompiler && Debugger.IsAttached)
        {
            Debugger.Break();
        }
#endif
    }

    protected static readonly DebugProc OpenGLDebugMessageDelegate = OnDebugMessage;

    /// <summary>Renders a frame and hands it to the viewport. Called by the render loop thread.</summary>
    public bool Draw(bool isPaused)
    {
        using var lockedGl = glLock.EnterScope();

        if (GLNativeWindow == null || GraphicsContext == null || GLControl is not { } viewport)
        {
            Log.Debug(nameof(GLBaseControl), "Attempted to draw a destroyed GL viewer.");
            RenderLoopThread.UnsetCurrentGLControl(this);
            return false;
        }

        GraphicsContext.Begin();

        try
        {
            if (ShouldResize)
            {
                OnResize(viewport.PixelWidth, viewport.PixelHeight);
                ShouldResize = false;
            }

            var firstDraw = FirstPaint;

            if (firstDraw)
            {
                OnFirstPaint();
                FirstPaint = false;
            }

            var wasPaused = Paused;
            var resumingRender = wasPaused && !isPaused;
            Paused = isPaused;

            var currentTime = Stopwatch.GetTimestamp();
            var elapsed = resumingRender
                ? TimeSpan.Zero
                : Stopwatch.GetElapsedTime(LastUpdate, currentTime);

            // Clamp frametime so it does not cause issues in things like particle rendering
            var frameTime = MathF.Min(1f, (float)elapsed.TotalSeconds);
            LastUpdate = currentTime;
            OnUpdate(frameTime);

            OnPaint(frameTime);

            if (SkipBufferSwap)
            {
                SkipBufferSwap = false;
                return false;
            }

            var presentStart = Stopwatch.GetTimestamp();
            PresentFrame(viewport);
            var presentEnd = Stopwatch.GetTimestamp();

            var framePeriodMs = isPaused || resumingRender || lastPresentTimestamp == 0
                ? 0.0
                : Stopwatch.GetElapsedTime(lastPresentTimestamp, presentEnd).TotalMilliseconds;

            OnBufferSwapped(Stopwatch.GetElapsedTime(presentStart, presentEnd).TotalMilliseconds, framePeriodMs);

            lastPresentTimestamp = presentEnd;

            if (firstDraw)
            {
                LastUpdate = Stopwatch.GetTimestamp();
            }

            return true;
        }
        finally
        {
            GraphicsContext.End();
        }
    }

    private void PresentFrame(GLViewport viewport)
    {
        if (GLDefaultFramebuffer is not { } presentation || !presentation.HasValidDimensions())
        {
            return;
        }

        var width = presentation.Width;
        var height = presentation.Height;

        presentation.Bind(FramebufferTarget.ReadFramebuffer);
        GL.ReadBuffer(ReadBufferMode.ColorAttachment0);
        GL.PixelStore(PixelStoreParameter.PackAlignment, 4);

        viewport.SubmitFrame(width, height, pixels => GL.ReadPixels(0, 0, width, height, PixelFormat.Bgra, PixelType.UnsignedByte, pixels));
    }

    protected virtual void BlitFramebufferToScreen()
    {
        //
    }

    public GLLockScope MakeCurrent()
    {
        if (GraphicsContext == null)
        {
            throw new InvalidOperationException("Cannot acquire GLLockScope without a valid GLNativeWindow.");
        }

        return new GLLockScope(glLock, GraphicsContext);
    }

    static bool loadedBindings;
    private static void LoadOpenGLBindings()
    {
        var provider = new OpenTK.Windowing.GraphicsLibraryFramework.GLFWBindingsContext();
        GL.LoadBindings(provider);
    }

    protected virtual SkiaSharp.SKBitmap? ReadPixelsToBitmap()
    {
        if (GLDefaultFramebuffer is null)
        {
            return null;
        }

        var bitmap = new SkiaSharp.SKBitmap(GLDefaultFramebuffer.Width, GLDefaultFramebuffer.Height, SkiaSharp.SKColorType.Bgra8888, SkiaSharp.SKAlphaType.Opaque);
        var pixels = bitmap.GetPixels(out var length);

        using var lockedGl = MakeCurrent();

        BlitFramebufferToScreen();

        GLDefaultFramebuffer.Bind(FramebufferTarget.ReadFramebuffer);
        GL.ReadBuffer(ReadBufferMode.ColorAttachment0);
        GL.ReadPixels(0, 0, GLDefaultFramebuffer.Width, GLDefaultFramebuffer.Height, PixelFormat.Bgra, PixelType.UnsignedByte, pixels);

        // Flip y
        using var canvas = new SkiaSharp.SKCanvas(bitmap);
        canvas.Scale(1, -1, 0, bitmap.Height / 2f);
        canvas.DrawBitmap(bitmap, new SkiaSharp.SKPoint(), SkiaSharp.SKSamplingOptions.Default);

        return bitmap;
    }
}
