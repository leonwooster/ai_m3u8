using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml.Navigation;
using VideoDownloader.Core.Services;
using VideoDownloader.WinUI.Views;

namespace VideoDownloader.WinUI
{
    public partial class App : Application
    {
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

            // Add Logging
            services.AddLogging(configure =>
            {
                configure.AddDebug(); 
                // configure.AddConsole(); 
                // Add other providers as needed
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
        }

        /// <summary>
        /// Initializes the singleton application object.  This is the first line of authored code
        /// executed, and as such is the logical equivalent of main() or WinMain().
        /// </summary>
        protected override void OnLaunched(LaunchActivatedEventArgs e)
        {
             window ??= new Window(); 

            // Note: Standard DI integration with Frame navigation is more complex.
            // For now, MainPage will resolve its dependencies manually via App.Services.
            // A more robust solution involves a custom Frame or navigation service.

            if (window.Content is not Frame rootFrame)
            {
                rootFrame = new Frame();
                rootFrame.NavigationFailed += OnNavigationFailed;
                window.Content = rootFrame;
            }

            if (rootFrame.Content == null)
            {
                // When the navigation stack isn't restored navigate to the first page,
                // configuring the new page by passing required information as a navigation
                // parameter
                 rootFrame.Navigate(typeof(MainPage), e.Arguments);
            }


            window.Activate();
        }

        /// <summary>
        /// Invoked when Navigation to a certain page fails
        /// </summary>
        /// <param name="sender">The Frame which failed navigation</param>
        /// <param name="e">Details about the navigation failure</param>
        void OnNavigationFailed(object sender, NavigationFailedEventArgs e)
        {
            // Log the error using DI logger if possible, or throw
            var logger = Services.GetService<ILogger<App>>();
            logger?.LogError("Failed to load Page {PageFullName}. Source: {SourcePageType}", e.SourcePageType.FullName, e.SourcePageType);

            // For now, just throw to indicate a critical failure
            throw new Exception("Failed to load Page " + e.SourcePageType.FullName);
        }
    }
}
