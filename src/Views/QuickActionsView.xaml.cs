using AcApp = Autodesk.AutoCAD.ApplicationServices.Application;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

namespace XcTools.Views
{
    /// <summary>
    /// QuickActionsView.xaml 的交互逻辑
    /// </summary>
    public partial class QuickActionsView : Window
    {
        public QuickActionsView()
        {
            InitializeComponent();

            // 从插件包根目录加载窗口图标
            Icon = App.LoadWindowIcon();

            // 恢复窗口状态
            XcTools.Services.WindowStateService.RestoreWindowState("QuickActionsView", this);
            
            // 注册窗口关闭事件，保存窗口状态
            this.Closing += QuickActionsView_Closing;
        }

        /// <summary>窗口句柄创建后再设一次图标，防止 AutoCAD 宿主覆盖</summary>
        protected override void OnSourceInitialized(System.EventArgs e)
        {
            base.OnSourceInitialized(e);
            Icon = App.LoadWindowIcon();
        }

        /// <summary>
        /// 窗口关闭事件，保存窗口状态
        /// </summary>
        private void QuickActionsView_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            XcTools.Services.WindowStateService.SaveWindowState("QuickActionsView", this);
        }

        // 绘制命令按钮
        private void LineButton_Click(object sender, RoutedEventArgs e)
        {
            CloseAndExecuteCommand("LINE");
        }

        private void RectangleButton_Click(object sender, RoutedEventArgs e)
        {
            CloseAndExecuteCommand("RECTANG");
        }

        private void CircleButton_Click(object sender, RoutedEventArgs e)
        {
            CloseAndExecuteCommand("CIRCLE");
        }

        // 修改命令按钮
        private void EraseButton_Click(object sender, RoutedEventArgs e)
        {
            CloseAndExecuteCommand("ERASE");
        }

        private void CopyButton_Click(object sender, RoutedEventArgs e)
        {
            CloseAndExecuteCommand("COPY");
        }

        private void MoveButton_Click(object sender, RoutedEventArgs e)
        {
            CloseAndExecuteCommand("MOVE");
        }

        // 视图命令按钮
        private void PanButton_Click(object sender, RoutedEventArgs e)
        {
            CloseAndExecuteCommand("PAN");
        }

        private void ZoomWindowButton_Click(object sender, RoutedEventArgs e)
        {
            CloseAndExecuteCommand("ZOOM W");
        }

        private void ZoomExtentsButton_Click(object sender, RoutedEventArgs e)
        {
            CloseAndExecuteCommand("ZOOM E");
        }

        // 其他命令按钮
        private void TrimButton_Click(object sender, RoutedEventArgs e)
        {
            CloseAndExecuteCommand("TRIM");
        }

        private void JoinButton_Click(object sender, RoutedEventArgs e)
        {
            CloseAndExecuteCommand("JOIN");
        }

        private void ExplodeButton_Click(object sender, RoutedEventArgs e)
        {
            CloseAndExecuteCommand("EXPLODE");
        }

        // 关闭窗口并执行命令
        private void CloseAndExecuteCommand(string command)
        {
            this.Close();
            AcApp.DocumentManager.MdiActiveDocument.SendStringToExecute($"{command} ", true, false, false);
        }
    }
}