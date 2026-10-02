using System.Runtime.InteropServices;
using System.Threading;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using Avalonia.VisualTree;
using GUI.Types.GLViewers;

namespace GUI.Controls;

/// <summary>
/// Shows the frames a <see cref="GLBaseControl"/> renders offscreen and forwards input to it.
///
/// The renderer draws into a framebuffer object on its own GL context, the frame is read back and drawn here
/// as a bitmap. That costs a copy per frame, but it works the same on every platform and windowing system,
/// where embedding a native GL window would need per platform reparenting that Wayland does not allow.
/// </summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1001:Types that own disposable fields should be disposable", Justification = "The render thread may still be waiting on frameConsumed while the viewport goes away, and the slim event holds no OS handle unless its WaitHandle is used")]
sealed class GLViewport : Control
{
    private const int BitmapCount = 3;

    private readonly GLBaseControl owner;
    private readonly WriteableBitmap?[] bitmaps = new WriteableBitmap?[BitmapCount];
    private WriteableBitmap? displayedBitmap;
    private int nextBitmap;

    // Written by the render thread, copied out by the UI thread
    private readonly Lock frameLock = new();
    private byte[] frameData = [];
    private int frameWidth;
    private int frameHeight;
    private bool frameDirty;
    private int presentQueued;
    private readonly ManualResetEventSlim frameConsumed = new(initialState: true);

    private volatile bool isShown;
    private volatile int pixelWidth;
    private volatile int pixelHeight;

    public GLViewport(GLBaseControl owner)
    {
        this.owner = owner;

        Focusable = true;
        ClipToBounds = true;
        Background = Brushes.Black;
    }

    public static readonly StyledProperty<IBrush?> BackgroundProperty = Panel.BackgroundProperty.AddOwner<GLViewport>();

    public IBrush? Background
    {
        get => GetValue(BackgroundProperty);
        set => SetValue(BackgroundProperty, value);
    }

    /// <summary>Whether the viewport is in a visible part of the UI. Safe to read from any thread.</summary>
    public bool IsShown => isShown;

    /// <summary>Size of the render target in physical pixels. Safe to read from any thread.</summary>
    public int PixelWidth => pixelWidth;

    /// <inheritdoc cref="PixelWidth"/>
    public int PixelHeight => pixelHeight;

    public double RenderScaling => TopLevel.GetTopLevel(this)?.RenderScaling ?? 1.0;

    /// <summary>Asks for a new frame, for example after something changed while the render loop was idle.</summary>
    public void Invalidate()
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            owner.AttachToRenderLoop();
        }
        else
        {
            Dispatcher.UIThread.Post(owner.AttachToRenderLoop);
        }
    }

    /// <summary>
    /// Called on the render thread with the GL context current: reads the finished frame through
    /// <paramref name="readPixels"/> (BGRA, bottom-up rows) and queues it for display.
    /// </summary>
    public void SubmitFrame(int width, int height, Action<nint> readPixels)
    {
        if (width <= 0 || height <= 0)
        {
            return;
        }

        using (frameLock.EnterScope())
        {
            var size = width * height * 4;

            if (frameData.Length != size)
            {
                frameData = GC.AllocateUninitializedArray<byte>(size, pinned: true);
            }

            readPixels(Marshal.UnsafeAddrOfPinnedArrayElement(frameData, 0));

            frameWidth = width;
            frameHeight = height;
            frameDirty = true;
        }

        frameConsumed.Reset();

        if (Interlocked.Exchange(ref presentQueued, 1) == 0)
        {
            Dispatcher.UIThread.Post(SchedulePresent, DispatcherPriority.Render);
        }
    }

    /// <summary>
    /// Called by the render loop outside of the GL lock: waits for the submitted frame to be shown, which paces
    /// rendering to the display refresh rate. The timeout keeps rendering going if the UI stalls or is hidden.
    /// </summary>
    public void WaitForPresentation()
    {
        frameConsumed.Wait(TimeSpan.FromMilliseconds(100));
    }

    private void SchedulePresent()
    {
        if (TopLevel.GetTopLevel(this) is { } topLevel)
        {
            topLevel.RequestAnimationFrame(_ => Present());
        }
        else
        {
            Present();
        }
    }

    private void Present()
    {
        Interlocked.Exchange(ref presentQueued, 0);

        try
        {
            using (frameLock.EnterScope())
            {
                if (!frameDirty)
                {
                    return;
                }

                frameDirty = false;

                var bitmap = bitmaps[nextBitmap];

                if (bitmap == null || bitmap.PixelSize.Width != frameWidth || bitmap.PixelSize.Height != frameHeight)
                {
                    bitmap?.Dispose();
                    bitmap = new WriteableBitmap(new PixelSize(frameWidth, frameHeight), new Avalonia.Vector(96, 96), PixelFormats.Bgra8888, AlphaFormat.Opaque);
                    bitmaps[nextBitmap] = bitmap;
                }

                using (var target = bitmap.Lock())
                {
                    var rowBytes = frameWidth * 4;

                    if (target.RowBytes == rowBytes)
                    {
                        Marshal.Copy(frameData, 0, target.Address, rowBytes * frameHeight);
                    }
                    else
                    {
                        for (var y = 0; y < frameHeight; y++)
                        {
                            Marshal.Copy(frameData, y * rowBytes, target.Address + y * target.RowBytes, rowBytes);
                        }
                    }
                }

                // Rotate so the compositor can still be drawing the previous frame while the next one is written
                displayedBitmap = bitmap;
                nextBitmap = (nextBitmap + 1) % BitmapCount;
            }

            InvalidateVisual();
        }
        finally
        {
            frameConsumed.Set();
        }
    }

    public override void Render(DrawingContext context)
    {
        var bounds = new Rect(Bounds.Size);

        if (Background is { } background)
        {
            context.FillRectangle(background, bounds);
        }

        if (displayedBitmap is not { } bitmap)
        {
            return;
        }

        // GL rows are bottom-up
        using (context.PushTransform(Matrix.CreateScale(1, -1) * Matrix.CreateTranslation(0, bounds.Height)))
        {
            context.DrawImage(bitmap, new Rect(bitmap.Size), bounds);
        }
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);

        isShown = true;
        UpdatePixelSize();

        if (TopLevel.GetTopLevel(this) is { } topLevel)
        {
            topLevel.ScalingChanged += OnScalingChanged;
        }

        owner.AttachToRenderLoop();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);

        isShown = false;

        if (e.RootVisual is TopLevel topLevel)
        {
            topLevel.ScalingChanged -= OnScalingChanged;
        }

        // Unblock a render thread waiting for this frame, it will notice the viewport is gone
        frameConsumed.Set();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == IsVisibleProperty)
        {
            isShown = IsVisible && this.IsAttachedToVisualTree();

            if (isShown)
            {
                owner.AttachToRenderLoop();
            }
        }
    }

    protected override void OnSizeChanged(SizeChangedEventArgs e)
    {
        base.OnSizeChanged(e);
        UpdatePixelSize();
    }

    private void OnScalingChanged(object? sender, EventArgs e) => UpdatePixelSize();

    private void UpdatePixelSize()
    {
        var scaling = RenderScaling;
        var width = Math.Max(0, (int)Math.Round(Bounds.Width * scaling));
        var height = Math.Max(0, (int)Math.Round(Bounds.Height * scaling));

        if (width == pixelWidth && height == pixelHeight)
        {
            return;
        }

        pixelWidth = width;
        pixelHeight = height;
        owner.OnViewportSizeChanged();
    }

    /// <summary>Converts a position in this control to framebuffer pixels.</summary>
    public System.Drawing.Point ToPixels(Point position)
    {
        var scaling = RenderScaling;
        return new System.Drawing.Point((int)(position.X * scaling), (int)(position.Y * scaling));
    }

    /// <summary>Converts framebuffer pixels to physical screen coordinates.</summary>
    public PixelPoint PixelsToScreen(System.Drawing.Point pixels)
    {
        var scaling = RenderScaling;
        return this.PointToScreen(new Point(pixels.X / scaling, pixels.Y / scaling));
    }

    protected override void OnPointerEntered(PointerEventArgs e)
    {
        base.OnPointerEntered(e);
        owner.HandlePointerEntered();
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        owner.HandlePointerExited();
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        owner.HandlePointerPressed(e);
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        owner.HandlePointerReleased(e);
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        owner.HandlePointerMoved(e);
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        owner.HandlePointerWheel(e);
    }

    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        base.OnPointerCaptureLost(e);
        owner.HandlePointerCaptureLost();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        owner.HandleKeyDown(e);
    }

    protected override void OnKeyUp(KeyEventArgs e)
    {
        base.OnKeyUp(e);
        owner.HandleKeyUp(e);
    }

    protected override void OnLostFocus(FocusChangedEventArgs e)
    {
        base.OnLostFocus(e);
        owner.OnLostFocus(this, EventArgs.Empty);
    }

    public void DisposeFrames()
    {
        Dispatcher.UIThread.VerifyAccess();

        displayedBitmap = null;

        for (var i = 0; i < bitmaps.Length; i++)
        {
            bitmaps[i]?.Dispose();
            bitmaps[i] = null;
        }

        frameConsumed.Set();
    }
}
