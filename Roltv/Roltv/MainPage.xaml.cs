using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading.Tasks;
using Windows.ApplicationModel;
using Windows.Foundation;
using Windows.Foundation.Collections;
using Windows.Media.Capture;
using Windows.UI.Core;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Controls.Primitives;
using Windows.UI.Xaml.Data;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Navigation;

namespace Roltv
{
    public sealed partial class MainPage : Page
    {
        private MediaCapture mediaCapture;

        public MainPage()
        {
            InitializeComponent();

            NavigationCacheMode = NavigationCacheMode.Disabled;
            Application.Current.Suspending += ApplicationSuspending;

            InitializeCameraAsync();
        }

        private async void InitializeCameraAsync()
        {
            try
            {
                if (mediaCapture == null)
                {
                    mediaCapture = new MediaCapture();
                    await mediaCapture.InitializeAsync();

                    mediaCapture.Failed += new MediaCaptureFailedEventHandler(MediaCaptureFailed);

                    previewElement.Source = mediaCapture;
                    await mediaCapture.StartPreviewAsync();


                    UpdateStatus("Camera found.  Initialized for video recording", 1000);
                }
            }
            catch (Exception ex)
            {
                UpdateStatus($"Unable to initialize camera: {ex.Message}");
            }
        }

        private async void MediaCaptureFailed(MediaCapture sender, MediaCaptureFailedEventArgs currentFailure)
        {
            await Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, () =>
            {
                try
                {
                    status.Text = "MediaCaptureFailed: " + currentFailure.Message;
                }
                catch (Exception)
                {
                }
                finally
                {
                    status.Text += "\nCheck if camera is diconnected. Try re-launching the app";
                }
            });
        }

        private async void UpdateStatus(string message, int delay = 0)
        {
            await Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, () =>
                {
                    status.Text = message;
                    Task.Delay(delay);
                });
        }

        private async void ApplicationSuspending(object sender, SuspendingEventArgs e)
        {
            // Handle global application events only if this page is active
            if (Frame.CurrentSourcePageType == typeof(MainPage))
            {
                var deferral = e.SuspendingOperation.GetDeferral();
                await CleanupCameraAsync();
                deferral.Complete();
            }
        }

        protected async override void OnNavigatedFrom(NavigationEventArgs e)
        {
            await CleanupCameraAsync();
        }

        private async Task CleanupCameraAsync()
        {
            if (mediaCapture != null)
            {
                //if (isPreviewing)
                //{
                //    await mediaCapture.StopPreviewAsync();
                //}

                await Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
                {
                    previewElement.Source = null;
                    //if (displayRequest != null)
                    //{
                    //    displayRequest.RequestRelease();
                    //}

                    mediaCapture.Dispose();
                    mediaCapture = null;
                });
            }

        }
    }
}
