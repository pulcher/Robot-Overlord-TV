using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading;
using System.Threading.Tasks;
using Windows.ApplicationModel;
using Windows.Foundation;
using Windows.Foundation.Collections;
using Windows.Graphics.Imaging;
using Windows.Media;
using Windows.Media.Capture;
using Windows.Media.FaceAnalysis;
using Windows.Media.MediaProperties;
using Windows.System.Threading;
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
        private const int framesPerSecond = 10; // current limit on the plan for processing images

        private MediaCapture mediaCapture;
        private ThreadPoolTimer frameProcessingTimer;
        private FaceTracker faceTracker;
        private SemaphoreSlim frameProcessingSimaphore = new SemaphoreSlim(1);
        private VideoEncodingProperties videoProperties;

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
                    await SetCaptureResolution();

                    mediaCapture.Failed += new MediaCaptureFailedEventHandler(MediaCaptureFailed);
                     
                    previewElement.Source = mediaCapture;
                    previewElement.FlowDirection = FlowDirection.RightToLeft;

                    videoProperties = mediaCapture.VideoDeviceController
                        .GetMediaStreamProperties(MediaStreamType.VideoPreview) as VideoEncodingProperties;

                    //mediaCapture.SetPreviewRotation(VideoRotation.Clockwise180Degrees);

                    await mediaCapture.StartPreviewAsync();

                    var test = 1000 / framesPerSecond;

                    var timerInterval = TimeSpan.FromMilliseconds(1000 / framesPerSecond); // gets us seconds/frame
                    frameProcessingTimer = ThreadPoolTimer.CreatePeriodicTimer(new TimerElapsedHandler(ProcessVideoFrame), timerInterval);

                    UpdateStatus("Camera found.  Initialized for video recording", 1000);
                }
            }
            catch (Exception ex)
            {
                UpdateStatus($"Unable to initialize camera: {ex.Message}");
            }
        }

        private async Task SetCaptureResolution()
        {
            var availableResolutions = mediaCapture.VideoDeviceController.GetAvailableMediaStreamProperties(MediaStreamType.VideoPreview)
                .Cast<VideoEncodingProperties>().OrderByDescending(v => v.Width * v.Height * (v.FrameRate.Numerator / v.FrameRate.Denominator));

            uint maxHeight = 720;
            var videoEncodingSetting = availableResolutions.FirstOrDefault(v => v.Height <= maxHeight);

            if(videoEncodingSetting != null)
            {
                await mediaCapture.VideoDeviceController.SetMediaStreamPropertiesAsync(MediaStreamType.VideoPreview, videoEncodingSetting);
            }
        }

        private async void ProcessVideoFrame(ThreadPoolTimer timer)
        {
            if (!frameProcessingSimaphore.Wait(0))
            {
                // We are already doing something
                return;
            }

            try
            {
                IEnumerable<DetectedFace> faces = null;

                const BitmapPixelFormat inputPixelFormat = BitmapPixelFormat.Nv12;

                using (var previewFrame = new VideoFrame(inputPixelFormat, (int)videoProperties.Width, (int)videoProperties.Height))
                {
                    var test = "";

                    await mediaCapture.GetPreviewFrameAsync(previewFrame);

                    if (FaceTracker.IsBitmapPixelFormatSupported(previewFrame.SoftwareBitmap.BitmapPixelFormat))
                    {
                        faces = await faceTracker.ProcessNextFrameAsync(previewFrame);
                    }
                }
            }
            catch (Exception)
            {
            }
            finally
            {
                frameProcessingSimaphore.Release();
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
