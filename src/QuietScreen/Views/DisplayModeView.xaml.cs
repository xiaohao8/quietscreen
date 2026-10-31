using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using QuietScreen.Core;

namespace QuietScreen.Views
{
    public partial class DisplayModeView : UserControl
    {
        public DisplayModeView()
        {
            InitializeComponent();
        }

        public void Refresh() { }

        private void Mode_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button b)
            {
                var mode = b.Tag.ToString() switch
                {
                    "internal" => DisplayService.Topology.InternalOnly,
                    "external" => DisplayService.Topology.ExternalOnly,
                    "extend" => DisplayService.Topology.Extend,
                    "clone" => DisplayService.Topology.Duplicate,
                    _ => DisplayService.Topology.Extend
                };
                DisplayService.SwitchTo(mode);
            }
        }

        private void BtnOpenDisplaySettings_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "ms-settings:display",
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                Logger.Error("打开系统显示设置失败: " + ex.Message);
            }
        }
    }
}
