using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using Nitrous.Enums;
using Nitrous.Helpers;
using Nitrous.Managers;

namespace Nitrous.Ui;

public class OsdForm : Form
{
    private readonly System.Windows.Forms.Timer _fadeTimer;
    private readonly System.Windows.Forms.Timer _holdTimer;
    private string _profileText = "PROFILE";
    private Color _accentColor = Color.White;
    private PowerProfile _activeProfile = PowerProfile.Balanced;

    // Set peak opacity to 85% for a subtle translucent effect
    private double _opacity = 0.85;

    // Palette mirrors DashboardStyles.xaml tokens: Steel950 fill, BorderCard border, Steel100 text.
    private static readonly Color SurfaceColor = Color.FromArgb(8, 8, 10);
    private static readonly Color BorderColor = Color.FromArgb(56, 56, 66);
    private static readonly Color TextColor = Color.FromArgb(237, 237, 239);
    private static readonly string MonoFontFamily = ResolveMonoFontFamily();

    // GDI+ resources are cached: the fade timer repaints ~30 times per display, so per-paint
    // allocation (and an undisposed StringFormat) would churn native handles every tick.
    private readonly Font _font;
    private readonly SolidBrush _textBrush;
    private readonly Pen _borderPen;
    private readonly StringFormat _textFormat;
    private SolidBrush _accentBrush;
    private GraphicsPath? _borderPath;
    private Region? _region;

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
        this.BackColor = SurfaceColor;
        this.TopMost = true;
        this.ShowInTaskbar = false;
        this.StartPosition = FormStartPosition.Manual;

        _font = new Font(MonoFontFamily, 14, FontStyle.Bold);
        _textBrush = new SolidBrush(TextColor);
        _borderPen = new Pen(BorderColor, 1);
        _textFormat = new StringFormat
        {
            Alignment = StringAlignment.Center,
            LineAlignment = StringAlignment.Center
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

        _profileText = profileName.ToUpper();
        if (_accentColor != color)
        {
            _accentColor = color;
            _accentBrush.Dispose();
            _accentBrush = new SolidBrush(color);
        }

        _activeProfile = profile;

        // Reset to the subtle peak opacity
        _opacity = 0.85;
        this.Opacity = _opacity;

        this.Show();
        this.Refresh();

        _fadeTimer.Stop();
        _holdTimer.Stop();
        _holdTimer.Start();
    }


    private void FadeTimer_Tick(object? sender, EventArgs e)
    {
        _opacity -= 0.05;
        this.Opacity = _opacity;
        if (_opacity <= 0)
        {
            _fadeTimer.Stop();
            this.Hide();
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _holdTimer.Stop();
            _fadeTimer.Stop();
            _holdTimer.Dispose();
            _fadeTimer.Dispose();

            _font.Dispose();
            _textBrush.Dispose();
            _borderPen.Dispose();
            _textFormat.Dispose();
            _borderPath?.Dispose();
            _region?.Dispose();
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
    /// </summary>
    private void ApplyLayout()
    {
        _layout = OsdLayoutHelper.Compute(this.DeviceDpi / 96.0);
        this.Size = _layout.FormSize;

        var screen = Screen.PrimaryScreen?.WorkingArea ?? new Rectangle(0, 0, 1920, 1080);
        this.Location = new Point((screen.Width - this.Width) / 2, screen.Height - _layout.BottomMargin);

        _borderPath?.Dispose();
        _borderPath = CreateChamferPath(
            new RectangleF(0.5f, 0.5f, this.Width - 1, this.Height - 1),
            _layout.Chamfer);

        // Clip the form to the chamfered silhouette (sharp top-left/bottom-right, cut top-right/bottom-left)
        using var regionPath = CreateChamferPath(new RectangleF(0, 0, this.Width, this.Height), _layout.Chamfer);
        var region = new Region(regionPath);
        _region?.Dispose();
        _region = region;
        this.Region = region;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.Clear(SurfaceColor);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        e.Graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

        // 1. Accent bar (form region trims its bottom-left chamfer)
        e.Graphics.FillRectangle(_accentBrush, 0, 0, _layout.AccentBarWidth, this.Height);

        // 2. Draw chamfered border tracing the form's clipped silhouette
        if (_borderPath != null)
        {
            e.Graphics.DrawPath(_borderPen, _borderPath);
        }

        // 3. Draw Speedometer Icon
        DrawSpeedometerIcon(e.Graphics, _layout.IconBounds, _accentColor, _activeProfile);

        // 4. Draw Text
        e.Graphics.DrawString(_profileText, _font, _textBrush, _layout.TextRect, _textFormat);
    }

    // Builds the GDI+ path from the shared chamfer polygon so the region and border always agree.
    private static GraphicsPath CreateChamferPath(RectangleF rect, float chamfer)
    {
        var path = new GraphicsPath();
        var points = GdiChamferGeometryHelper.CreateChamferPoints(rect, chamfer);
        if (points.Length > 0)
        {
            path.AddPolygon(points);
        }

        return path;
    }

    private static string ResolveMonoFontFamily()
    {
        string[] candidates = { "JetBrains Mono", "Cascadia Mono", "Cascadia Code", "Consolas" };
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

        return FontFamily.GenericMonospace.Name;
    }

    private void DrawSpeedometerIcon(Graphics g, Rectangle bounds, Color color, PowerProfile profile)
    {
        using var thickPen = new Pen(color, 2.5f) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        using var thinPen = new Pen(color, 2f) { StartCap = LineCap.Round, EndCap = LineCap.Round };

        int cx = bounds.X + bounds.Width / 2;
        int cy = bounds.Y + bounds.Height / 2;
        int r = bounds.Width / 2;

        g.DrawArc(thickPen, cx - r, cy - r, r * 2, r * 2, 135, 270);

        g.FillEllipse(_accentBrush, cx - 3, cy - 3, 6, 6);

        var (nx, ny) = DialGeometryHelper.CalculateNeedleTip(cx, cy, r - 2, profile);
        g.DrawLine(thinPen, cx, cy, nx, ny);
    }

    protected override CreateParams CreateParams
    {
        get
        {
            CreateParams cp = base.CreateParams;
            cp.ExStyle |= 0x08000000; // WS_EX_NOACTIVATE
            return cp;
        }
    }
}
