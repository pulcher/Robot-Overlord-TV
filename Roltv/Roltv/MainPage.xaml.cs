using Roltv.Controls;
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
            InitializeFaceTracking();
        }

        private async void InitializeFaceTracking()
        {
            if (faceTracker == null)
            {
                faceTracker = await FaceTracker.CreateAsync();

                var timerInterval = TimeSpan.FromMilliseconds(1000 / framesPerSecond); // gets us seconds/frame
                frameProcessingTimer = ThreadPoolTimer.CreatePeriodicTimer(new TimerElapsedHandler(ProcessVideoFrame), timerInterval);

                UpdateStatus("Face detection initiated...");
            }
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
                     
                    PreviewElement.Source = mediaCapture;
                    PreviewElement.FlowDirection = FlowDirection.RightToLeft;

                    videoProperties = mediaCapture.VideoDeviceController
                        .GetMediaStreamProperties(MediaStreamType.VideoPreview) as VideoEncodingProperties;

                    //mediaCapture.SetPreviewRotation(VideoRotation.Clockwise180Degrees);

                    await mediaCapture.StartPreviewAsync();

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
                    await mediaCapture.GetPreviewFrameAsync(previewFrame);

                    if (FaceTracker.IsBitmapPixelFormatSupported(previewFrame.SoftwareBitmap.BitmapPixelFormat))
                    {
                        faces = await faceTracker.ProcessNextFrameAsync(previewFrame);

                        if (faces.Any())
                        {
                            var previewFrameSize = new Size(previewFrame.SoftwareBitmap.PixelWidth, previewFrame.SoftwareBitmap.PixelHeight);
                            await Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
                            {
                                ShowFaceTracking(faces, previewFrameSize);
                            });
                        }

                        var firstFace = faces.FirstOrDefault();

                        var test = faces.Count();
                    }
                }
            }
            catch (Exception)
            {
                // face detection failed for some reason.
            }
            finally
            {
                frameProcessingSimaphore.Release();
            }
        }

        private void ShowFaceTracking(IEnumerable<DetectedFace> faces, Size framePixelSize)
        {
            // Clear off all the junk
            PreviewVisualizer.Children.Clear();

            var actualWidth = PreviewElement.ActualWidth;
            var actualHeight = PreviewElement.ActualHeight;

            if (mediaCapture.CameraStreamState == Windows.Media.Devices.CameraStreamState.Streaming
                && faces.Any() && actualHeight != 0 && actualWidth !=0)
            {
                var widthScale = framePixelSize.Width / actualWidth;
                var heightScale = framePixelSize.Height / actualHeight;

                foreach (var face in faces)
                {
                    var faceBorder = new RealTimeFaceIdentificationBorder();
                    PreviewVisualizer.Children.Add(faceBorder);

                    faceBorder.ShowFaceRectangle((uint)(face.FaceBox.X / widthScale), (uint)(face.FaceBox.Y / heightScale),
                        (uint)(face.FaceBox.Width / widthScale), (uint)(face.FaceBox.Height / heightScale));

                    PreviewVisualizer.Children.Add(new TextBlock
                    {
                        Text = string.Format("Coverage: {0:0}%", 100 * ((double)face.FaceBox.Height / this.videoProperties.Height)),
                        Margin = new Thickness((uint)(face.FaceBox.X / widthScale), (uint)(face.FaceBox.Y / heightScale), 0, 0)
                    });
                }
            }
        }

        private async void MediaCaptureFailed(MediaCapture sender, MediaCaptureFailedEventArgs currentFailure)
        {
            await Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
            {
                try
                {
                    Status.Text = "MediaCaptureFailed: " + currentFailure.Message;
                }
                catch (Exception)
                {
                }
                finally
                {
                    Status.Text += "\nCheck if camera is diconnected. Try re-launching the app";
                }
            });
        }

        private async void UpdateStatus(string message, int delay = 0)
        {
            await Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
                {
                    Status.Text = message;
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
                    PreviewElement.Source = null;
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
