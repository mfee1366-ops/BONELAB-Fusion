using System.Diagnostics;

namespace FusionLauncher;

internal sealed class ModIoConnectForm : Form
{
    private readonly Label _code = new() { Text = "Requesting code…", AutoSize = true, Font = new Font("Segoe UI Semibold", 30f), ForeColor = Color.White };
    private readonly Label _status = new() { Text = "Connecting to mod.io…", AutoSize = true, ForeColor = Color.Gainsboro };
    private readonly CancellationTokenSource _cancellation = new();
    private string _url = "https://mod.io/connect";

    public ModIoConnectForm(ModIoService service)
    {
        Text = "Connect to mod.io"; Size = new Size(520, 300); StartPosition = FormStartPosition.CenterParent;
        BackColor = Color.FromArgb(18, 20, 25); ForeColor = Color.White;
        var open = new Button { Text = "Open mod.io/connect", AutoSize = true, BackColor = Color.FromArgb(79, 128, 255), ForeColor = Color.White, FlatStyle = FlatStyle.Flat };
        open.Click += (_, _) => Process.Start(new ProcessStartInfo(_url) { UseShellExecute = true });
        var layout = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, Padding = new Padding(25), WrapContents = false };
        layout.Controls.Add(new Label { Text = "Enter this code on mod.io", AutoSize = true, ForeColor = Color.Gainsboro }); layout.Controls.Add(_code); layout.Controls.Add(_status); layout.Controls.Add(open); Controls.Add(layout);
        Shown += async (_, _) => await ConnectAsync(service);
        FormClosed += (_, _) => _cancellation.Cancel();
    }

    private async Task ConnectAsync(ModIoService service)
    {
        try
        {
            string result = await service.ConnectDeviceAsync((code, url) => BeginInvoke(() => ShowCode(code, url)), _cancellation.Token);
            if (!IsDisposed) { _status.Text = result; DialogResult = DialogResult.OK; Close(); }
        }
        catch (OperationCanceledException) { }
        catch (Exception exception) { if (!IsDisposed) _status.Text = $"Connection failed: {exception.Message}"; }
    }

    private void ShowCode(string code, string url)
    {
        _code.Text = code; _url = url; _status.Text = "A browser has opened. Log in, enter this code, then return here.";
        Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
    }
}
