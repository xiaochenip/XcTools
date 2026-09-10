using System;
using System.IO;
using System.Xml.Serialization;

namespace XcTools.Services
{
    public class SettingsService
    {
        private static string _settingsFilePath;

        static SettingsService()
        {
            _settingsFilePath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "XcTools",
                "Settings.xml");

            string directory = Path.GetDirectoryName(_settingsFilePath);
            if (!Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }
        }

        public static string GetLibraryPath()
        {
            AppSettings settings = LoadSettings();
            if (!string.IsNullOrEmpty(settings.LibraryPath) && Directory.Exists(settings.LibraryPath))
            {
                return settings.LibraryPath;
            }

            return LibraryManagerService.GetDefaultLibraryPath();
        }

        public static void SetLibraryPath(string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                throw new ArgumentException("路径不能为空", nameof(path));
            }

            if (!Directory.Exists(path))
            {
                Directory.CreateDirectory(path);
            }

            AppSettings settings = LoadSettings();
            settings.LibraryPath = path;
            SaveSettings(settings);
        }

        public static void ResetLibraryPath()
        {
            AppSettings settings = LoadSettings();
            settings.LibraryPath = null;
            SaveSettings(settings);
        }

        public static AppSettings LoadSettings()
        {
            try
            {
                if (File.Exists(_settingsFilePath))
                {
                    XmlSerializer serializer = new XmlSerializer(typeof(AppSettings));
                    using (StreamReader reader = new StreamReader(_settingsFilePath))
                    {
                        return (AppSettings)serializer.Deserialize(reader);
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"加载配置错误: {ex.Message}");
            }

            return new AppSettings();
        }

        public static void SaveSettings(AppSettings settings)
        {
            try
            {
                XmlSerializer serializer = new XmlSerializer(typeof(AppSettings));
                using (StreamWriter writer = new StreamWriter(_settingsFilePath))
                {
                    serializer.Serialize(writer, settings);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"保存配置错误: {ex.Message}");
            }
        }

        public static string GetSettingsFilePath()
        {
            return _settingsFilePath;
        }
    }

    [XmlRoot("Settings")]
    public class AppSettings
    {
        [XmlElement("LibraryPath")]
        public string LibraryPath { get; set; }

        public AppSettings()
        {
            LibraryPath = null;
        }
    }
}
