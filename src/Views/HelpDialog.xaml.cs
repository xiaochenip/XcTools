using System.Windows;

namespace XcTools.Views
{
    public partial class HelpDialog : Window
    {
        public HelpDialog()
        {
            InitializeComponent();

            // 从插件包根目录加载窗口图标
            Icon = App.LoadWindowIcon();
        }

        /// <summary>窗口句柄创建后再设一次图标，防止 AutoCAD 宿主覆盖</summary>
        protected override void OnSourceInitialized(System.EventArgs e)
        {
            base.OnSourceInitialized(e);
            Icon = App.LoadWindowIcon();
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }
    }
}
