// <copyright file="HeroAnimation.xaml.cs" company="Mozilla">
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not distributed with this file, you can obtain one at http://mozilla.org/MPL/2.0/.
// </copyright>

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using System.Windows.Threading;
using LottieSharp;

namespace FirefoxPrivateNetwork.UI.Components
{
    /// <summary>
    /// Interaction logic for HeroAnimation.xaml.
    /// </summary>
    public partial class HeroAnimation : UserControl, INotifyPropertyChanged
    {
        /// <summary>
        /// Indicates that the VPN status, triggering the animation to transition frames.
        /// </summary>
        public static readonly DependencyProperty StatusProperty = DependencyProperty.Register("Status", typeof(Models.ConnectionState), typeof(HeroAnimation), new PropertyMetadata(OnStatusChangedCallBack));

        private LottieAnimationView globeAnimation = null;

        /// <summary>
        /// Initializes a new instance of the <see cref="HeroAnimation"/> class.
        /// </summary>
        public HeroAnimation()
        {
            DataContext = Manager.MainWindowViewModel;
            InitializeComponent();
            ConstructGlobeAnimation();

        }

        /// <inheritdoc/>
        public event PropertyChangedEventHandler PropertyChanged;

        /// <summary>
        /// Gets or sets a value indicating the VPN status, triggering the animation to transition frames.
        /// </summary>
        public Models.ConnectionState Status
        {
            get
            {
                return (Models.ConnectionState)GetValue(StatusProperty);
            }

            set
            {
                SetValue(StatusProperty, value);
            }
        }

        /// <summary>
        /// Reacts when the property of the element has been changed.
        /// </summary>
        /// <param name="propertyName">Name of the property which has been changed.</param>
        public virtual void OnPropertyChanged(string propertyName)
        {
            PropertyChangedEventHandler handler = this.PropertyChanged;
            if (handler != null)
            {
                var e = new PropertyChangedEventArgs(propertyName);
                handler(this, e);
            }

            if (propertyName == "Status")
            {
                UpdateAnimation();
            }
        }

        private static void OnStatusChangedCallBack(DependencyObject sender, DependencyPropertyChangedEventArgs e)
        {
            HeroAnimation h = sender as HeroAnimation;
            if (h != null)
            {
                h.OnPropertyChanged("Status");
            }
        }

        private void UpdateAnimation()
        {

            switch (Status)
            {
                case Models.ConnectionState.Connecting:
                    globeAnimation.SetMinAndMaxFrame(0, 15);
                    break;
                case Models.ConnectionState.Protected:
                    globeAnimation.SetMinAndMaxFrame(15, 30);
                    break;
                case Models.ConnectionState.Disconnecting:
                    globeAnimation.SetMinAndMaxFrame(60, 75);
                    break;
                case Models.ConnectionState.Unprotected:
                    globeAnimation.SetMinAndMaxFrame(75, 90);
                    break;
                default:
                    break;
            }

            globeAnimation.ResumeAnimation();
        }

        private void ConstructGlobeAnimation()
        {
            try
            {
                var animationResourceKey = "globe";
                var animationFileName = Application.Current.Resources[animationResourceKey].ToString();
                string animationJson;

                using (var sr = new StreamReader(Application.GetResourceStream(new Uri(animationFileName)).Stream))
                {
                    animationJson = sr.ReadToEnd();
                }

                globeAnimation = new LottieAnimationView
                {
                    Name = "GlobeAnimation",
                    DefaultCacheStrategy = LottieAnimationView.CacheStrategy.Strong,
                    Speed = 1,
                    AutoPlay = false,
                    VerticalAlignment = VerticalAlignment.Center,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    RepeatCount = 0,
                };

                globeAnimation.ImageDrawable.Stretch = Stretch.None;
                globeAnimation.ImageDrawable.HorizontalAlignment = HorizontalAlignment.Center;
                globeAnimation.ImageDrawable.VerticalAlignment = VerticalAlignment.Center;
                globeAnimation.SetAnimationFromJsonAsync(animationJson, animationResourceKey);

            }
            catch (Exception)
            {
                ErrorHandling.ErrorHandler.WriteToLog("Failed to construct Lottie animation.", ErrorHandling.LogLevel.Error);
            }

            HeroAnimationContainer.Children.Add(globeAnimation);
            globeAnimation.Visibility = Visibility.Visible;
        }

    }
}
