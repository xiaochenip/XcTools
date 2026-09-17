using System;
using System.Collections.Generic;
using System.IO;
using System.Xml.Serialization;

namespace XcTools.Models
{
    [XmlRoot("Settings")]
    public class Settings
    {
        [XmlElement("Theme")]
        public string Theme { get; set; } = "Light";
        
        [XmlArray("CommandMappings")]
        [XmlArrayItem("CommandMapping")]
        public List<CommandMapping> CommandMappings { get; set; } = new List<CommandMapping>();
        
        [XmlArray("Shortcuts")]
        [XmlArrayItem("Shortcut")]
        public List<Shortcut> Shortcuts { get; set; } = new List<Shortcut>();
        
        public static Settings Load()
        {
            string settingsPath = GetSettingsPath();
            if (File.Exists(settingsPath))
            {
                try
                {
                    XmlSerializer serializer = new XmlSerializer(typeof(Settings));
                    using (StreamReader reader = new StreamReader(settingsPath))
                    {
                        return (Settings)serializer.Deserialize(reader);
                    }
                }
                catch
                {
                    return GetDefaultSettings();
                }
            }
            return GetDefaultSettings();
        }
        
        public void Save()
        {
            string settingsPath = GetSettingsPath();
            try
            {
                XmlSerializer serializer = new XmlSerializer(typeof(Settings));
                using (StreamWriter writer = new StreamWriter(settingsPath))
                {
                    serializer.Serialize(writer, this);
                }
            }
            catch
            {
            }
        }
        
        private static string GetSettingsPath()
        {
            string appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            string settingsDir = Path.Combine(appDataPath, "XcTools");
            if (!Directory.Exists(settingsDir))
            {
                Directory.CreateDirectory(settingsDir);
            }
            return Path.Combine(settingsDir, "settings.xml");
        }
        
        private static Settings GetDefaultSettings()
        {
            Settings settings = new Settings();
            settings.CommandMappings.Add(new CommandMapping { OriginalCommand = "XCCAD_LIBRARY", CustomCommand = "XTK" });
            settings.CommandMappings.Add(new CommandMapping { OriginalCommand = "XCCAD_INSERTDWG", CustomCommand = "XCI" });
            settings.CommandMappings.Add(new CommandMapping { OriginalCommand = "XCCAD_ADDFROMCAD", CustomCommand = "XCX" });
            settings.CommandMappings.Add(new CommandMapping { OriginalCommand = "XCH", CustomCommand = "XCH" });
            settings.CommandMappings.Add(new CommandMapping { OriginalCommand = "XHP", CustomCommand = "XHP" });
            return settings;
        }
    }
    
    public class CommandMapping
    {
        [XmlElement("OriginalCommand")]
        public string OriginalCommand { get; set; }
        
        [XmlElement("CustomCommand")]
        public string CustomCommand { get; set; }
    }
    
    public class Shortcut
    {
        [XmlElement("Command")]
        public string Command { get; set; }
        
        [XmlElement("Key")]
        public string Key { get; set; }
        
        [XmlElement("ModifierKeys")]
        public string ModifierKeys { get; set; }
    }
}