using System;
using System.IO;
using System.Xml.Serialization;
using VideoDownloader.Core.Models;
using Microsoft.Extensions.Logging;
using System.Reflection;

namespace VideoDownloader.Core.Services
{
    public class ConfigurationService
    {
        private readonly string _configPath;
        private readonly ILogger<ConfigurationService> _logger;
        private static readonly XmlSerializer _serializer = new XmlSerializer(typeof(DownloadSettings));

        public ConfigurationService(ILogger<ConfigurationService> logger)
        {
            _logger = logger;
            string baseDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            _configPath = Path.Combine(baseDir, "config.xml");
        }

        public DownloadSettings LoadConfiguration()
        {
            try
            {
                if (File.Exists(_configPath))
                {
                    using var stream = File.OpenRead(_configPath);
                    var settings = (DownloadSettings)_serializer.Deserialize(stream);
                    _logger.LogInformation("Configuration loaded successfully from {Path}", _configPath);
                    return settings;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to load configuration from {Path}", _configPath);
            }

            // Return default settings if file doesn't exist or loading fails
            var defaultSettings = new DownloadSettings();
            SaveConfiguration(defaultSettings); // Create the file with defaults
            return defaultSettings;
        }

        public void SaveConfiguration(DownloadSettings settings)
        {
            try
            {
                using var stream = File.Create(_configPath);
                _serializer.Serialize(stream, settings);
                _logger.LogInformation("Configuration saved successfully to {Path}", _configPath);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to save configuration to {Path}", _configPath);
            }
        }
    }
}
