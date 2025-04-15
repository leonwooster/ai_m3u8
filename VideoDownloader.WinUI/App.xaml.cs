using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Serilog;
using Serilog.Events;
using Microsoft.UI.Xaml.Navigation;
using VideoDownloader.Core.Services;
using VideoDownloader.WinUI.Views;
using System.IO;

namespace VideoDownloader.WinUI
{
    public partial class App : Application
    {
        private const string LOG_FILE_PATH = "logs/videodownloader-.log";
        private const int MAX_LOG_FILE_SIZE = 10 * 1024 * 1024; // 10MB
        private const int MAX_LOG_FILE_COUNT = 5;

        /// <summary>
        /// Gets the <see cref="IServiceProvider"/> instance for the application.
        /// </summary>
        public IServiceProvider Services { get; }

        private Window? window;

        /// <summary>
        /// Provides access to the main application window.
        /// Throws an exception if accessed before the window is initialized.
        /// </summary>
        public Window MainWindow
        {
            get
            {
                // Ensure window is initialized before accessing
                // Should be initialized by OnLaunched before this is needed
                return window ?? throw new InvalidOperationException("The main application window is not initialized yet.");
            }
        }

        /// <summary>
        /// Configures the services for the application.
        /// </summary>
        private static IServiceProvider ConfigureServices()
        {
            var services = new ServiceCollection();

            // Configure Serilog
            var logPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, LOG_FILE_PATH);
            var logDir = Path.GetDirectoryName(logPath);
            if (!Directory.Exists(logDir))
            {
                Directory.CreateDirectory(logDir!);
            }

            Log.Logger = new LoggerConfiguration()
                .MinimumLevel.Debug()
                .WriteTo.Debug()
                .WriteTo.File(
                    path: logPath,
                    rollingInterval: RollingInterval.Day,
                    retainedFileCountLimit: MAX_LOG_FILE_COUNT,
                    fileSizeLimitBytes: MAX_LOG_FILE_SIZE,
                    rollOnFileSizeLimit: true,
                    outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff} [{Level:u3}] {Message:lj}{NewLine}{Exception}"
                )
                .CreateLogger();

            // Add Logging
            services.AddLogging(configure =>
            {
                configure.ClearProviders();
                configure.AddSerilog(dispose: true);
            });

            // Add HTTP Client Factory
            // Configure default User-Agent for HttpClient used by DownloadService
            services.AddHttpClient<DownloadService>(client =>
            {
                client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/91.0.4472.124 Safari/537.36");
            })
            .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
            {
                AllowAutoRedirect = true, 
                AutomaticDecompression = System.Net.DecompressionMethods.GZip | System.Net.DecompressionMethods.Deflate
            });

            // Add Core Services
            services.AddSingleton<M3U8Parser>(); 
            services.AddTransient<DownloadService>(); 

            // Add ViewModels or Pages 
            services.AddTransient<MainPage>();

            return services.BuildServiceProvider();
        }

        public App()
        {
            InitializeComponent();
            Services = ConfigureServices(); 
            Log.Information("Application started");
        }

        /// <summary>
        /// Initializes the singleton application object.  This is the first line of authored code
        /// executed, and as such is the logical equivalent of main() or WinMain().
        /// </summary>
        protected override void OnLaunched(LaunchActivatedEventArgs e)
        {
            try
            {
                Log.Information("Application launching - Creating main window");
                window = new Window();
                Log.Information("Window instance created");

                // Subscribe to window closing event
                window.Closed += (sender, args) =>
                {
                    Log.Information("Application closing");
                    Log.CloseAndFlush();
                };

                Log.Information("Setting up window content and frame");
                Frame rootFrame;
                if (window.Content is not Frame existingFrame)
                {
                    Log.Information("Creating new frame");
                    rootFrame = new Frame();
                    rootFrame.NavigationFailed += OnNavigationFailed;
                    window.Content = rootFrame;
                }
                else
                {
                    Log.Information("Using existing frame");
                    rootFrame = existingFrame;
                }

                if (rootFrame.Content == null)
                {
                    Log.Information("Navigating to MainPage");
                    rootFrame.Navigate(typeof(MainPage), e.Arguments);
                }

                Log.Information("Activating window");
                window.Activate();
                Log.Information("Window activated successfully");
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Critical error during application launch");
                throw; // Re-throw to ensure the app terminates if there's a critical error
            }
        }

        /// <summary>
        /// Invoked when Navigation to a certain page fails
        /// </summary>
        /// <param name="sender">The Frame which failed navigation</param>
        /// <param name="e">Details about the navigation failure</param>
        private void OnNavigationFailed(object sender, NavigationFailedEventArgs e)
        {
            Log.Error(e.Exception, "Navigation failed to {SourcePageType}", e.SourcePageType.FullName);
            throw new Exception("Failed to load Page " + e.SourcePageType.FullName);
        }
    }
}
