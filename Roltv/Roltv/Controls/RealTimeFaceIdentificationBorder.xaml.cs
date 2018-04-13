using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Foundation;
using Windows.Foundation.Collections;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Controls.Primitives;
using Windows.UI.Xaml.Data;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Navigation;


namespace Roltv.Controls
{
    public sealed partial class RealTimeFaceIdentificationBorder : UserControl
    {
        public RealTimeFaceIdentificationBorder()
        {
            this.InitializeComponent();
        }

        public void ShowFaceRectangle(double left, double top, double width, double height)
        {
            this.faceRectangle.Margin = new Thickness(left, top, 0, 0);
            this.faceRectangle.Width = width;
            this.faceRectangle.Height = height;

            this.faceRectangle.Visibility = Visibility.Visible;
        }

        //public void ShowRealTimeEmotionData(EmotionScores scores)
        //{
        //    this.emotionEmojiControl.UpdateEmotion(scores);
        //}

        public void ShowIdentificationData(double age, string gender, uint confidence, string name = null, string uniqueId = null)
        {
            int roundedAge = (int)Math.Round(age);

            if (!string.IsNullOrEmpty(name))
            {
                this.captionTextHeader.Text = string.Format("{0}, {1} ({2}%)", name, roundedAge, confidence);
            }
            else if (!string.IsNullOrEmpty(gender))
            {
                this.captionTextHeader.Text = string.Format("{0}, {1}", roundedAge.ToString(), gender);
            }

            if (uniqueId != null)
            {
                this.captionTextSubHeader.Text = string.Format("Face Id: {0}", uniqueId);
            }

            this.captionBorder.Visibility = Visibility.Visible;
            this.captionBorder.Margin = new Thickness(this.faceRectangle.Margin.Left - (this.captionBorder.Width - this.faceRectangle.Width) / 2,
                                                    this.faceRectangle.Margin.Top - this.captionBorder.Height - 2, 0, 0);
        }
    }
}
