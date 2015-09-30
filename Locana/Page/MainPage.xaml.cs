﻿#define WINDOWS_APP

using Kazyx.ImageStream;
using Kazyx.RemoteApi;
using Kazyx.RemoteApi.Camera;
using Kazyx.Uwpmm.CameraControl;
using Kazyx.Uwpmm.Control;
using Kazyx.Uwpmm.DataModel;
using Kazyx.Uwpmm.Settings;
using Kazyx.Uwpmm.Utility;
using Naotaco.ImageProcessor.Histogram;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using Windows.Data.Xml.Dom;
using Windows.Foundation;
using Windows.Storage;
using Windows.UI.Core;
using Windows.UI.Notifications;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Media.Animation;

// The Blank Page item template is documented at http://go.microsoft.com/fwlink/?LinkId=402352&clcid=0x409

namespace Locana
{
    public sealed partial class MainPage : Page
    {
        public MainPage()
        {
            this.InitializeComponent();
            NetworkObserver.INSTANCE.CameraDiscovered += NetworkObserver_Discovered;
            NetworkObserver.INSTANCE.ForceRestart();
            MediaDownloader.Instance.Fetched += OnFetchdImage;
            InitializeUI();
        }

        private void InitializeUI()
        {
            HistogramControl.Init(Histogram.ColorType.White, 800);

            HistogramCreator = null;
            HistogramCreator = new HistogramCreator(HistogramCreator.HistogramResolution.Resolution_256);
            HistogramCreator.OnHistogramCreated += async (r, g, b) =>
            {
                await Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
                {
                    HistogramControl.SetHistogramValue(r, g, b);
                });
            };
        }

        private void Page_Loaded(object sender, RoutedEventArgs e)
        {
            InitializeVisualStates();
        }

        private void InitializeVisualStates()
        {
            var groups = VisualStateManager.GetVisualStateGroups(LayoutRoot);
            Debug.WriteLine("CurrentState: " + groups[0].CurrentState.Name);
            groups[0].CurrentStateChanged += (sender, e) =>
            {
                Debug.WriteLine("Width state changed: " + e.OldState.Name + " -> " + e.NewState.Name);
                switch (e.NewState.Name)
                {
                    case "WideState":
                        ControlPanelState = DisplayState.AlwaysVisible;
                        break;
                    case "NarrowState":
                        ControlPanelState = DisplayState.Collapsible;
                        break;
                }
            };
            groups[1].CurrentStateChanged += (sender, e) =>
            {
                Debug.WriteLine("Height state changed: " + e.OldState.Name + " -> " + e.NewState.Name);
                switch (e.NewState.Name)
                {
                    case "TallState":
                        ShootingParamSliderState = DisplayState.AlwaysVisible;
                        if (ShootingParamSliders.Visibility == Visibility.Collapsed)
                        {
                            ShootingParamSliders.Visibility = Visibility.Collapsed;
                            StartRotateAnimation(OpenSliderImage, 180, 0);
                        }
                        break;
                    case "ShortState":
                        ShootingParamSliderState = DisplayState.Collapsible;
                        if (ShootingParamSliders.Visibility == Visibility.Visible)
                        {
                            ShootingParamSliders.Visibility = Visibility.Visible;
                            StartRotateAnimation(OpenSliderImage, 0, 180);
                        }
                        break;
                }
            };
        }

        private HistogramCreator HistogramCreator;

        private void OnFetchdImage(StorageFolder folder, StorageFile file, GeotaggingResult result)
        {
            ShowToast("picture saved!", file);
        }

        private TargetDevice target;
        private StreamProcessor liveview = new StreamProcessor();
        private ImageDataSource liveview_data = new ImageDataSource();
        private ImageDataSource postview_data = new ImageDataSource();

        LiveviewScreenViewData ScreenViewData;

        async void NetworkObserver_Discovered(object sender, CameraDeviceEventArgs e)
        {
            var target = e.CameraDevice;
            try
            {
                await SequentialOperation.SetUp(target, liveview);
            }
            catch (Exception ex)
            {
                Debug.WriteLine("Failed setup: " + ex.Message);
                return;
            }

            this.target = target;
            target.Status.PropertyChanged += Status_PropertyChanged;

            await Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
            {
                ScreenViewData = new LiveviewScreenViewData(target);
                ScreenViewData.NotifyFriendlyNameUpdated();
                BatteryStatusDisplay.BatteryInfo = target.Status.BatteryInfo;
                LayoutRoot.DataContext = ScreenViewData;
                var panels = SettingPanelBuilder.CreateNew(target);
                var pn = panels.GetPanelsToShow();
                foreach (var panel in pn)
                {
                    ControlPanel.Children.Add(panel);
                }

                ShootingParamSliders.DataContext = new ShootingParamViewData() { Status = target.Status, Liveview = ScreenViewData };

                HideFrontScreen();
            });

            SetUIHandlers();
        }

        private void SetUIHandlers()
        {
            FnumberSlider.SliderOperated += async (s, arg) =>
            {
                DebugUtil.Log("Fnumber operated: " + arg.Selected);
                try { await target.Api.Camera.SetFNumberAsync(arg.Selected); }
                catch (RemoteApiException) { }
            };
            SSSlider.SliderOperated += async (s, arg) =>
            {
                DebugUtil.Log("SS operated: " + arg.Selected);
                try { await target.Api.Camera.SetShutterSpeedAsync(arg.Selected); }
                catch (RemoteApiException) { }
            };
            ISOSlider.SliderOperated += async (s, arg) =>
            {
                DebugUtil.Log("ISO operated: " + arg.Selected);
                try { await target.Api.Camera.SetISOSpeedAsync(arg.Selected); }
                catch (RemoteApiException) { }
            };
            EvSlider.SliderOperated += async (s, arg) =>
            {
                DebugUtil.Log("Ev operated: " + arg.Selected);
                try { await target.Api.Camera.SetEvIndexAsync(arg.Selected); }
                catch (RemoteApiException) { }
            };
            ProgramShiftSlider.SliderOperated += async (s, arg) =>
            {
                DebugUtil.Log("Program shift operated: " + arg.OperatedStep);
                try { await target.Api.Camera.SetProgramShiftAsync(arg.OperatedStep); }
                catch (RemoteApiException) { }
            };
        }

        private void HideFrontScreen()
        {
            ScreenViewData.IsWaitingConnection = false;
        }

        void Status_PropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            var status = sender as CameraStatus;
            switch (e.PropertyName)
            {
                case "BatteryInfo":
                    BatteryStatusDisplay.BatteryInfo = status.BatteryInfo;
                    break;
                case "ContShootingResult":
                    // EnqueueContshootingResult(status.ContShootingResult);
                    break;
                case "Status":
                    if (status.Status == EventParam.Idle)
                    {
                        // When recording is stopped, clear recording time.
                        status.RecordingTimeSec = 0;
                    }
                    break;
                default:
                    break;
            }
        }

        private bool IsRendering = false;

        async void liveview_JpegRetrieved(object sender, JpegEventArgs e)
        {
            if (IsRendering) { return; }

            IsRendering = true;
            await LiveviewUtil.SetAsBitmap(e.Packet.ImageData, liveview_data, HistogramCreator, Dispatcher);
            IsRendering = false;
        }

        void liveview_Closed(object sender, EventArgs e)
        {
            Debug.WriteLine("Liveview connection closed");
        }

        private void LiveviewImage_Loaded(object sender, RoutedEventArgs e)
        {
            var image = sender as Image;
            image.DataContext = liveview_data;
            liveview.JpegRetrieved += liveview_JpegRetrieved;
            liveview.Closed += liveview_Closed;
        }

        private void LiveviewImage_Unloaded(object sender, RoutedEventArgs e)
        {
            var image = sender as Image;
            image.DataContext = null;
            liveview.JpegRetrieved -= liveview_JpegRetrieved;
            liveview.Closed -= liveview_Closed;
            TearDownCurrentTarget();
        }

        private void TearDownCurrentTarget()
        {
            LayoutRoot.DataContext = null;
        }

        async void ShutterButtonPressed()
        {
            await SequentialOperation.StartStopRecording(
                new List<TargetDevice> { target },
                (result) =>
                {
                    switch (result)
                    {
                        case SequentialOperation.ShootingResult.StillSucceed:
                            ShowToast(SystemUtil.GetStringResource("Message_ImageCapture_Succeed"));
                            break;
                        case SequentialOperation.ShootingResult.StartSucceed:
                        case SequentialOperation.ShootingResult.StopSucceed:
                            break;
                        case SequentialOperation.ShootingResult.StillFailed:
                        case SequentialOperation.ShootingResult.StartFailed:
                            ShowError(SystemUtil.GetStringResource("ErrorMessage_shootingFailure"));
                            break;
                        case SequentialOperation.ShootingResult.StopFailed:
                            ShowError(SystemUtil.GetStringResource("ErrorMessage_fatal"));
                            break;
                        default:
                            break;
                    }
                });
        }

        private ToastNotification BuildToast(string str, StorageFile file = null)
        {
            ToastTemplateType template = ToastTemplateType.ToastImageAndText01;
            XmlDocument toastXml = ToastNotificationManager.GetTemplateContent(template);
            XmlNodeList toastTextElements = toastXml.GetElementsByTagName("text");
            toastTextElements[0].AppendChild(toastXml.CreateTextNode(str));

            var toastImageAttributes = toastXml.GetElementsByTagName("image");

            if (file == null)
            {
                ((XmlElement)toastImageAttributes[0]).SetAttribute("src", "ms-appx:///Assets/Toast/Locana_square_full.png");
            }
            else
            {
                ((XmlElement)toastImageAttributes[0]).SetAttribute("src", file.Path);
            }
            return new ToastNotification(toastXml);
        }

        private void ShowToast(string str, StorageFile file = null)
        {
            Debug.WriteLine("toast with image: " + str);
            var toast = BuildToast(str, file);
            ToastNotificationManager.CreateToastNotifier().Show(toast);
        }

        private void ShowError(string v)
        {
            Debug.WriteLine("error: " + v);
            ShowToast(v);
        }

        private async void ZoomOutButton_Click(object sender, RoutedEventArgs e)
        {
            try { await target.Api.Camera.ActZoomAsync(ZoomParam.DirectionOut, ZoomParam.ActionStop); }
            catch (RemoteApiException ex) { DebugUtil.Log(ex.StackTrace); }
        }

        private async void ZoomOutButton_Tapped(object sender, TappedRoutedEventArgs e)
        {
            try { await target.Api.Camera.ActZoomAsync(ZoomParam.DirectionOut, ZoomParam.Action1Shot); }
            catch (RemoteApiException ex) { DebugUtil.Log(ex.StackTrace); }
        }

        private async void ZoomOutButton_Holding(object sender, HoldingRoutedEventArgs e)
        {
            try { await target.Api.Camera.ActZoomAsync(ZoomParam.DirectionOut, ZoomParam.ActionStart); }
            catch (RemoteApiException ex) { DebugUtil.Log(ex.StackTrace); }
        }

        private async void ZoomInButton_Click(object sender, RoutedEventArgs e)
        {
            try { await target.Api.Camera.ActZoomAsync(ZoomParam.DirectionIn, ZoomParam.ActionStop); }
            catch (RemoteApiException ex) { DebugUtil.Log(ex.StackTrace); }
        }

        private async void ZoomInButton_Tapped(object sender, TappedRoutedEventArgs e)
        {
            try { await target.Api.Camera.ActZoomAsync(ZoomParam.DirectionIn, ZoomParam.Action1Shot); }
            catch (RemoteApiException ex) { DebugUtil.Log(ex.StackTrace); }
        }

        private async void ZoomInButton_Holding(object sender, HoldingRoutedEventArgs e)
        {
            try { await target.Api.Camera.ActZoomAsync(ZoomParam.DirectionIn, ZoomParam.ActionStart); }
            catch (RemoteApiException ex) { DebugUtil.Log(ex.StackTrace); }
        }

        private void Button_Click(object sender, RoutedEventArgs e)
        {
            ShutterButtonPressed();
        }

        private void ShutterButton_Tapped(object sender, TappedRoutedEventArgs e)
        {

        }

        private void ShutterButton_Holding(object sender, HoldingRoutedEventArgs e)
        {

        }

        private void Grid_Tapped(object sender, TappedRoutedEventArgs e)
        {
            OpenCloseSliders();
        }

        private void Grid_ManipulationCompleted(object sender, ManipulationCompletedRoutedEventArgs e)
        {
            OpenCloseSliders();
        }

        public void StartRotateAnimation(UIElement target, double from, double to)
        {
            var duration = new Duration(TimeSpan.FromMilliseconds(200));
            var sb = new Storyboard() { Duration = duration };
            var da = new DoubleAnimation() { Duration = duration };

            sb.Children.Add(da);

            var rt = new RotateTransform();

            Storyboard.SetTarget(da, rt);
            Storyboard.SetTargetProperty(da, "Angle");
            da.From = from;
            da.To = to;

            target.RenderTransform = rt;
            target.RenderTransformOrigin = new Point(0.5, 0.5);
            sb.Begin();
        }

        private void OpenCloseSliders()
        {
            if (ShootingParamSliderState == DisplayState.AlwaysVisible)
            {
                return;
            }

            if (ShootingParamSliders.Visibility == Visibility.Visible)
            {
                ShootingParamSliders.Visibility = Visibility.Collapsed;
                StartRotateAnimation(OpenSliderImage, 180, 0);
            }
            else
            {
                ShootingParamSliders.Visibility = Visibility.Visible;
                StartRotateAnimation(OpenSliderImage, 0, 180);
            }
        }

        DisplayState ShootingParamSliderState = DisplayState.AlwaysVisible;
        DisplayState ControlPanelState = DisplayState.AlwaysVisible;

        enum DisplayState
        {
            AlwaysVisible,
            Collapsible,
        }

        private void OpenCloseControlPanel()
        {
            if (ControlPanelState == DisplayState.AlwaysVisible)
            {
                return;
            }

            if (ControllPanelScroll.Visibility == Visibility.Visible)
            {
                ControllPanelScroll.Visibility = Visibility.Collapsed;
                StartRotateAnimation(OpenControlPanelImage, 180, 0);
            }
            else
            {
                ControllPanelScroll.Visibility = Visibility.Visible;
                StartRotateAnimation(OpenControlPanelImage, 0, 180);
            }
        }

        private void Grid_ManipulationCompleted_1(object sender, ManipulationCompletedRoutedEventArgs e)
        {
            OpenCloseControlPanel();
        }

        private void Grid_Tapped_1(object sender, TappedRoutedEventArgs e)
        {
            OpenCloseControlPanel();
        }
    }

}
