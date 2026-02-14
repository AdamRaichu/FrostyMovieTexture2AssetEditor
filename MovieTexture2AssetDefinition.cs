using Frosty.Controls;
using Frosty.Core;
using Frosty.Core.Controls;
using Frosty.Core.Windows;
using FrostySdk.Interfaces;
using FrostySdk.IO;
using FrostySdk.Managers;
using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using System.Reflection;
using System.Windows.Controls;
using System.Threading;
using LibVLCSharp.Shared;
using LibVLCSharp.WPF;
using static DuplicationPlugin.DuplicationTool;

using MediaPlayer = LibVLCSharp.Shared.MediaPlayer;

namespace MovieTexture2AssetEditorPlugin
{
    public class MovieTexture2DuplicationExtension : DuplicateAssetExtension
    {
        public override string AssetType => "MovieTexture2Asset";

        public override EbxAssetEntry DuplicateAsset(EbxAssetEntry entry, string newName, bool createNew, Type newType)
        {
            EbxAssetEntry refEntry = base.DuplicateAsset(entry, newName, createNew, newType);

            EbxAsset refAsset = App.AssetManager.GetEbx(refEntry);
            dynamic refRoot = refAsset.RootObject;

            ChunkAssetEntry webmChunk = App.AssetManager.GetChunkEntry(refRoot.ChunkGuid);
            ChunkAssetEntry newWebmChunk = DuplicateChunk(webmChunk);
            refRoot.ChunkGuid = newWebmChunk.Id;

            App.AssetManager.ModifyEbx(refEntry.Name, refAsset);

            return refEntry;
        }
    }

    public class MovieTextureDuplicationExtension : MovieTexture2DuplicationExtension
    {
        public override string AssetType => "MovieTextureAsset";
    }

    public class MovieTexture2AssetDefition : AssetDefinition
    {

        protected static ImageSource iconSource = new ImageSourceConverter().ConvertFromString("pack://application:,,,/FrostyCore;Component/Images/Assets/MovieTextureFileType.png") as ImageSource;

        public override FrostyAssetEditor GetEditor(ILogger logger)
        {
            return new MovieTexture2Editor(logger);
        }

        public override ImageSource GetIcon()
        {
            return iconSource;
        }
    }

    [TemplatePart(Name = "PART_VideoView", Type = typeof(VideoView))]
    [TemplatePart(Name = "PART_PlayButton", Type = typeof(Button))]
    [TemplatePart(Name = "PART_PauseButton", Type = typeof(Button))]
    [TemplatePart(Name = "PART_SeekSlider", Type = typeof(Slider))]
    [TemplatePart(Name = "PART_VolumeSlider", Type = typeof(Slider))]
    [TemplatePart(Name = "PART_AssetPropertyGrid", Type = typeof(FrostyPropertyGrid))]
    public class MovieTexture2Editor : FrostyAssetEditor
    {
        private VideoView videoView;
        private LibVLC libVLC;
        private MediaPlayer mediaPlayer;
        private Button playButton;
        private Button pauseButton;
        private Slider seekSlider;
        private Slider volumeSlider;
        private FrostyPropertyGrid propertyGrid;
        
        private string tempFilePath;
        private bool isSeeking;
        private long lastTime = -1;

        static MovieTexture2Editor()
        {
            DefaultStyleKeyProperty.OverrideMetadata(typeof(MovieTexture2Editor), new FrameworkPropertyMetadata(typeof(MovieTexture2Editor)));
        }

        public MovieTexture2Editor(ILogger logger) : base(logger)
        {
        }

        public override void OnApplyTemplate()
        {
            base.OnApplyTemplate();

            videoView = GetTemplateChild("PART_VideoView") as VideoView;
            playButton = GetTemplateChild("PART_PlayButton") as Button;
            pauseButton = GetTemplateChild("PART_PauseButton") as Button;
            seekSlider = GetTemplateChild("PART_SeekSlider") as Slider;
            volumeSlider = GetTemplateChild("PART_VolumeSlider") as Slider;
            propertyGrid = GetTemplateChild("PART_AssetPropertyGrid") as FrostyPropertyGrid;

            // Initialize LibVLC and MediaPlayer via Shared Method
            EnsureVlcInitialized();

            // Event Listeners
            if (playButton != null) playButton.Click += PlayButton_Click;
            if (pauseButton != null) pauseButton.Click += PauseButton_Click;
            
            if (seekSlider != null)
            {
                seekSlider.PreviewMouseDown += SeekSlider_PreviewMouseDown;
                seekSlider.PreviewMouseUp += SeekSlider_PreviewMouseUp;
                seekSlider.ValueChanged += SeekSlider_ValueChanged;
            }
            
            if (volumeSlider != null)
                volumeSlider.ValueChanged += VolumeSlider_ValueChanged;

            Loaded += MovieTexture2Editor_Loaded;
            Unloaded += MovieTexture2Editor_Unloaded;

            // Bind Property Grid
            if (propertyGrid != null)
            {
                propertyGrid.Object = asset.RootObject;
                propertyGrid.OnModified += PropertyGrid_OnModified;
            }
        }

        private void EnsureVlcInitialized()
        {
            if (libVLC == null)
            {
                // Ensure Core is initialized
                try 
                {
                    string assemblyLocation = Assembly.GetExecutingAssembly().Location;
                    string assemblyDir = Path.GetDirectoryName(assemblyLocation);
                    string libvlcPath = Path.Combine(assemblyDir, "libvlc", IntPtr.Size == 8 ? "win-x64" : "win-x86");
                    Core.Initialize(libvlcPath); 
                } 
                catch { }

                libVLC = new LibVLC();
                mediaPlayer = new MediaPlayer(libVLC);
                
                // Bind events
                mediaPlayer.LengthChanged += MediaPlayer_LengthChanged;
                mediaPlayer.TimeChanged += MediaPlayer_TimeChanged;
                mediaPlayer.EndReached += MediaPlayer_EndReached;
                
                if (videoView != null)
                    videoView.MediaPlayer = mediaPlayer;
            }
        }

        private void PropertyGrid_OnModified(object sender, ItemModifiedEventArgs e)
        {
            InvokeOnAssetModified();
        }

        private void MovieTexture2Editor_Loaded(object sender, RoutedEventArgs e)
        {
            EnsureVlcInitialized();

            if (tempFilePath == null)
            {
                ExtractVideo();
            }
            
            if (tempFilePath != null && File.Exists(tempFilePath))
            {
                using (Media media = new Media(libVLC, tempFilePath))
                {
                    mediaPlayer.Media = media;
                }
                
                // Restore timestamp if we have one
                if (lastTime > 0)
                {
                    mediaPlayer.Time = lastTime;
                }
            }
        }

        private void MovieTexture2Editor_Unloaded(object sender, RoutedEventArgs e)
        {
            if (mediaPlayer != null)
            {
                lastTime = mediaPlayer.Time;
                mediaPlayer.Stop();
                mediaPlayer.Dispose();
                mediaPlayer = null;
            }

            if (libVLC != null)
            {
                libVLC.Dispose();
                libVLC = null;
            }

            if (videoView != null)
            {
                videoView.MediaPlayer = null;
            }
            
            if (tempFilePath != null && File.Exists(tempFilePath))
            {
                try { File.Delete(tempFilePath); } catch { }
                tempFilePath = null;
            }
        }

        private void ExtractVideo()
        {
            try
            {
                dynamic root = asset.RootObject;
                Guid chunkGuid = (Guid)root.ChunkGuid;
                
                if (chunkGuid == Guid.Empty)
                    return;

                ChunkAssetEntry chunkEntry = App.AssetManager.GetChunkEntry(chunkGuid);
                if (chunkEntry == null)
                    return;

                Stream chunkStream = App.AssetManager.GetChunk(chunkEntry);
                if (chunkStream == null)
                    return;

                tempFilePath = Path.GetTempFileName().Replace(".tmp", ".webm");
                
                using (FileStream fs = new FileStream(tempFilePath, FileMode.Create))
                {
                    chunkStream.CopyTo(fs);
                }
            }
            catch (Exception ex)
            {
                logger.LogError("Failed to extract video: " + ex.Message);
            }
        }

        private void PlayButton_Click(object sender, RoutedEventArgs e)
        {
            mediaPlayer.Play();
        }

        private void PauseButton_Click(object sender, RoutedEventArgs e)
        {
            mediaPlayer.Pause();
        }

        private void SeekSlider_PreviewMouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            isSeeking = true;
        }

        private void SeekSlider_PreviewMouseUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            isSeeking = false;
            long time = (long)seekSlider.Value;
            mediaPlayer.Time = time;
        }

        private void SeekSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (isSeeking)
            {
                // Optional: Live seeking
                // mediaPlayer.Time = (long)e.NewValue; 
            }
        }

        private void VolumeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (mediaPlayer != null)
            {
                mediaPlayer.Volume = (int)e.NewValue;
            }
        }

        private void MediaPlayer_LengthChanged(object sender, MediaPlayerLengthChangedEventArgs e)
        {
            Dispatcher.Invoke(() =>
            {
                seekSlider.Maximum = e.Length;
            });
        }

        private void MediaPlayer_TimeChanged(object sender, MediaPlayerTimeChangedEventArgs e)
        {
            if (!isSeeking)
            {
                Dispatcher.Invoke(() =>
                {
                    seekSlider.Value = e.Time;
                });
            }
        }

        private void MediaPlayer_EndReached(object sender, EventArgs e)
        {
             // Loop? or Stop.
             ThreadPool.QueueUserWorkItem(_ => mediaPlayer?.Stop()); 
        }

        public override List<ToolbarItem> RegisterToolbarItems() {

            List<ToolbarItem> list = base.RegisterToolbarItems();
            list.Add(new ToolbarItem("Export", "Export Movie", "Images/Export.png", new RelayCommand((object state) => { ExportButton_Click(this, new RoutedEventArgs()); })));
            list.Add(new ToolbarItem("Import", "Import Movie", "Images/Import.png", new RelayCommand((object state) => { ImportButton_Click(this, new RoutedEventArgs()); })));
            return list;
        }

        private void ExportButton_Click(object sender, RoutedEventArgs e)
        {
            dynamic root = asset.RootObject;
            ChunkAssetEntry chunkAssetEntry = App.AssetManager.GetChunkEntry(root.ChunkGuid);

            FrostySaveFileDialog saveFileDialog = new FrostySaveFileDialog("Export Movie Asset", "WEBM (*.webm)|*.webm", "Movie", AssetEntry.Filename, false);
            bool result = false;
            while (true)
            {
                string initialDir = saveFileDialog.InitialDirectory;
                result = saveFileDialog.ShowDialog();

                if (result)
                {
                    FileInfo fileInfo = new FileInfo(saveFileDialog.FileName);
                    saveFileDialog.InitialDirectory = fileInfo.DirectoryName;

                    if (fileInfo.Exists)
                    {
                        if (FrostyMessageBox.Show(saveFileDialog.FileName + " already exists\r\nDo you want to replace it?", "Frosty Editor (Exporting Movie Asset)", MessageBoxButton.YesNo) == MessageBoxResult.Yes)
                            break;
                    }
                    else
                    {
                        break;
                    }
                }
            }

            if (!result)
            {
                return;
            }

            FrostyTaskWindow.Show("Exporting video", "Exporting video...", (task) =>
            {
                Stream chunkStream = App.AssetManager.GetChunk(chunkAssetEntry);
                if (chunkStream != null)
                {
                    using (NativeWriter writer = new NativeWriter(new FileStream(saveFileDialog.FileName, FileMode.Create)))
                    {
                        using (NativeReader reader = new NativeReader(chunkStream))
                            writer.Write(reader.ReadToEnd());
                    }
                }
                else
                {
                    logger.LogError("Failed to export chunk ${chunkAssetEntry.ChunkGuid}. Maybe it doesn't exist?");
                }
            });
            logger.Log("Exported Movie Asset to " + saveFileDialog.FileName);
        }

        private void ImportButton_Click(object sender, RoutedEventArgs e)
        {

            dynamic root = asset.RootObject;
            ChunkAssetEntry chunkAssetEntry = App.AssetManager.GetChunkEntry(root.ChunkGuid);

            FrostyOpenFileDialog openFileDialog = new FrostyOpenFileDialog("Import Movie Asset", "WEBM (*.webm)|*.webm", "Movie");
            if (openFileDialog.ShowDialog())
            {
                uint chunkSize = 0;
                FrostyTaskWindow.Show("Importing Video as Chunk", "Importing...", (task) =>
                {
                    using (NativeReader reader = new NativeReader(new FileStream(openFileDialog.FileName, FileMode.Open, FileAccess.Read)))
                    {
                        byte[] buffer = reader.ReadToEnd();
                        App.AssetManager.ModifyChunk(chunkAssetEntry.Id, buffer);
                        chunkSize = (uint)buffer.Length;
                    }
                });
                root.ChunkSize = chunkSize;
                App.AssetManager.ModifyEbx(AssetEntry.Name, asset);
                
                // Reload video
                if (tempFilePath != null)
                {
                    mediaPlayer.Stop();
                    // Extract new video
                    ExtractVideo();
                    if (tempFilePath != null && File.Exists(tempFilePath))
                    {
                         using (Media media = new Media(libVLC, tempFilePath))
                             mediaPlayer.Media = media;
                         mediaPlayer.Play();
                    }
                }

                // Refresh the property grid UI
                if (propertyGrid != null)
                     propertyGrid.Object = asset.RootObject;
                
                InvokeOnAssetModified();

                logger.Log($"Succesfully imported {AssetEntry.Filename}.");
            }
        }
    }
}
