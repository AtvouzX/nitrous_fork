using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using Nitrous.Enums;

namespace Nitrous.Ui;

public class OsdForm : Form
{
    private readonly System.Windows.Forms.Timer _fadeTimer;
    private string _profileText = "PROFILE";
    private Color _accentColor = Color.White;
    private PowerProfile _activeProfile = PowerProfile.Balanced;

    // Set peak opacity to 85% for a subtle translucent effect
    private double _opacity = 0.85;
    private readonly int _cornerRadius = 10;

    protected override bool ShowWithoutActivation => true;

    public OsdForm()
    {
        this.FormBorderStyle = FormBorderStyle.None;
        this.BackColor = Color.FromArgb(24, 24, 27);
        this.TopMost = true;
        this.ShowInTaskbar = false;

        this.Size = new Size(280, 70);

        var screen = Screen.PrimaryScreen?.WorkingArea ?? new Rectangle(0, 0, 1920, 1080);
        this.StartPosition = FormStartPosition.Manual;
        this.Location = new Point((screen.Width - this.Width) / 2, screen.Height - 160);

        // Clip the form's corners based on the specified radius
        this.Region = new Region(GetRoundedPath(new Rectangle(0, 0, this.Width, this.Height), _cornerRadius));

        _fadeTimer = new System.Windows.Forms.Timer { Interval = 30 };
        _fadeTimer.Tick += FadeTimer_Tick;
    }

    public void ShowProfile(string profileName, Color color, PowerProfile profile)
    {
        _profileText = profileName.ToUpper();
        _accentColor = color;
        _activeProfile = profile;

        // Reset to the subtle peak opacity
        _opacity = 0.85;
        this.Opacity = _opacity;

        this.Invalidate();
        this.Show();

        _fadeTimer.Stop();
        System.Threading.Tasks.Task.Delay(1500).ContinueWith(_ =>
        {
            this.Invoke(new Action(() => _fadeTimer.Start()));
        });
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

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        e.Graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

        // 1. Accent bar (Form region automatically trims the top/bottom left corners)
        using (var brush = new SolidBrush(_accentColor))
        {
            e.Graphics.FillRectangle(brush, 0, 0, 8, this.Height);
        }

        // 2. Draw Rounded Border perfectly tracing the form's curved edge
        using (var path = GetRoundedPath(new Rectangle(0, 0, this.Width - 1, this.Height - 1), _cornerRadius))
        using (var pen = new Pen(Color.FromArgb(48, 48, 54), 1))
        {
            e.Graphics.DrawPath(pen, path);
        }

        // 3. Draw Speedometer Icon
        DrawSpeedometerIcon(e.Graphics, new Rectangle(25, 17, 36, 36), _accentColor, _activeProfile);

        // 4. Draw Text
        using (var font = new Font("Segoe UI", 16, FontStyle.Bold))
        using (var textBrush = new SolidBrush(Color.White))
        {
            StringFormat format = new StringFormat
            {
                Alignment = StringAlignment.Center,
                LineAlignment = StringAlignment.Center
            };

            Rectangle textRect = new Rectangle(65, 0, this.Width - 65, this.Height);
            e.Graphics.DrawString(_profileText, font, textBrush, textRect, format);
        }
    }

    // Helper method to generate standard geometric curves for rounded edges
    private GraphicsPath GetRoundedPath(Rectangle rect, int radius)
    {
        GraphicsPath path = new GraphicsPath();
        int d = radius * 2;

        path.AddArc(rect.X, rect.Y, d, d, 180, 90); // Top-Left
        path.AddArc(rect.Right - d, rect.Y, d, d, 270, 90); // Top-Right
        path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90); // Bottom-Right
        path.AddArc(rect.X, rect.Bottom - d, d, d, 90, 90); // Bottom-Left
        path.CloseFigure();

        return path;
    }

    private void DrawSpeedometerIcon(Graphics g, Rectangle bounds, Color color, PowerProfile profile)
    {
        using var thickPen = new Pen(color, 2.5f) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        using var thinPen = new Pen(color, 2f) { StartCap = LineCap.Round, EndCap = LineCap.Round };

        int cx = bounds.X + bounds.Width / 2;
        int cy = bounds.Y + bounds.Height / 2;
        int r = bounds.Width / 2;

        g.DrawArc(thickPen, cx - r, cy - r, r * 2, r * 2, 135, 270);

        double angle = 270;
        switch (profile)
        {
            case PowerProfile.Quiet: angle = 180; break;
            case PowerProfile.Balanced: angle = 270; break;
            case PowerProfile.Performance: angle = 360; break;
            case PowerProfile.Turbo: angle = 45; break;
        }

        g.FillEllipse(new SolidBrush(color), cx - 3, cy - 3, 6, 6);

        double rad = angle * Math.PI / 180.0;
        int nx = cx + (int)((r - 2) * Math.Cos(rad));
        int ny = cy + (int)((r - 2) * Math.Sin(rad));

        g.DrawLine(thinPen, cx, cy, nx, ny);
    }

    protected override CreateParams CreateParams
    {
        get
        {
            CreateParams cp = base.CreateParams;
            cp.ExStyle |= 0x00000020; // WS_EX_TRANSPARENT
            cp.ExStyle |= 0x08000000; // WS_EX_NOACTIVATE
            return cp;
        }
    }
}
