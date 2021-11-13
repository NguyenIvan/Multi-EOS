using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using EOSDigital.API;
using EOSDigital.SDK;
using System.Threading;

namespace WpfExample
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        #region Variables

        CanonAPI APIHandler;
        Camera MainCamera;
        CameraValue[] AvList;
        CameraValue[] TvList;
        CameraValue[] ISOList;
        List<Camera> CamList;
        bool IsInit = false;
        int BulbTime = 30;
        ImageBrush bgbrush = new ImageBrush();
        Action<BitmapImage> SetImageAction;
        System.Windows.Forms.FolderBrowserDialog SaveFolderBrowser = new System.Windows.Forms.FolderBrowserDialog();

        int ErrCount;
        object ErrLock = new object();
        float _wait = 1.0f;
        MediaPlayer _player = new MediaPlayer();
        const int TIT_LENGTH = 1000; // In milliseconds
        private int _sleepTime;
        private bool _inSession = false;
        TextBoxOutputter debugConsole;

        #endregion

        public MainWindow()
        {
            try
            {

                InitializeComponent();

                debugConsole = new TextBoxOutputter(DebugConsole);

                NumberOfCaptures = 1;
                WaitSeconds = 1;
                _player.Open(new Uri("Resources/tit.wav", UriKind.Relative));
                _player.MediaEnded += _player_MediaEnded;

                APIHandler = new CanonAPI();
                APIHandler.CameraAdded += APIHandler_CameraAdded;
                ErrorHandler.SevereErrorHappened += ErrorHandler_SevereErrorHappened;
                ErrorHandler.NonSevereErrorHappened += ErrorHandler_NonSevereErrorHappened;
                SavePathTextBox.Text = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "RemotePhoto");
                SetImageAction = (BitmapImage img) => { bgbrush.ImageSource = img; };
                SaveFolderBrowser.Description = "Save Images To...";
                RefreshCamera();
                IsInit = true;

            }
            catch (DllNotFoundException) { ReportError("Canon DLLs not found!", true); }
            catch (Exception ex) { ReportError(ex.Message, true); }
        }

        public int NumberOfCaptures { get; set; }

        public float WaitSeconds { get { return _wait; } set { if (value >= 1.0) { _wait = value; } } }

        private void Window_Closing(object sender, CancelEventArgs e)
        {
            try
            {
                IsInit = false;
                MainCamera?.Dispose();
                APIHandler?.Dispose();
            }
            catch (Exception ex) { ReportError(ex.Message, false); }
        }

        #region API Events

        private void APIHandler_CameraAdded(CanonAPI sender)
        {
            try { Dispatcher.Invoke((Action)delegate { RefreshCamera(); }); }
            catch (Exception ex) { ReportError(ex.Message, false); }
        }

        private void MainCamera_StateChanged(Camera sender, StateEventID eventID, int parameter)
        {
            try { if (eventID == StateEventID.Shutdown && IsInit) { Dispatcher.Invoke((Action)delegate { CloseSession(); }); } }
            catch (Exception ex) { ReportError(ex.Message, false); }
        }

        private void MainCamera_ProgressChanged(object sender, int progress)
        {
            try { MainProgressBar.Dispatcher.Invoke((Action)delegate { MainProgressBar.Value = progress; }); }
            catch (Exception ex) { ReportError(ex.Message, false); }
        }

        private void MainCamera_LiveViewUpdated(Camera sender, Stream img)
        {
            try
            {
                using (WrapStream s = new WrapStream(img))
                {
                    img.Position = 0;
                    BitmapImage EvfImage = new BitmapImage();
                    EvfImage.BeginInit();
                    EvfImage.StreamSource = s;
                    EvfImage.CacheOption = BitmapCacheOption.OnLoad;
                    EvfImage.EndInit();
                    EvfImage.Freeze();
                    Application.Current.Dispatcher.BeginInvoke(SetImageAction, EvfImage);
                }
            }
            catch (Exception ex) { ReportError(ex.Message, false); }
        }

        private void MainCamera_DownloadReady(Camera sender, DownloadInfo Info)
        {
            try
            {
                string dir = null;
                SavePathTextBox.Dispatcher.Invoke((Action)delegate { dir = SavePathTextBox.Text; });
                sender.DownloadFile(Info, dir);
                MainProgressBar.Dispatcher.Invoke((Action)delegate { MainProgressBar.Value = 0; });
            }
            catch (Exception ex) { ReportError(ex.Message, false); }
        }

        private void ErrorHandler_NonSevereErrorHappened(object sender, ErrorCode ex)
        {
            ReportError($"SDK Error code: {ex} ({((int)ex).ToString("X")})", false);
        }

        private void ErrorHandler_SevereErrorHappened(object sender, Exception ex)
        {
            ReportError(ex.Message, true);
        }

        #endregion

        #region Session

        private void OpenAllButton_Click(object sender, RoutedEventArgs e)
        {
            OpenAll();
        }

        private void CloseAllButton_Click(object sender, RoutedEventArgs e)
        {
            CloseAll();
        }

        private void OpenSessionButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (MainCamera?.SessionOpen == true) CloseSession();
                else OpenSession();
            }
            catch (Exception ex) { ReportError(ex.Message, false); }
        }

        private void RefreshButton_Click(object sender, RoutedEventArgs e)
        {
            try { RefreshCamera(); }
            catch (Exception ex) { ReportError(ex.Message, false); }
        }

        #endregion

        #region Settings

        private void AvCoBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            try
            {
                if (AvCoBox.SelectedIndex < 0) return;
                MainCamera.SetSetting(PropertyID.Av, AvValues.GetValue((string)AvCoBox.SelectedItem).IntValue);
            }
            catch (Exception ex) { ReportError(ex.Message, false); }
        }

        private void TvCoBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            try
            {
                if (TvCoBox.SelectedIndex < 0) return;

                MainCamera.SetSetting(PropertyID.Tv, TvValues.GetValue((string)TvCoBox.SelectedItem).IntValue);
                if ((string)TvCoBox.SelectedItem == "Bulb")
                {
                    BulbBox.IsEnabled = true;
                    BulbSlider.IsEnabled = true;
                }
                else
                {
                    BulbBox.IsEnabled = false;
                    BulbSlider.IsEnabled = false;
                }
            }
            catch (Exception ex) { ReportError(ex.Message, false); }
        }

        private void ISOCoBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            try
            {
                if (ISOCoBox.SelectedIndex < 0) return;
                MainCamera.SetSetting(PropertyID.ISO, ISOValues.GetValue((string)ISOCoBox.SelectedItem).IntValue);
            }
            catch (Exception ex) { ReportError(ex.Message, false); }
        }

        private void TakePhotoButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                //if ((string)TvCoBox.SelectedItem == "Bulb") MainCamera.TakePhotoBulbAsync(BulbTime);
                //else MainCamera.TakePhotoAsync();
                //Calculate the sleep time
                _sleepTime = Math.Max((int)(WaitSeconds * 1000) - TIT_LENGTH, 0);
                for (int i = 0; i < NumberOfCaptures; i++)
                {
                    Thread.Sleep(_sleepTime);
                    _player.Position = TimeSpan.FromMilliseconds(1);
                    _player.Play();
                    Thread.Sleep(TIT_LENGTH);
                    //MainCamera.TakePhotoAsync();
                    foreach (Camera cam in CamList)
                    {
                        if (cam.SessionOpen)
                        {
                            cam.TakePhotoAsync();
                        }
                    }
                }

            }
            catch (Exception ex) { ReportError(ex.Message, false); }
        }


        void _player_MediaEnded(object sender, EventArgs e)
        {
            
        }

        private void VideoButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Recording state = (Recording)MainCamera.GetInt32Setting(PropertyID.Record);
                if (state != Recording.On)
                {
                    MainCamera.StartFilming(true);
                    VideoButtonText.Inlines.Clear();
                    VideoButtonText.Inlines.Add("Stop");
                    VideoButtonText.Inlines.Add(new LineBreak());
                    VideoButtonText.Inlines.Add("Video");
                }
                else
                {
                    bool save = (bool)STComputerRdButton.IsChecked || (bool)STBothRdButton.IsChecked;
                    MainCamera.StopFilming(save);
                    VideoButtonText.Inlines.Clear();
                    VideoButtonText.Inlines.Add("Record");
                    VideoButtonText.Inlines.Add(new LineBreak());
                    VideoButtonText.Inlines.Add("Video");
                }
            }
            catch (Exception ex) { ReportError(ex.Message, false); }
        }

        private void BulbSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            try { if (IsInit) BulbBox.Text = BulbSlider.Value.ToString(); }
            catch (Exception ex) { ReportError(ex.Message, false); }
        }

        private void BulbBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            try
            {
                if (IsInit)
                {
                    int b;
                    if (int.TryParse(BulbBox.Text, out b) && b != BulbTime)
                    {
                        BulbTime = b;
                        BulbSlider.Value = BulbTime;
                    }
                    else BulbBox.Text = BulbTime.ToString();
                }
            }
            catch (Exception ex) { ReportError(ex.Message, false); }
        }

        private void SaveToRdButton_Checked(object sender, RoutedEventArgs e)
        {
            try
            {
                if (IsInit)
                {
                    if ((bool)STCameraRdButton.IsChecked)
                    {
                        MainCamera.SetSetting(PropertyID.SaveTo, (int)SaveTo.Camera);
                        BrowseButton.IsEnabled = false;
                        SavePathTextBox.IsEnabled = false;
                    }
                    else
                    {
                        if ((bool)STComputerRdButton.IsChecked) MainCamera.SetSetting(PropertyID.SaveTo, (int)SaveTo.Host);
                        else if ((bool)STBothRdButton.IsChecked) MainCamera.SetSetting(PropertyID.SaveTo, (int)SaveTo.Both);

                        MainCamera.SetCapacity(4096, int.MaxValue);
                        BrowseButton.IsEnabled = true;
                        SavePathTextBox.IsEnabled = true;
                    }
                }
            }
            catch (Exception ex) { ReportError(ex.Message, false); }
        }

        private void BrowseButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (Directory.Exists(SavePathTextBox.Text)) SaveFolderBrowser.SelectedPath = SavePathTextBox.Text;
                if (SaveFolderBrowser.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                {
                    SavePathTextBox.Text = SaveFolderBrowser.SelectedPath;
                }
            }
            catch (Exception ex) { ReportError(ex.Message, false); }
        }

        #endregion

        #region Live view

        private void StarLVButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                debugConsole.WriteLine("Starting Live View on {0}, index {1}", MainCamera.DeviceName, CameraListBox.SelectedIndex);
                if (!MainCamera.IsLiveViewOn)
                {
                    LVCanvas.Background = bgbrush;
                    MainCamera.StartLiveView();
                    StarLVButton.Content = "Stop LV";
                }
                else
                {
                    MainCamera.StopLiveView();
                    StarLVButton.Content = "Start LV";
                    LVCanvas.Background = Brushes.LightGray;
                }
            }
            catch (Exception ex) { ReportError(ex.Message, false); }
        }

        private void FocusNear3Button_Click(object sender, RoutedEventArgs e)
        {
            try { MainCamera.SendCommand(CameraCommand.DriveLensEvf, (int)DriveLens.Near3); }
            catch (Exception ex) { ReportError(ex.Message, false); }
        }

        private void FocusNear2Button_Click(object sender, RoutedEventArgs e)
        {
            try { MainCamera.SendCommand(CameraCommand.DriveLensEvf, (int)DriveLens.Near2); }
            catch (Exception ex) { ReportError(ex.Message, false); }
        }

        private void FocusNear1Button_Click(object sender, RoutedEventArgs e)
        {
            try { MainCamera.SendCommand(CameraCommand.DriveLensEvf, (int)DriveLens.Near1); }
            catch (Exception ex) { ReportError(ex.Message, false); }
        }

        private void FocusFar1Button_Click(object sender, RoutedEventArgs e)
        {
            try { MainCamera.SendCommand(CameraCommand.DriveLensEvf, (int)DriveLens.Far1); }
            catch (Exception ex) { ReportError(ex.Message, false); }
        }

        private void FocusFar2Button_Click(object sender, RoutedEventArgs e)
        {
            try { MainCamera.SendCommand(CameraCommand.DriveLensEvf, (int)DriveLens.Far2); }
            catch (Exception ex) { ReportError(ex.Message, false); }
        }

        private void FocusFar3Button_Click(object sender, RoutedEventArgs e)
        {
            try { MainCamera.SendCommand(CameraCommand.DriveLensEvf, (int)DriveLens.Far3); }
            catch (Exception ex) { ReportError(ex.Message, false); }
        }

        #endregion

        #region Subroutines

        private void CloseSession()
        {
            MainCamera.CloseSession();
            AvCoBox.Items.Clear();
            TvCoBox.Items.Clear();
            ISOCoBox.Items.Clear();
            SettingsGroupBox.IsEnabled = false;
            LiveViewGroupBox.IsEnabled = false;
            SessionButton.Content = "Open Session";
            SessionLabel.Content = "No open session";
            StarLVButton.Content = "Start LV";
        }

        private void RefreshCamera()
        {
            CameraListBox.Items.Clear();
            CamList = APIHandler.GetCameraList();
            int count = 0;
            foreach (Camera cam in CamList) {
                CameraListBox.Items.Add(cam.DeviceName + count++);
            }
            if (MainCamera?.SessionOpen == true) CameraListBox.SelectedIndex = CamList.FindIndex(t => t.ID == MainCamera.ID);
            else if (CamList.Count > 0) CameraListBox.SelectedIndex = 0;
        }
        private void CloseAll()
        {
            // All cameras are supposed to be in sessions, we need to close them all
            foreach (Camera camera in CamList)
            {
                if (camera.SessionOpen)
                {
                    camera.CloseSession();
                }
            }
            OpenAllButton.Visibility = Visibility.Visible;
            CloseAllButton.Visibility = Visibility.Hidden;
            CleanUpMainCamera();
            _inSession = false;
        }
        private void OpenAll()
        {
            // All cameras are supposed to be in sessions, we need to close them all
            foreach (Camera camera in CamList)
            {
                if (!camera.SessionOpen)
                {
                    camera.OpenSession();
                }
            }
            OpenAllButton.Visibility = Visibility.Hidden;
            CloseAllButton.Visibility = Visibility.Visible;
            if (CameraListBox.SelectedIndex >= 0)
            {
                SetupMainCamera(CamList[CameraListBox.SelectedIndex]);
            }
            _inSession = true;
        }


        private void SetupMainCamera(Camera camera)
        {
            MainCamera = camera;
            MainCamera.LiveViewUpdated += MainCamera_LiveViewUpdated;
            MainCamera.ProgressChanged += MainCamera_ProgressChanged;
            MainCamera.StateChanged += MainCamera_StateChanged;

            // Need to listen to all cameras ready events
            MainCamera.DownloadReady += MainCamera_DownloadReady;

            SessionLabel.Content = MainCamera.DeviceName;
            AvList = MainCamera.GetSettingsList(PropertyID.Av);
            TvList = MainCamera.GetSettingsList(PropertyID.Tv);
            ISOList = MainCamera.GetSettingsList(PropertyID.ISO);
            foreach (var Av in AvList) AvCoBox.Items.Add(Av.StringValue);
            foreach (var Tv in TvList) TvCoBox.Items.Add(Tv.StringValue);
            foreach (var ISO in ISOList) ISOCoBox.Items.Add(ISO.StringValue);
            AvCoBox.SelectedIndex = AvCoBox.Items.IndexOf(AvValues.GetValue(MainCamera.GetInt32Setting(PropertyID.Av)).StringValue);
            TvCoBox.SelectedIndex = TvCoBox.Items.IndexOf(TvValues.GetValue(MainCamera.GetInt32Setting(PropertyID.Tv)).StringValue);
            ISOCoBox.SelectedIndex = ISOCoBox.Items.IndexOf(ISOValues.GetValue(MainCamera.GetInt32Setting(PropertyID.ISO)).StringValue);
        }

        private void CleanUpMainCamera()
        {
            if (MainCamera != null)
            {
                MainCamera.LiveViewUpdated -= MainCamera_LiveViewUpdated;
                MainCamera.ProgressChanged -= MainCamera_ProgressChanged;
                MainCamera.StateChanged -= MainCamera_StateChanged;
                MainCamera.DownloadReady -= MainCamera_DownloadReady;
                // Need to listen to all cameras ready events
            }

            AvCoBox.Items.Clear();
            TvCoBox.Items.Clear();
            ISOCoBox.Items.Clear();
            StarLVButton.Content = "Start LV";
        }

        private void OpenSession()
        {
            if (CameraListBox.SelectedIndex >= 0)
            {
                MainCamera = CamList[CameraListBox.SelectedIndex];
                MainCamera.OpenSession();
                MainCamera.LiveViewUpdated += MainCamera_LiveViewUpdated;
                MainCamera.ProgressChanged += MainCamera_ProgressChanged;
                MainCamera.StateChanged += MainCamera_StateChanged;
                MainCamera.DownloadReady += MainCamera_DownloadReady;

                SessionButton.Content = "Close Session";
                SessionLabel.Content = MainCamera.DeviceName;
                AvList = MainCamera.GetSettingsList(PropertyID.Av);
                TvList = MainCamera.GetSettingsList(PropertyID.Tv);
                ISOList = MainCamera.GetSettingsList(PropertyID.ISO);
                foreach (var Av in AvList) AvCoBox.Items.Add(Av.StringValue);
                foreach (var Tv in TvList) TvCoBox.Items.Add(Tv.StringValue);
                foreach (var ISO in ISOList) ISOCoBox.Items.Add(ISO.StringValue);
                AvCoBox.SelectedIndex = AvCoBox.Items.IndexOf(AvValues.GetValue(MainCamera.GetInt32Setting(PropertyID.Av)).StringValue);
                TvCoBox.SelectedIndex = TvCoBox.Items.IndexOf(TvValues.GetValue(MainCamera.GetInt32Setting(PropertyID.Tv)).StringValue);
                ISOCoBox.SelectedIndex = ISOCoBox.Items.IndexOf(ISOValues.GetValue(MainCamera.GetInt32Setting(PropertyID.ISO)).StringValue);
                SettingsGroupBox.IsEnabled = true;
                LiveViewGroupBox.IsEnabled = true;
            }
        }

        private void ReportError(string message, bool lockdown)
        {
            int errc;
            lock (ErrLock) { errc = ++ErrCount; }

            if (lockdown) EnableUI(false);

            if (errc < 4) MessageBox.Show(message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            else if (errc == 4) MessageBox.Show("Many errors happened!", "Error", MessageBoxButton.OK, MessageBoxImage.Error);

            lock (ErrLock) { ErrCount--; }
        }

        private void EnableUI(bool enable)
        {
            if (!Dispatcher.CheckAccess()) Dispatcher.Invoke((Action)delegate { EnableUI(enable); });
            else
            {
                SettingsGroupBox.IsEnabled = enable;
                InitGroupBox.IsEnabled = enable;
                LiveViewGroupBox.IsEnabled = enable;
            }
        }

        #endregion

        private void CameraListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (CameraListBox.SelectedIndex > -1)
            {
                Camera selectedCamera = CamList[CameraListBox.SelectedIndex];

                if (selectedCamera != null && selectedCamera.SessionOpen)
                {
                    debugConsole.WriteLine("Main Camera Selection Changed to {0}, index {1}", selectedCamera.DeviceName, CameraListBox.SelectedIndex);
                    CleanUpMainCamera();
                    SetupMainCamera(selectedCamera);
                }
            }
        }
    }
}
