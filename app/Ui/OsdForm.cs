using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Windows.Forms;
using Nitrous.Enums;
using Nitrous.Helpers;
using Nitrous.Interop;
using Nitrous.Managers;

namespace Nitrous.Ui;

/// <summary>
/// Forged-metal OSD rendered through a layered window (<c>UpdateLayeredWindow</c>) so the
/// background can stay translucent (denser on the left, fading out to the right) while the
/// icon, divider, borders and text are always fully opaque. Runs its own UI-thread timers.
/// </summary>
public class OsdForm : Form
{
    private readonly System.Windows.Forms.Timer _fadeTimer;
    private readonly System.Windows.Forms.Timer _holdTimer;
    private string _profileText = "BALANCED";
    private Color _accentColor = OsdThemeHelper.GetAccent(PowerProfile.Balanced);
    private PowerProfile _activeProfile = PowerProfile.Balanced;

    // Steady-state layer alpha; only the fade-out animation lowers it.
    private byte _fadeAlpha = 255;

    private readonly Font _titleFont;
    private readonly Font _subtitleFont;
    private readonly SolidBrush _titleBrush;
    private readonly SolidBrush _subtitleBrush;
    private readonly SolidBrush _shadowBrush;
    private readonly Pen _outerBorderPen;
    private readonly Pen _innerBorderPen;
    private readonly StringFormat _textFormat;
    private SolidBrush _accentBrush;
    private GraphicsPath? _borderPath;
    private GraphicsPath? _innerBorderPath;
    private Bitmap? _layerBitmap;

    // Rebuilt from the monitor DPI once the handle exists (DeviceDpi reads 96 before that).
    private OsdLayout _layout = OsdLayoutHelper.Compute(1.0);

    protected override bool ShowWithoutActivation => true;

    public OsdForm()
    {
        this.SetStyle(ControlStyles.AllPaintingInWmPaint |
                      ControlStyles.UserPaint |
                      ControlStyles.OptimizedDoubleBuffer |
                      ControlStyles.ResizeRedraw, true);
        this.DoubleBuffered = true;

        this.FormBorderStyle = FormBorderStyle.None;
        this.BackColor = OsdThemeHelper.Surface;
        this.TopMost = true;
        this.ShowInTaskbar = false;
        this.StartPosition = FormStartPosition.Manual;

        // Nitrous type: JetBrains Mono stack for the profile name, Inter stack for the eyebrow.
        _titleFont = new Font(ResolveFontFamily(["JetBrains Mono", "Cascadia Mono", "Cascadia Code", "Consolas"], FontFamily.GenericMonospace.Name), 20, FontStyle.Bold);
        _subtitleFont = new Font(ResolveFontFamily(["Inter", "Segoe UI Variable Text", "Segoe UI"], FontFamily.GenericSansSerif.Name), 9, FontStyle.Bold);
        _titleBrush = new SolidBrush(OsdThemeHelper.TitleText);
        _subtitleBrush = new SolidBrush(OsdThemeHelper.SubtitleText);
        _shadowBrush = new SolidBrush(Color.Black);
        _outerBorderPen = new Pen(OsdThemeHelper.OuterBorder, 1);
        _innerBorderPen = new Pen(OsdThemeHelper.InnerBorder, 1);
        _textFormat = new StringFormat(StringFormat.GenericTypographic)
        {
            Alignment = StringAlignment.Near,
            LineAlignment = StringAlignment.Near,
            Trimming = StringTrimming.EllipsisCharacter,
            FormatFlags = StringFormatFlags.NoWrap
        };
        _accentBrush = new SolidBrush(_accentColor);

        _fadeTimer = new System.Windows.Forms.Timer { Interval = 30 };
        _fadeTimer.Tick += FadeTimer_Tick;

        _holdTimer = new System.Windows.Forms.Timer { Interval = 1500 };
        _holdTimer.Tick += (_, _) =>
        {
            _holdTimer.Stop();
            _fadeTimer.Start();
        };
    }

    public PowerProfile ActiveProfile => _activeProfile;
    public string ProfileText => _profileText;

    public void ShowProfile(string profileName, Color color, PowerProfile profile)
    {
        if (this.IsDisposed) return;

        if (this.InvokeRequired)
        {
            try
            {
                this.BeginInvoke(new Action(() => ShowProfile(profileName, color, profile)));
            }
            catch (Exception ex)
            {
                if (LogManager.IsDebugEnabled)
                {
                    LogManager.Debug($"OSD marshal failed: {ex.Message}", "OSD");
                }
            }

            return;
        }

        // Accent always comes from the theme so tray and OSD agree; the color
        // parameter stays for call-site compatibility.
        _profileText = StripModeSuffix(profileName).ToUpperInvariant();
        if (string.IsNullOrWhiteSpace(_profileText))
        {
            _profileText = profile.ToString().ToUpperInvariant();
        }

        Color accent = OsdThemeHelper.GetAccent(profile);
        if (_accentColor != accent)
        {
            _accentColor = accent;
            _accentBrush.Dispose();
            _accentBrush = new SolidBrush(accent);
        }

        _activeProfile = profile;
        _fadeAlpha = 255;

        if (this.IsHandleCreated)
        {
            ApplyLayout();
        }

        this.Show();
        PushLayered();

        _fadeTimer.Stop();
        _holdTimer.Stop();
        _holdTimer.Start();
    }

    private static string StripModeSuffix(string name)
    {
        const string suffix = " MODE";
        if (name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
        {
            return name[..^suffix.Length];
        }

        return name;
    }

    private void FadeTimer_Tick(object? sender, EventArgs e)
    {
        _fadeAlpha = (byte)Math.Max(0, _fadeAlpha - 17);
        if (_fadeAlpha == 0)
        {
            _fadeTimer.Stop();
            this.Hide();
            return;
        }

        PushLayered();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _holdTimer.Stop();
            _fadeTimer.Stop();
            _holdTimer.Dispose();
            _fadeTimer.Dispose();

            _titleFont.Dispose();
            _subtitleFont.Dispose();
            _titleBrush.Dispose();
            _subtitleBrush.Dispose();
            _shadowBrush.Dispose();
            _outerBorderPen.Dispose();
            _innerBorderPen.Dispose();
            _textFormat.Dispose();
            _borderPath?.Dispose();
            _innerBorderPath?.Dispose();
            _layerBitmap?.Dispose();
            _accentBrush.Dispose();
        }

        base.Dispose(disposing);
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        ApplyLayout();
    }

    /// <summary>
    /// Sizes and positions the overlay for the current monitor DPI. Runs on handle creation because
    /// DeviceDpi reads 96 until then; the OSD is a fixed-size popup, so no live DPI re-layout.
    /// Width follows the measured two-line text, clamped to the 250-600px range.
    /// </summary>
    private void ApplyLayout()
    {
        double scale = this.DeviceDpi / 96.0;

        int contentWidth = 0;
        using (var g = this.CreateGraphics())
        {
            contentWidth = MeasureContentWidth(g);
        }

        _layout = OsdLayoutHelper.Compute(scale, contentWidth);
        this.Size = _layout.FormSize;

        var screen = Screen.PrimaryScreen?.WorkingArea ?? new Rectangle(0, 0, 1920, 1080);
        this.Location = OsdLayoutHelper.ComputePosition(_layout.FormSize, screen, _layout.BottomMargin);

        _borderPath?.Dispose();
        _borderPath = CreateForgedPath(
            new RectangleF(0.5f, 0.5f, this.Width - 1, this.Height - 1),
            _layout.Chamfer);

        _innerBorderPath?.Dispose();
        _innerBorderPath = CreateForgedPath(
            new RectangleF(2.5f, 2.5f, this.Width - 5, this.Height - 5),
            Math.Max(0, _layout.Chamfer - 2));

        _layerBitmap?.Dispose();
        _layerBitmap = null;
    }

    /// <summary>
    /// Renders one OSD frame off-screen at the given DPI scale for rendering tests.
    /// Never shows the form, starts timers, or touches the layered window.
    /// Caller owns the returned bitmap.
    /// </summary>
    internal Bitmap RenderFrameForTesting(double scale, PowerProfile profile)
    {
        Color accent = OsdThemeHelper.GetAccent(profile);
        if (_accentColor != accent)
        {
            _accentColor = accent;
            _accentBrush.Dispose();
            _accentBrush = new SolidBrush(accent);
        }

        _activeProfile = profile;
        _profileText = profile.ToString().ToUpperInvariant();

        float dpi = (float)(96.0 * Math.Clamp(scale, OsdLayoutHelper.MinScale, OsdLayoutHelper.MaxScale));
        int contentWidth;
        using (var measureBitmap = new Bitmap(1, 1, PixelFormat.Format32bppPArgb))
        {
            measureBitmap.SetResolution(dpi, dpi);
            using var measureGraphics = Graphics.FromImage(measureBitmap);
            contentWidth = MeasureContentWidth(measureGraphics);
        }

        _layout = OsdLayoutHelper.Compute(scale, contentWidth);
        this.Size = _layout.FormSize;

        _borderPath?.Dispose();
        _borderPath = CreateForgedPath(
            new RectangleF(0.5f, 0.5f, this.Width - 1, this.Height - 1),
            _layout.Chamfer);

        _innerBorderPath?.Dispose();
        _innerBorderPath = CreateForgedPath(
            new RectangleF(2.5f, 2.5f, this.Width - 5, this.Height - 5),
            Math.Max(0, _layout.Chamfer - 2));

        var bitmap = new Bitmap(this.Width, this.Height, PixelFormat.Format32bppPArgb);
        bitmap.SetResolution(dpi, dpi);
        using (var g = Graphics.FromImage(bitmap))
        {
            DrawScene(g, bitmap.Width, bitmap.Height);
        }

        return bitmap;
    }

    private int MeasureContentWidth(Graphics g)
    {
        float subtitle = g.MeasureString("NITROUS", _subtitleFont, PointF.Empty, _textFormat).Width;
        float title = g.MeasureString(_profileText, _titleFont, PointF.Empty, _textFormat).Width;
        return (int)Math.Ceiling(Math.Max(subtitle, title));
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        // Layered windows ignore screen-DC painting; push the pre-rendered bitmap instead.
        PushLayered();
    }

    private void EnsureLayerBitmap()
    {
        if (_layerBitmap == null || _layerBitmap.Width != this.Width || _layerBitmap.Height != this.Height)
        {
            _layerBitmap?.Dispose();
            // PArgb: UpdateLayeredWindow expects premultiplied alpha; plain Argb lets
            // dark RGB leak through low-alpha pixels and the faded side looks dark.
            _layerBitmap = new Bitmap(this.Width, this.Height, PixelFormat.Format32bppPArgb);
        }
    }

    private void PushLayered()
    {
        if (this.IsDisposed || !this.IsHandleCreated) return;
        if (this.Width <= 0 || this.Height <= 0) return;

        EnsureLayerBitmap();
        RenderScene();

        var topLeft = new User32.POINT { X = this.Left, Y = this.Top };
        var size = new User32.SIZE { cx = this.Width, cy = this.Height };
        var src = new User32.POINT { X = 0, Y = 0 };
        var blend = new User32.BLENDFUNCTION
        {
            BlendOp = User32.AC_SRC_OVER,
            BlendFlags = 0,
            SourceConstantAlpha = _fadeAlpha,
            AlphaFormat = User32.AC_SRC_ALPHA
        };

        IntPtr screenDc = User32.GetDC(IntPtr.Zero);
        if (screenDc == IntPtr.Zero) return;

        try
        {
            IntPtr memDc = User32.CreateCompatibleDC(screenDc);
            if (memDc == IntPtr.Zero) return;

            try
            {
                IntPtr hBitmap = _layerBitmap!.GetHbitmap(Color.FromArgb(0));
                try
                {
                    IntPtr oldBitmap = User32.SelectObject(memDc, hBitmap);
                    try
                    {
                        if (!User32.UpdateLayeredWindow(this.Handle, screenDc, ref topLeft, ref size, memDc, ref src, 0, ref blend, User32.ULW_ALPHA))
                        {
                            if (LogManager.IsDebugEnabled)
                            {
                                LogManager.Debug($"OSD layer push failed: {System.Runtime.InteropServices.Marshal.GetLastWin32Error()}", "OSD");
                            }
                        }
                    }
                    finally
                    {
                        User32.SelectObject(memDc, oldBitmap);
                    }
                }
                finally
                {
                    User32.DeleteObject(hBitmap);
                }
            }
            finally
            {
                User32.DeleteDC(memDc);
            }
        }
        finally
        {
            User32.ReleaseDC(IntPtr.Zero, screenDc);
        }
    }

    private void RenderScene()
    {
        using var g = Graphics.FromImage(_layerBitmap!);
        DrawScene(g, this.Width, this.Height);
    }

    private void DrawScene(Graphics g, int width, int height)
    {
        g.Clear(Color.Transparent);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        // ClearType needs an opaque backdrop; anti-aliasing blends correctly over alpha.
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAlias;

        var client = new RectangleF(0, 0, width, height);
        using var bodyPath = CreateForgedPath(client, _layout.Chamfer);

        // 1. Forged-metal base: dark fill densest on the left, falling off to the
        // right. All lightness (sheen, glow) sits on the right so the left keeps
        // its density and the icon side never looks washed out.
        using (var backdrop = new LinearGradientBrush(
            new PointF(0, 0), new PointF(width, 0),
            Color.FromArgb(235, 20, 20, 22),
            Color.FromArgb(30, 20, 20, 22)))
        {
            g.FillPath(backdrop, bodyPath);
        }

        using (var sheen = new LinearGradientBrush(
            new PointF(0, 0), new PointF(width, 0),
            Color.FromArgb(0, 180, 180, 190),
            Color.FromArgb(24, 180, 180, 190)))
        {
            g.FillPath(sheen, bodyPath);
        }

        using (var glowPath = CreateForgedPath(client, _layout.Chamfer))
        using (var glow = new PathGradientBrush(glowPath))
        {
            glow.CenterPoint = new PointF(width * 0.8f, height * 0.85f);
            glow.CenterColor = Color.FromArgb(18, 150, 150, 160);
            glow.SurroundColors = [Color.FromArgb(0, 150, 150, 160)];
            g.FillPath(glow, bodyPath);
        }

        // 2. Double border tracing the clipped silhouette (fully opaque)
        if (_borderPath != null)
        {
            g.DrawPath(_outerBorderPen, _borderPath);
        }

        if (_innerBorderPath != null)
        {
            g.DrawPath(_innerBorderPen, _innerBorderPath);
        }

        // 3. Corner accents fading in toward the top-right and bottom-left cuts
        DrawCornerAccents(g, width, height);

        // 4. Divider, per-profile icon, two-line text (all fully opaque)
        DrawDivider(g);
        DrawProfileIcon(g, _layout.IconBounds, _accentColor, _activeProfile);
        DrawText(g);
    }

    private void DrawCornerAccents(Graphics g, int width, int height)
    {
        float c = _layout.Chamfer;
        float run = _layout.CornerAccentRun;
        float drop = _layout.CornerAccentDrop;
        float inset = 2.5f;
        float w = 4f;

        // Top-right: top edge into the slant into the right edge
        using (var trBrush = new LinearGradientBrush(
            new PointF(width - c - run, 0), new PointF(width, 0),
            Color.FromArgb(51, _accentColor), _accentColor))
        using (var trPen = new Pen(trBrush, w)
        {
            LineJoin = LineJoin.Round,
            StartCap = LineCap.Round,
            EndCap = LineCap.Round
        })
        {
            g.DrawLines(trPen,
            [
                new PointF(width - c - run, inset),
                new PointF(width - c, inset),
                new PointF(width - inset, c),
                new PointF(width - inset, c + drop)
            ]);
        }

        // Bottom-left: mirror of the above
        using (var blBrush = new LinearGradientBrush(
            new PointF(0, height), new PointF(c + run, height),
            Color.FromArgb(51, _accentColor), _accentColor))
        using (var blPen = new Pen(blBrush, w)
        {
            LineJoin = LineJoin.Round,
            StartCap = LineCap.Round,
            EndCap = LineCap.Round
        })
        {
            g.DrawLines(blPen,
            [
                new PointF(inset, height - c - drop),
                new PointF(inset, height - c),
                new PointF(c, height - inset),
                new PointF(c + run, height - inset)
            ]);
        }
    }

    private void DrawDivider(Graphics g)
    {
        var r = _layout.DividerRect;
        if (r.Width <= 0 || r.Height <= 0) return;

        using var brush = new LinearGradientBrush(
            new PointF(0, r.Top), new PointF(0, r.Bottom),
            Color.FromArgb(0, 255, 255, 255),
            Color.FromArgb(0, 255, 255, 255));
        var blend = new ColorBlend
        {
            Positions = [0f, 0.2f, 0.8f, 1f],
            Colors =
            [
                Color.FromArgb(0, 255, 255, 255),
                Color.FromArgb(230, 255, 255, 255),
                Color.FromArgb(230, 255, 255, 255),
                Color.FromArgb(0, 255, 255, 255)
            ]
        };
        brush.InterpolationColors = blend;
        g.FillRectangle(brush, r);
    }

    private void DrawText(Graphics g)
    {
        var r = _layout.TextRect;
        if (r.Width <= 0) return;

        float subHeight = _subtitleFont.GetHeight(g);
        float titleHeight = _titleFont.GetHeight(g);
        float total = subHeight + _layout.ContentGap + titleHeight;
        float y = (this.Height - total) / 2f;

        var subRect = new RectangleF(r.X, y, r.Width, subHeight);
        var titleRect = new RectangleF(r.X, y + subHeight + _layout.ContentGap, r.Width, titleHeight);
        var subShadow = subRect; subShadow.Offset(1, 1);
        var titleShadow = titleRect; titleShadow.Offset(1, 1);

        g.DrawString("NITROUS", _subtitleFont, _shadowBrush, subShadow, _textFormat);
        g.DrawString("NITROUS", _subtitleFont, _subtitleBrush, subRect, _textFormat);
        g.DrawString(_profileText, _titleFont, _shadowBrush, titleShadow, _textFormat);
        g.DrawString(_profileText, _titleFont, _titleBrush, titleRect, _textFormat);
    }

    // Builds the 8-point forged path (all four corners cut) from the reference clip-path.
    private static GraphicsPath CreateForgedPath(RectangleF rect, float chamfer)
    {
        var path = new GraphicsPath();
        if (rect.Width <= 0 || rect.Height <= 0)
        {
            return path;
        }

        float c = Math.Clamp(chamfer, 0f, Math.Min(rect.Width, rect.Height) / 2f);
        path.AddPolygon(
        [
            new PointF(rect.Left + c, rect.Top),
            new PointF(rect.Right - c, rect.Top),
            new PointF(rect.Right, rect.Top + c),
            new PointF(rect.Right, rect.Bottom - c),
            new PointF(rect.Right - c, rect.Bottom),
            new PointF(rect.Left + c, rect.Bottom),
            new PointF(rect.Left, rect.Bottom - c),
            new PointF(rect.Left, rect.Top + c)
        ]);

        return path;
    }

    private static string ResolveFontFamily(string[] candidates, string fallback)
    {
        var installed = FontFamily.Families;

        foreach (var name in candidates)
        {
            foreach (var family in installed)
            {
                if (string.Equals(family.Name, name, StringComparison.OrdinalIgnoreCase))
                {
                    return name;
                }
            }
        }

        return fallback;
    }

    private static void DrawProfileIcon(Graphics g, Rectangle bounds, Color color, PowerProfile profile)
    {
        switch (profile)
        {
            case PowerProfile.Eco:
                DrawLeafIcon(g, bounds, color);
                break;
            case PowerProfile.Quiet:
                DrawMoonIcon(g, bounds, color);
                break;
            case PowerProfile.Balanced:
                DrawBalancedIcon(g, bounds, color);
                break;
            case PowerProfile.Performance:
                DrawBoltIcon(g, bounds, color);
                break;
            case PowerProfile.Turbo:
                DrawFlameIcon(g, bounds, color);
                break;
            default:
                DrawBalancedIcon(g, bounds, color);
                break;
        }
    }

    private static void DrawLeafIcon(Graphics g, Rectangle bounds, Color color)
    {
        float x = bounds.X;
        float y = bounds.Y;
        float w = bounds.Width;
        float h = bounds.Height;

        var basePoint = new PointF(x + 0.30f * w, y + 0.85f * h);
        var tip = new PointF(x + 0.75f * w, y + 0.15f * h);

        using var path = new GraphicsPath();
        path.StartFigure();
        path.AddBezier(
            basePoint,
            new PointF(x + 0.20f * w, y + 0.35f * h),
            new PointF(x + 0.50f * w, y + 0.10f * h),
            tip);
        path.AddBezier(
            tip,
            new PointF(x + 0.85f * w, y + 0.60f * h),
            new PointF(x + 0.55f * w, y + 0.88f * h),
            basePoint);
        path.CloseFigure();

        using var brush = new SolidBrush(color);
        g.FillPath(brush, path);

        using var pen = new Pen(OsdThemeHelper.Surface, 2f) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        g.DrawLine(pen, basePoint.X, basePoint.Y, tip.X - 0.15f * w, tip.Y + 0.15f * h);
    }

    private static void DrawMoonIcon(Graphics g, Rectangle bounds, Color color)
    {
        float s = Math.Min(bounds.Width, bounds.Height);
        float x = bounds.X + (bounds.Width - s) / 2f;
        float y = bounds.Y + (bounds.Height - s) / 2f;

        using var outer = new GraphicsPath();
        outer.AddEllipse(x, y, s, s);
        using var region = new Region(outer);
        using var inner = new GraphicsPath();
        inner.AddEllipse(x + s * 0.30f, y - s * 0.10f, s * 0.78f, s * 0.78f);
        region.Exclude(inner);

        using var brush = new SolidBrush(color);
        g.FillRegion(brush, region);
    }

    private static void DrawBalancedIcon(Graphics g, Rectangle bounds, Color color)
    {
        float cx = bounds.X + bounds.Width / 2f;
        float cy = bounds.Y + bounds.Height / 2f;
        float half = Math.Min(bounds.Width, bounds.Height) / 2f - 3f;

        using var pen = new Pen(color, 3f) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };
        g.DrawPolygon(pen,
        [
            new PointF(cx, cy - half),
            new PointF(cx + half, cy),
            new PointF(cx, cy + half),
            new PointF(cx - half, cy)
        ]);
        g.DrawLine(pen, cx, cy - half, cx, cy + half);
        g.DrawLine(pen, cx - half, cy, cx + half, cy);

        using var brush = new SolidBrush(color);
        float d = Math.Max(5f, half * 0.45f);
        g.FillEllipse(brush, cx - d / 2f, cy - d / 2f, d, d);
    }

    private static void DrawBoltIcon(Graphics g, Rectangle bounds, Color color)
    {
        // Unit coords mirror the reference bolt (24x24 viewBox)
        ReadOnlySpan<(float U, float V)> units = [(13, 2), (3, 14), (12, 14), (11, 22), (21, 10), (12, 10)];
        var pts = new PointF[units.Length];
        for (int i = 0; i < units.Length; i++)
        {
            pts[i] = new PointF(
                bounds.X + (units[i].U / 24f) * bounds.Width,
                bounds.Y + (units[i].V / 24f) * bounds.Height);
        }

        using var brush = new SolidBrush(color);
        g.FillPolygon(brush, pts);
    }

    private static void DrawFlameIcon(Graphics g, Rectangle bounds, Color color)
    {
        float x = bounds.X;
        float y = bounds.Y;
        float w = bounds.Width;
        float h = bounds.Height;

        var basePoint = new PointF(x + 0.50f * w, y + 0.94f * h);
        var tip = new PointF(x + 0.60f * w, y + 0.04f * h);

        using var path = new GraphicsPath();
        path.StartFigure();
        path.AddBezier(basePoint,
            new PointF(x + 0.10f * w, y + 0.70f * h),
            new PointF(x + 0.25f * w, y + 0.55f * h),
            new PointF(x + 0.34f * w, y + 0.38f));
        path.AddBezier(path.GetLastPoint(),
            new PointF(x + 0.40f * w, y + 0.25f * h),
            new PointF(x + 0.52f * w, y + 0.16f * h),
            tip);
        path.AddBezier(path.GetLastPoint(),
            new PointF(x + 0.72f * w, y + 0.30f * h),
            new PointF(x + 0.86f * w, y + 0.52f * h),
            new PointF(x + 0.80f * w, y + 0.74f * h));
        path.AddBezier(path.GetLastPoint(),
            new PointF(x + 0.76f * w, y + 0.88f * h),
            new PointF(x + 0.64f * w, y + 0.94f * h),
            basePoint);
        path.CloseFigure();

        using var brush = new SolidBrush(color);
        g.FillPath(brush, path);
    }

    protected override CreateParams CreateParams
    {
        get
        {
            CreateParams cp = base.CreateParams;
            cp.ExStyle |= 0x08000000 | User32.WS_EX_LAYERED; // WS_EX_NOACTIVATE | WS_EX_LAYERED
            return cp;
        }
    }
}
