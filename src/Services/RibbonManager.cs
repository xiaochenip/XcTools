using AcApp = Autodesk.AutoCAD.ApplicationServices.Application;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.Windows;
using System;
using System.Linq;
using System.Reflection;

namespace XcTools.Services
{
    /// <summary>
    /// 使用 Autodesk.Windows Ribbon API（AutoCAD 原生 Ribbon）创建「小辰CAD工具箱」选项卡。
    /// 不依赖任何外部菜单文件，API 在 AutoCAD 2026 完全可用。
    /// </summary>
    public static class RibbonManager
    {
        private const string TabId = "XCTOOLS_DEFAULT_TAB";
        private const string TabTitle = "小辰CAD工具箱";
        private static bool _isRibbonLoaded;

        public static bool IsRibbonLoaded => _isRibbonLoaded;

        public static void LoadRibbon()
        {
            App.Log("RibbonManager.LoadRibbon START");
            try
            {
                RibbonControl rc = ComponentManager.Ribbon;
                if (rc == null)
                {
                    App.Log("RibbonManager: ComponentManager.Ribbon is null, deferring.");
                    return;
                }

                // 1) 已存在同 ID 选项卡：先移除避免重复创建
                RibbonTab existing = rc.Tabs.FirstOrDefault(t => t.Id == TabId);
                if (existing != null)
                {
                    rc.Tabs.Remove(existing);
                    App.Log("RibbonManager: Removed pre-existing tab with same Id.");
                }

                // 2) 构建选项卡
                var tab = new RibbonTab
                {
                    Title = TabTitle,
                    Id = TabId
                };

                // 3) 图库管理面板
                tab.Panels.Add(BuildPanel("图库",
                    new RibbonButton[]
                    {
                        MakeButton("图库管理", "_XTK", "打开图库管理窗口"),
                        MakeButton("插入图块", "_XCI", "选择图块插入"),
                    },
                    new RibbonButton[]
                    {
                        MakeButton("连续插入", "_XCM", "连续插入多张图块"),
                        MakeButton("选图入库", "_XAD", "选图形加入图库"),
                    }
                ));

                // 4) 图库维护面板
                tab.Panels.Add(BuildPanel("维护",
                    new RibbonButton[]
                    {
                        MakeButton("搜索图块", "_XCFD", "按名称搜索图库内图块"),
                        MakeButton("刷新缩略图", "_XCRF", "重新生成缩略图"),
                    },
                    new RibbonButton[]
                    {
                        MakeButton("新建分类", "_XCF", "在图库中新建分类"),
                        MakeButton("重命名", "_XCREN", "重命名图块或分类"),
                        MakeButton("删除", "_XCDEL", "删除图块或分类"),
                    }
                ));

                // 5) 系统面板
                tab.Panels.Add(BuildPanel("系统",
                    new RibbonButton[]
                    {
                        MakeButton("帮助", "_XHP", "打开帮助与命令列表"),
                        MakeButton("命令列表", "_XCH", "查看所有可用命令"),
                        MakeButton("帮助", "_XHP", "打开帮助说明"),
                    }
                ));

                // 6) 添加到 Ribbon 并激活
                rc.Tabs.Add(tab);
                try { tab.IsActive = true; } catch { }

                _isRibbonLoaded = true;
                App.Log("RibbonManager.LoadRibbon COMPLETE");
            }
            catch (Exception ex)
            {
                App.Log($"RibbonManager.LoadRibbon ERROR: {ex.Message}");
                App.Log($"Stack trace: {ex.StackTrace}");
                Editor ed = AcApp.DocumentManager.MdiActiveDocument?.Editor;
                ed?.WriteMessage($"\nRibbon 加载失败: {ex.Message}，仍可使用命令 XTK、XCM、XAD 等。");
            }
        }

        public static void UnloadRibbon()
        {
            try
            {
                RibbonControl rc = ComponentManager.Ribbon;
                if (rc != null)
                {
                    RibbonTab existing = rc.Tabs.FirstOrDefault(t => t.Id == TabId);
                    if (existing != null)
                    {
                        rc.Tabs.Remove(existing);
                        App.Log("RibbonManager.UnloadRibbon: Tab removed.");
                        Editor ed = AcApp.DocumentManager.MdiActiveDocument?.Editor;
                        ed?.WriteMessage("\n小辰CAD工具箱 Ribbon 已卸载。");
                    }
                }
                _isRibbonLoaded = false;
            }
            catch (Exception ex)
            {
                App.Log($"RibbonManager.UnloadRibbon ERROR: {ex.Message}");
            }
        }

        // ---- helpers ----

        private static RibbonPanel BuildPanel(string title,
            params RibbonButton[][] rowGroups)
        {
            var src = new RibbonPanelSource { Title = title };
            var panel = new RibbonPanel { Source = src };

            foreach (RibbonButton[] row in rowGroups)
            {
                foreach (RibbonButton btn in row)
                {
                    src.Items.Add(btn);
                }
            }
            return panel;
        }

        private static RibbonButton MakeButton(string text, string command, string tooltip)
        {
            var btn = new RibbonButton
            {
                Text = text,
                ShowText = true,
                ShowImage = false,
                ToolTip = tooltip,
                CommandHandler = new RibbonCommand(command),
            };
            return btn;
        }

        /// <summary>简单 IRibbonCommand 实现：按钮点击时把命令送到活动文档</summary>
        private class RibbonCommand : System.Windows.Input.ICommand
        {
            private readonly string _command;
            public event EventHandler CanExecuteChanged { add { } remove { } }
            public bool CanExecute(object parameter) => true;

            public RibbonCommand(string command)
            {
                _command = command;
            }

            public void Execute(object parameter)
            {
                try
                {
                    Document doc = AcApp.DocumentManager.MdiActiveDocument;
                    if (doc == null) return;
                    doc.SendStringToExecute(_command + " ", true, false, true);
                }
                catch (Exception ex)
                {
                    App.Log($"RibbonCommand '{_command}' Execute ERROR: {ex.Message}");
                }
            }
        }
    }
}
