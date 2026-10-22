namespace PtcXaeMcp.WinForms;

public partial class Form1
{
    private System.ComponentModel.IContainer components = null!;

    // Header (issue #270): brand, status chips, Stop / Menu / theme toggle. Docking and
    // flow layouts only — no absolute coordinates.
    private TableLayoutPanel _pnlHeader = null!;
    private FlowLayoutPanel _flpBrand = null!;
    private PictureBox _picLogo = null!;
    private Label _lblProductName = null!;
    private FlowLayoutPanel _flpHeaderActions = null!;
    private Label _lblRunState = null!;
    private Button _btnStop = null!;
    private Button _btnMenu = null!;
    private Button _btnThemeToggle = null!;
    private FlowLayoutPanel _flpStatusChips = null!;
    private FlowLayoutPanel _flpXaeChip = null!;
    private Label _lblXaeDot = null!;
    private Label _lblXaeState = null!;
    private FlowLayoutPanel _flpProjectChip = null!;
    private Label _lblProjectDot = null!;
    private Label _lblProjectState = null!;
    private FlowLayoutPanel _flpMcpChip = null!;
    private Label _lblMcpDot = null!;
    private Label _lblMcpCaption = null!;
    private ToolTip _toolTip = null!;

    // Header menu
    private ContextMenuStrip _menuHeader = null!;
    private ToolStripMenuItem _mnuRefreshStatus = null!;
    private ToolStripMenuItem _mnuForceCloseXae = null!;
    private ToolStripMenuItem _mnuDismissSecurityDialog = null!;
    private ToolStripMenuItem _mnuOpenLogFolder = null!;
    private ToolStripMenuItem _mnuToggleTheme = null!;

    // Views (their content is built in Form1.cs, see CreateViews)
    private Theme.ThemedTabControl _tabControl = null!;
    private TabPage _tabActivity = null!;
    private TabPage _tabApprovals = null!;
    private TabPage _tabConsole = null!;

    protected override void Dispose(bool disposing)
    {
        if (disposing && (components != null))
        {
            components.Dispose();
        }
        base.Dispose(disposing);
    }

    private void InitializeComponent()
    {
        components = new System.ComponentModel.Container();
        _pnlHeader = new TableLayoutPanel();
        _flpBrand = new FlowLayoutPanel();
        _picLogo = new PictureBox();
        _lblProductName = new Label();
        _flpHeaderActions = new FlowLayoutPanel();
        _lblRunState = new Label();
        _btnStop = new Button();
        _btnMenu = new Button();
        _btnThemeToggle = new Button();
        _flpStatusChips = new FlowLayoutPanel();
        _flpXaeChip = new FlowLayoutPanel();
        _lblXaeDot = new Label();
        _lblXaeState = new Label();
        _flpProjectChip = new FlowLayoutPanel();
        _lblProjectDot = new Label();
        _lblProjectState = new Label();
        _flpMcpChip = new FlowLayoutPanel();
        _lblMcpDot = new Label();
        _lblMcpCaption = new Label();
        _toolTip = new ToolTip(components);
        _menuHeader = new ContextMenuStrip(components);
        _mnuRefreshStatus = new ToolStripMenuItem();
        _mnuForceCloseXae = new ToolStripMenuItem();
        _mnuDismissSecurityDialog = new ToolStripMenuItem();
        _mnuOpenLogFolder = new ToolStripMenuItem();
        _mnuToggleTheme = new ToolStripMenuItem();
        _tabControl = new Theme.ThemedTabControl();
        _tabActivity = new TabPage();
        _tabApprovals = new TabPage();
        _tabConsole = new TabPage();
        _pnlHeader.SuspendLayout();
        _flpBrand.SuspendLayout();
        ((System.ComponentModel.ISupportInitialize)_picLogo).BeginInit();
        _flpHeaderActions.SuspendLayout();
        _flpStatusChips.SuspendLayout();
        _flpXaeChip.SuspendLayout();
        _flpProjectChip.SuspendLayout();
        _flpMcpChip.SuspendLayout();
        _menuHeader.SuspendLayout();
        _tabControl.SuspendLayout();
        SuspendLayout();

        //
        // _pnlHeader: row 0 = brand | actions, row 1 = status chips (full width)
        //
        _pnlHeader.AutoSize = true;
        _pnlHeader.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        _pnlHeader.ColumnCount = 2;
        _pnlHeader.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        _pnlHeader.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        _pnlHeader.RowCount = 2;
        _pnlHeader.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        _pnlHeader.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        _pnlHeader.Controls.Add(_flpBrand, 0, 0);
        _pnlHeader.Controls.Add(_flpHeaderActions, 1, 0);
        _pnlHeader.Controls.Add(_flpStatusChips, 0, 1);
        _pnlHeader.SetColumnSpan(_flpStatusChips, 2);
        _pnlHeader.Dock = DockStyle.Top;
        _pnlHeader.Name = "_pnlHeader";
        _pnlHeader.Padding = new Padding(12, 8, 12, 8);
        _pnlHeader.TabIndex = 0;

        //
        // _flpBrand: logo + product name and version
        //
        _flpBrand.AutoSize = true;
        _flpBrand.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        _flpBrand.WrapContents = false;
        _flpBrand.Anchor = AnchorStyles.Left;
        _flpBrand.Margin = new Padding(0);
        _flpBrand.Name = "_flpBrand";
        _flpBrand.Controls.Add(_picLogo);
        _flpBrand.Controls.Add(_lblProductName);

        //
        // _picLogo (image loaded from the embedded Assets/logo.png, see ProductInfo.LoadLogo)
        //
        _picLogo.Anchor = AnchorStyles.Left;
        _picLogo.Margin = new Padding(0, 0, 12, 0);
        _picLogo.Name = "_picLogo";
        _picLogo.Size = new Size(232, 48); // logo is ~4.8:1; 48 px tall reads as the brand mark, not a footnote
        _picLogo.SizeMode = PictureBoxSizeMode.Zoom;
        _picLogo.TabStop = false;

        //
        // _lblProductName
        //
        _lblProductName.Anchor = AnchorStyles.Left;
        _lblProductName.AutoSize = true;
        _lblProductName.Font = new Font("Segoe UI Semibold", 12F, FontStyle.Regular, GraphicsUnit.Point, 0);
        _lblProductName.Margin = new Padding(0);
        _lblProductName.Name = "_lblProductName";
        _lblProductName.Text = "Pneutronic XAE MCP";

        //
        // _flpHeaderActions: apply-run state, Stop, Menu, theme toggle (right-aligned)
        //
        _flpHeaderActions.Anchor = AnchorStyles.Right;
        _flpHeaderActions.AutoSize = true;
        _flpHeaderActions.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        _flpHeaderActions.WrapContents = false;
        _flpHeaderActions.Margin = new Padding(12, 0, 0, 0);
        _flpHeaderActions.Name = "_flpHeaderActions";
        _flpHeaderActions.Controls.Add(_lblRunState);
        _flpHeaderActions.Controls.Add(_btnStop);
        _flpHeaderActions.Controls.Add(_btnMenu);
        _flpHeaderActions.Controls.Add(_btnThemeToggle);

        _lblRunState.Anchor = AnchorStyles.Left;
        _lblRunState.AutoSize = true;
        _lblRunState.Margin = new Padding(0, 0, 8, 0);
        _lblRunState.Name = "_lblRunState";
        _lblRunState.Text = string.Empty;

        _btnStop.AutoSize = true;
        _btnStop.Name = "_btnStop";
        _btnStop.TabIndex = 1;
        _btnStop.Text = "Stop";

        _btnMenu.AutoSize = true;
        _btnMenu.Name = "_btnMenu";
        _btnMenu.TabIndex = 2;
        _btnMenu.Text = "Menu ▾";

        _btnThemeToggle.AutoSize = true;
        _btnThemeToggle.MinimumSize = new Size(70, 29); // same width for "Dark" and "Light": no jump on toggle
        _btnThemeToggle.FlatStyle = FlatStyle.Flat;
        _btnThemeToggle.FlatAppearance.BorderSize = 1;
        _btnThemeToggle.Name = "_btnThemeToggle";
        _btnThemeToggle.TabIndex = 3;
        _btnThemeToggle.Text = "Dark";

        //
        // _flpStatusChips: XAE, project, MCP server (dot + text each; wraps when narrow)
        //
        _flpStatusChips.AutoSize = true;
        _flpStatusChips.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        _flpStatusChips.Dock = DockStyle.Fill;
        _flpStatusChips.Margin = new Padding(0, 6, 0, 0);
        _flpStatusChips.Name = "_flpStatusChips";
        _flpStatusChips.WrapContents = true;
        _flpStatusChips.Controls.Add(_flpXaeChip);
        _flpStatusChips.Controls.Add(_flpProjectChip);
        _flpStatusChips.Controls.Add(_flpMcpChip);

        ConfigureStatusChip(_flpXaeChip, "_flpXaeChip", _lblXaeDot, "_lblXaeDot", _lblXaeState, "_lblXaeState", "XAE: not checked");
        ConfigureStatusChip(_flpProjectChip, "_flpProjectChip", _lblProjectDot, "_lblProjectDot", _lblProjectState, "_lblProjectState", "Project: not checked");
        ConfigureStatusChip(_flpMcpChip, "_flpMcpChip", _lblMcpDot, "_lblMcpDot", _lblMcpCaption, "_lblMcpCaption", "MCP: inactive");
        // A long project line (project | PLC) wraps inside its chip instead of widening the window.
        _lblProjectState.MaximumSize = new Size(420, 0);

        //
        // _menuHeader (opened by _btnMenu)
        //
        _menuHeader.Items.AddRange(new ToolStripItem[]
        {
            _mnuRefreshStatus,
            new ToolStripSeparator(),
            _mnuForceCloseXae,
            _mnuDismissSecurityDialog,
            new ToolStripSeparator(),
            _mnuOpenLogFolder,
            _mnuToggleTheme,
        });
        _menuHeader.Name = "_menuHeader";
        _menuHeader.ShowImageMargin = false;

        _mnuRefreshStatus.Name = "_mnuRefreshStatus";
        _mnuRefreshStatus.Text = "Refresh status";
        _mnuForceCloseXae.Name = "_mnuForceCloseXae";
        _mnuForceCloseXae.Text = "Close XAE instance (recovery)…";
        _mnuDismissSecurityDialog.Name = "_mnuDismissSecurityDialog";
        _mnuDismissSecurityDialog.Text = "Dismiss security dialog";
        _mnuOpenLogFolder.Name = "_mnuOpenLogFolder";
        _mnuOpenLogFolder.Text = "Open log folder";
        _mnuToggleTheme.Name = "_mnuToggleTheme";
        _mnuToggleTheme.Text = "Light / dark theme";

        //
        // _tabControl: the three views
        //
        _tabControl.Controls.Add(_tabActivity);
        _tabControl.Controls.Add(_tabApprovals);
        _tabControl.Controls.Add(_tabConsole);
        _tabControl.Dock = DockStyle.Fill;
        _tabControl.Name = "_tabControl";
        _tabControl.SelectedIndex = 0;
        _tabControl.TabIndex = 4;

        _tabActivity.Name = "_tabActivity";
        _tabActivity.Padding = new Padding(8);
        _tabActivity.TabIndex = 0;
        _tabActivity.Text = "Activity";
        _tabActivity.UseVisualStyleBackColor = true;

        _tabApprovals.Name = "_tabApprovals";
        _tabApprovals.Padding = new Padding(8);
        _tabApprovals.TabIndex = 1;
        _tabApprovals.Text = "Approvals";
        _tabApprovals.UseVisualStyleBackColor = true;

        _tabConsole.Name = "_tabConsole";
        _tabConsole.Padding = new Padding(8);
        _tabConsole.TabIndex = 2;
        _tabConsole.Text = "Console";
        _tabConsole.UseVisualStyleBackColor = true;

        //
        // Form1
        //
        AutoScaleDimensions = new SizeF(8F, 20F);
        AutoScaleMode = AutoScaleMode.Font;
        ClientSize = new Size(832, 880);
        Controls.Add(_tabControl);
        Controls.Add(_pnlHeader);
        Font = new Font("Segoe UI", 9F, FontStyle.Regular, GraphicsUnit.Point, 0);
        MinimumSize = new Size(850, 680);
        Name = "Form1";
        StartPosition = FormStartPosition.CenterScreen;
        Text = "Pneutronic XAE MCP";

        _flpMcpChip.ResumeLayout(false);
        _flpMcpChip.PerformLayout();
        _flpProjectChip.ResumeLayout(false);
        _flpProjectChip.PerformLayout();
        _flpXaeChip.ResumeLayout(false);
        _flpXaeChip.PerformLayout();
        _flpStatusChips.ResumeLayout(false);
        _flpStatusChips.PerformLayout();
        _flpHeaderActions.ResumeLayout(false);
        _flpHeaderActions.PerformLayout();
        ((System.ComponentModel.ISupportInitialize)_picLogo).EndInit();
        _flpBrand.ResumeLayout(false);
        _flpBrand.PerformLayout();
        _pnlHeader.ResumeLayout(false);
        _pnlHeader.PerformLayout();
        _menuHeader.ResumeLayout(false);
        _tabControl.ResumeLayout(false);
        ResumeLayout(false);
        PerformLayout();
    }

    // ConfigureStatusChip / ConfigureStatusDot (hand-written helpers, not Designer output)
    // live in Form1.Layout.cs — issue #157 item 10.
}
