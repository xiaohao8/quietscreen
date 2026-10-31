using System;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using QuietScreen.Core;

namespace QuietScreen.Views
{
    public partial class LogView : UserControl
    {
        private DispatcherTimer _follow;
        private bool _autoScroll = true;

        public LogView()
        {
            InitializeComponent();
            _follow = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
            _follow.Tick += (s, e) => Refresh();
            Logger.LogAdded += line =>
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    if (LogBox.Text.Length > 200000) LogBox.Text = LogBox.Text.Substring(100000);
                    LogBox.AppendText(line + Environment.NewLine);
                }));
        }

        public void Refresh()
        {
            // 文件里可能有更多历史, 合并显示
            var lines = Logger.Snapshot();
            LogBox.Text = string.Join(Environment.NewLine, lines);
        }

        private void LogBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_autoScroll) LogBox.ScrollToEnd();
        }

        private void BtnRefresh_Click(object sender, RoutedEventArgs e) => Refresh();

        private void BtnOpenFile_Click(object sender, RoutedEventArgs e)
        {
            if (File.Exists(Logger.LogFile))
                Process.Start(new ProcessStartInfo(Logger.LogFile) { UseShellExecute = true });
        }

        private void BtnFollow_Click(object sender, RoutedEventArgs e)
        {
            if (_follow.IsEnabled)
            {
                _follow.Stop();
                BtnFollow.Content = "自动刷新: 关";
            }
            else
            {
                _follow.Start();
                BtnFollow.Content = "自动刷新: 开";
            }
        }
    }
}
