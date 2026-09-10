using System;
using System.IO;
using System.Windows;
using System.Xml.Serialization;

namespace XcTools.Services
{
    /// <summary>
    /// 窗口状态服务，用于保存和恢复窗口位置和状态
    /// </summary>
    public class WindowStateService
    {
        private static string _settingsFilePath;

        static WindowStateService()
        {
            // 设置配置文件路径
            _settingsFilePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "XcTools", "WindowStates.xml");
            
            // 确保目录存在
            string directory = Path.GetDirectoryName(_settingsFilePath);
            if (!Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }
        }

        /// <summary>
        /// 保存窗口状态
        /// </summary>
        /// <param name="windowName">窗口名称</param>
        /// <param name="window">窗口对象</param>
        public static void SaveWindowState(string windowName, Window window)
        {
            try
            {
                // 创建窗口状态对象
                WindowStateData windowState = new WindowStateData
                {
                    WindowName = windowName,
                    Left = window.Left,
                    Top = window.Top,
                    Width = window.Width,
                    Height = window.Height,
                    WindowState = window.WindowState
                };

                // 读取现有配置
                WindowStateCollection windowStates = LoadAllWindowStates();

                // 查找并更新现有配置，或添加新配置
                var existingState = windowStates.WindowStates.Find(w => w.WindowName == windowName);
                if (existingState != null)
                {
                    existingState.Left = windowState.Left;
                    existingState.Top = windowState.Top;
                    existingState.Width = windowState.Width;
                    existingState.Height = windowState.Height;
                    existingState.WindowState = windowState.WindowState;
                }
                else
                {
                    windowStates.WindowStates.Add(windowState);
                }

                // 保存配置
                SaveAllWindowStates(windowStates);
            }
            catch (Exception ex)
            {
                // 忽略保存错误，不影响主功能
                System.Diagnostics.Debug.WriteLine($"保存窗口状态错误: {ex.Message}");
            }
        }

        /// <summary>
        /// 恢复窗口状态
        /// </summary>
        /// <param name="windowName">窗口名称</param>
        /// <param name="window">窗口对象</param>
        public static void RestoreWindowState(string windowName, Window window)
        {
            try
            {
                // 读取现有配置
                WindowStateCollection windowStates = LoadAllWindowStates();

                // 查找窗口状态
                var windowState = windowStates.WindowStates.Find(w => w.WindowName == windowName);
                if (windowState != null)
                {
                    // 恢复位置和大小
                    window.Left = windowState.Left;
                    window.Top = windowState.Top;
                    window.Width = windowState.Width;
                    window.Height = windowState.Height;
                    window.WindowState = windowState.WindowState;
                }
            }
            catch (Exception ex)
            {
                // 忽略恢复错误，不影响主功能
                System.Diagnostics.Debug.WriteLine($"恢复窗口状态错误: {ex.Message}");
            }
        }

        /// <summary>
        /// 加载所有窗口状态
        /// </summary>
        /// <returns>窗口状态集合</returns>
        private static WindowStateCollection LoadAllWindowStates()
        {
            try
            {
                if (File.Exists(_settingsFilePath))
                {
                    XmlSerializer serializer = new XmlSerializer(typeof(WindowStateCollection));
                    using (StreamReader reader = new StreamReader(_settingsFilePath))
                    {
                        return (WindowStateCollection)serializer.Deserialize(reader);
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"加载窗口状态错误: {ex.Message}");
            }

            // 返回默认配置
            return new WindowStateCollection();
        }

        /// <summary>
        /// 保存所有窗口状态
        /// </summary>
        /// <param name="windowStates">窗口状态集合</param>
        private static void SaveAllWindowStates(WindowStateCollection windowStates)
        {
            try
            {
                XmlSerializer serializer = new XmlSerializer(typeof(WindowStateCollection));
                using (StreamWriter writer = new StreamWriter(_settingsFilePath))
                {
                    serializer.Serialize(writer, windowStates);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"保存窗口状态集合错误: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// 窗口状态集合
    /// </summary>
    [XmlRoot("WindowStates")]
    public class WindowStateCollection
    {
        public WindowStateCollection()
        {
            WindowStates = new System.Collections.Generic.List<WindowStateData>();
        }

        [XmlElement("WindowState")]
        public System.Collections.Generic.List<WindowStateData> WindowStates { get; set; }
    }

    /// <summary>
    /// 窗口状态数据
    /// </summary>
    public class WindowStateData
    {
        [XmlAttribute("Name")]
        public string WindowName { get; set; }

        public double Left { get; set; }
        public double Top { get; set; }
        public double Width { get; set; }
        public double Height { get; set; }
        public WindowState WindowState { get; set; }
    }
}