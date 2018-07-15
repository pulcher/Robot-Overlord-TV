using Microsoft.ProjectOxford.Face;
using Microsoft.ProjectOxford.Face.Contract;
using Roltv.Controls;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Windows.ApplicationModel;
using Windows.Foundation;
using Windows.Graphics.Imaging;
using Windows.Media;
using Windows.Media.Capture;
using Windows.Media.FaceAnalysis;
using Windows.Media.MediaProperties;
using Windows.Storage.Streams;
using Windows.System.Threading;
using Windows.UI.Core;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Navigation;
using Windows.Media.SpeechSynthesis;

namespace Roltv
{
    public sealed partial class MainPage : Page
    {
        private const int framesPerSecond = 10; // current limit on the plan for processing images

        private IFaceServiceClient faceServiceClient;
        private MediaCapture mediaCapture;
        private ThreadPoolTimer frameProcessingTimer;
        private FaceTracker faceTracker;
        private SemaphoreSlim frameProcessingSimaphore = new SemaphoreSlim(1);
        private VideoEncodingProperties videoProperties;
        private SpeechSynthesizer synth = new SpeechSynthesizer();

        // probably these need to be someplace different
        private Face[] globalFaces;
        private String[] facesDescription;
        private IEnumerable<FaceAttributeType> requiredFaceAttributes;
        private PersonGroup[] personGroups;

        // detect whether a face is there are not for other systems to kick in.
        private bool facesExistInFrame;  // probably should make this a delagate for when faces show and not.

        public MainPage()
        {
            InitializeComponent();

            NavigationCacheMode = NavigationCacheMode.Disabled;
            Application.Current.Suspending += ApplicationSuspending;

            InitializeCameraAsync();
            InitializeFaceTracking();
            InitializeFaceApi();
        }

        private async void InitializeFaceApi()
        {
            faceServiceClient = new FaceServiceClient("64424d4e9d114614a4c46fe258b272a7", "https://southcentralus.api.cognitive.microsoft.com/face/v1.0");

            // See https://docs.microsoft.com/en-us/azure/cognitive-services/face/glossary#a for the current list of supported options.
            requiredFaceAttributes = new FaceAttributeType[]
            {
                FaceAttributeType.Age,
                FaceAttributeType.Gender,
                FaceAttributeType.HeadPose,
                FaceAttributeType.Smile,
                FaceAttributeType.FacialHair,
                FaceAttributeType.Glasses,
                FaceAttributeType.Emotion,
                FaceAttributeType.Hair,
                FaceAttributeType.Makeup,
                FaceAttributeType.Occlusion,
                FaceAttributeType.Accessories,
                FaceAttributeType.Blur,
                FaceAttributeType.Exposure,
                FaceAttributeType.Noise
            };

            personGroups = await faceServiceClient.ListPersonGroupsAsync();

            var test = await faceServiceClient.ListPersonsInPersonGroupAsync(personGroups[0].PersonGroupId);

            var test3 = test[0].PersistedFaceIds;

            var test4 = await faceServiceClient.GetPersonFaceInPersonGroupAsync(personGroups[0].PersonGroupId, test[0].PersonId, test[0].PersistedFaceIds[0]);

            var test2 = 0;
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

                        if (!facesExistInFrame)
                        {
                            await Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
                            {
                                // Enable the Train feature and disable the other buttons
                                PageYes.Visibility = Visibility.Collapsed;
                                PageNo.Visibility = Visibility.Collapsed;
                                TrainMe.Visibility = Visibility.Visible;
                            });
                        }

                        if (faces.Any())
                        {
                            if (!facesExistInFrame)
                            {
                                facesExistInFrame = true;

                                await Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
                                {
                                    // Enable the Yes/No buttons.  Disable the Train Button
                                    PageYes.Visibility = Visibility.Visible;
                                    PageNo.Visibility = Visibility.Visible;
                                    TrainMe.Visibility = Visibility.Collapsed;
                                });

                                await ShowMessage("Will you help me?  If so, make sure I can see you face and click \"Yse\"", 1);
                            }

                            if (faces.Count() > 1)
                            {
                                await ShowMessage("Can only identify when multiple faces are visible.");

                                await Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
                                {
                                    // Disable the Yes/No buttons.
                                    PageYes.Visibility = Visibility.Collapsed;
                                    PageNo.Visibility = Visibility.Collapsed;
                                });
                            }
                            else
                            {
                                await Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
                                {
                                    // Enable the Yes/No Buttons
                                    PageYes.Visibility = Visibility.Visible;
                                    PageNo.Visibility = Visibility.Visible;
                                    TrainMe.Visibility = Visibility.Collapsed;
                                });

                                var captureStream = new MemoryStream();
                                await mediaCapture.CapturePhotoToStreamAsync(ImageEncodingProperties.CreatePng(), captureStream.AsRandomAccessStream());
                                captureStream.AsRandomAccessStream().Seek(0);

                                // ask the face api what it sees
                                // See: https://docs.microsoft.com/en-us/azure/cognitive-services/face/face-api-how-to-topics/howtodetectfacesinimage
                                var globalFaces = await faceServiceClient.DetectAsync(captureStream, true, true, requiredFaceAttributes);

                            }

                            var previewFrameSize = new Size(previewFrame.SoftwareBitmap.PixelWidth, previewFrame.SoftwareBitmap.PixelHeight);
                            await Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
                            {
                                ShowFaceTracking(faces, previewFrameSize);
                                ShowIdentificationiStatus(globalFaces);
                            });

                            var firstFace = faces.FirstOrDefault();
                        }
                        else
                        {
                            facesExistInFrame = false;
                            // reset the stuff because there are no faces to analyze.

                            await ShowMessage(String.Empty);
                            await Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
                            {
                                ShowFaceTracking(faces, new Size());
                            });
                        }

                        var test = faces.Count();
                    }
                }
            }
            catch (Exception ex)
            {
                var test = ex;

                
                // face detection failed for some reason.
            }
            finally
            {
                frameProcessingSimaphore.Release();
            }
        }

        private async void ShowIdentificationiStatus(Face[] globalFaces)
        {
            if (globalFaces == null)
                return;

            var message = new StringBuilder();

            message.Append($"Number of faces visible: {globalFaces.Length}.  Recognizing the following: ");

            var foundFaces = globalFaces.Select(x => x.FaceId).ToArray();

            if (foundFaces.Any())
            {

                foreach (var group in personGroups)
                {
                    var results = await faceServiceClient.IdentifyAsync(group.PersonGroupId, foundFaces);

                    foreach (var processedResult in results)
                    {
                        if (processedResult.Candidates.Length > 0)
                        {
                            var person = await faceServiceClient
                                .GetPersonInPersonGroupAsync(group.PersonGroupId, processedResult.Candidates[0].PersonId);

                            message.Append($"({group.Name}){person.Name}");
                            message.Append(", ");
                        }
                    }
                }
            }

            await ShowMessage(message.ToString());
        }

        private async Task ShowMessage(string message, int delay = 0)
        {
            await Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
            {
                InPicture.Text = message;
                Task.Delay(delay);
            });
        }

        private async Task<MemoryStream> GetImageAsStream(SoftwareBitmap softwareBitmap, Guid guid)
        {
            MemoryStream theStream;
            byte[] array = null;

            var memoryStream = new InMemoryRandomAccessStream();

            // Get a way to get a hold of the image bits
                var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.BmpEncoderId, memoryStream);
            
                encoder.SetSoftwareBitmap(softwareBitmap);

                try
                {
                    await encoder.FlushAsync();
                }
                catch( Exception ex )
                {
                    return new MemoryStream();
                }

                // make the data array large enough to hold on to the bits
                array = new byte[memoryStream.Size];

                // Copy all the bits to the array
                await memoryStream.ReadAsync(array.AsBuffer(), (uint)memoryStream.Size, InputStreamOptions.None);

                // Create the stream using the bits in the array
                theStream = new MemoryStream(array);
            
            return theStream;
        }

        private void ShowFaceTracking(IEnumerable<DetectedFace> faces, Size framePixelSize)
        {
            // Clear off all the junk
            PreviewVisualizer.Children.Clear();

            var actualWidth = PreviewElement.ActualWidth;
            var actualHeight = PreviewElement.ActualHeight;

            if (mediaCapture == null)
                return;

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
