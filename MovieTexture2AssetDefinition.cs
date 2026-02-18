using DuplicationPluginInjectorPlugin;
using Frosty.Controls;
using Frosty.Core;
//using FrostyEditor;
using Frosty.Core.Controls;
using Frosty.Core.Windows;
using FrostySdk.Interfaces;
using FrostySdk.IO;
using FrostySdk.Managers;
using LibVLCSharp.Shared;
using LibVLCSharp.WPF;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using static DuplicationPlugin.DuplicationTool;
using MediaPlayer = LibVLCSharp.Shared.MediaPlayer;

namespace MovieTexture2AssetEditorPlugin
{
    [RegisterDuplicationExtension]
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

    [RegisterDuplicationExtension]
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
    [TemplatePart(Name = "PART_PlaybackButton", Type = typeof(Button))]
    [TemplatePart(Name = "PART_SeekSlider", Type = typeof(Slider))]
    [TemplatePart(Name = "PART_VolumeSlider", Type = typeof(Slider))]
    [TemplatePart(Name = "PART_AssetPropertyGrid", Type = typeof(FrostyPropertyGrid))]
    public class MovieTexture2Editor : FrostyAssetEditor
    {
        private VideoView videoView;
        private LibVLC libVLC;
        private MediaPlayer mediaPlayer;
        private Button playbackButton;
        private Slider seekSlider;
        private Slider volumeSlider;
        private FrostyPropertyGrid propertyGrid;
        
        private string tempFilePath;
        private bool isSeeking;
        private long lastTime = -1;
        private bool wasPlaying;
        private int lastVolume = MoviePluginConfig.GetConfig().DefaultVolume;
        private bool isRestoring;

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
            playbackButton = GetTemplateChild("PART_PlaybackButton") as Button;
            seekSlider = GetTemplateChild("PART_SeekSlider") as Slider;
            volumeSlider = GetTemplateChild("PART_VolumeSlider") as Slider;
            propertyGrid = GetTemplateChild("PART_AssetPropertyGrid") as FrostyPropertyGrid;

            // Initialize LibVLC and MediaPlayer via Shared Method
            EnsureVlcInitialized();

            // Event Listeners
            if (playbackButton != null) playbackButton.Click += PlaybackButton_Click;
            
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
                    // Calculate path to ThirdParty\libvlc relative to FrostyEditor.exe
                    string assemblyLocation = Assembly.GetExecutingAssembly().Location;
                    string pluginsDir = Path.GetDirectoryName(assemblyLocation);
                    string frostyDir = Path.GetDirectoryName(pluginsDir); // Move up from Plugins folder
                    
                    // The user specified ThirdParty\libvlc
                    string thirdPartyDir = Path.Combine(frostyDir, "ThirdParty");
                    string libvlcPath = Path.Combine(thirdPartyDir, "libvlc");
                    string archPath = Path.Combine(libvlcPath, IntPtr.Size == 8 ? "win-x64" : "win-x86");

                    if (!Directory.Exists(archPath))
                    {
                        //FrostyTaskWindow.Show("Installing LibVLC (one-time)", "Extracting...", (task) => {
                        Directory.CreateDirectory(archPath);
                        ExtractLibVlcResources(archPath);
                        App.Logger.Log("Successfully installed libvlc native libraries to ThirdParty/libvlc. Movie load times will be faster in the future.");
                        //});
                    }

                    Core.Initialize(archPath); 
                } 
                catch (Exception ex)
                {
                    logger.Log($"Failed to initialize LibVLC: {ex.Message}");
                }

                libVLC = new LibVLC();
                mediaPlayer = new MediaPlayer(libVLC);
                
                // Bind events
                mediaPlayer.LengthChanged += MediaPlayer_LengthChanged;
                mediaPlayer.TimeChanged += MediaPlayer_TimeChanged;
                mediaPlayer.EndReached += MediaPlayer_EndReached;
                mediaPlayer.Playing += MediaPlayer_Playing;
                mediaPlayer.Paused += MediaPlayer_Paused;
                mediaPlayer.Stopped += MediaPlayer_Stopped;
                
                if (videoView != null)
                {
                    videoView.MediaPlayer = mediaPlayer;
                    if (videoView.Parent is FrameworkElement parent)
                    {
                        parent.SizeChanged += (s, ev) => UpdateVideoDimensions();
                    }
                }

                // Restore volume
                mediaPlayer.Volume = lastVolume;
                if (volumeSlider != null)
                {
                    volumeSlider.Value = lastVolume;
                }
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
                isRestoring = true;
                // Options: start-pause ensures it opens and primes a frame but stays paused
                string[] options = wasPlaying ? null : new string[] { ":start-pause" };
                using (Media media = new Media(libVLC, tempFilePath, FromType.FromPath, options))
                {
                    mediaPlayer.Media = media;
                    mediaPlayer.Play();
                    
                    // Note: We don't restore Time here. 
                    // Restoration is now handled in MediaPlayer_Playing.
                }
            }
        }

        private void MovieTexture2Editor_Unloaded(object sender, RoutedEventArgs e)
        {
            if (mediaPlayer != null)
            {
                lastTime = mediaPlayer.Time;
                wasPlaying = mediaPlayer.IsPlaying;
                lastVolume = mediaPlayer.Volume;
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

        private void PlaybackButton_Click(object sender, RoutedEventArgs e)
        {
            if (mediaPlayer.IsPlaying)
            {
                mediaPlayer.Pause();
            }
            else
            {
                mediaPlayer.Play();
            }
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

        private void MediaPlayer_Playing(object sender, EventArgs e)
        {
            Dispatcher.Invoke(() => 
            {
                if (playbackButton != null) playbackButton.Content = "⏸"; 
                UpdateVideoDimensions();
            });

            if (isRestoring)
            {
                isRestoring = false;
                
                long timeToRestore = lastTime;
                lastTime = -1; // Reset to prevent double seek
                
                ThreadPool.QueueUserWorkItem(_ => 
                {
                    Thread.Sleep(100); // Small buffer for native initialization
                    
                    if (timeToRestore > 0)
                    {
                        mediaPlayer.Time = timeToRestore;
                    }
                    
                    if (!wasPlaying)
                    {
                        mediaPlayer.Pause();
                    }
                });
            }
        }

        private void MediaPlayer_Paused(object sender, EventArgs e)
        {
            Dispatcher.Invoke(() => { if (playbackButton != null) playbackButton.Content = "▶"; });
        }

        private void MediaPlayer_Stopped(object sender, EventArgs e)
        {
            Dispatcher.Invoke(() => { if (playbackButton != null) playbackButton.Content = "▶"; });
        }

        private void MediaPlayer_EndReached(object sender, EventArgs e)
        {
             // Reset restoration state
             lastTime = 0;
             wasPlaying = false;
             
             // Loop? or Stop.
             ThreadPool.QueueUserWorkItem(_ => mediaPlayer?.Stop()); 
        }

        private void UpdateVideoDimensions()
        {
            if (mediaPlayer == null || videoView == null) return;

            uint width = 0;
            uint height = 0;
            mediaPlayer.Size(0, ref width, ref height);

            if (width > 0 && height > 0 && videoView.Parent is FrameworkElement parent)
            {
                double ratio = (double)width / height;
                double parentWidth = parent.ActualWidth;
                double parentHeight = parent.ActualHeight;

                if (parentWidth <= 0 || parentHeight <= 0) return;

                if (parentWidth / parentHeight > ratio)
                {
                    videoView.Height = parentHeight;
                    videoView.Width = parentHeight * ratio;
                }
                else
                {
                    videoView.Width = parentWidth;
                    videoView.Height = parentWidth / ratio;
                }
            }
        }

        public override List<ToolbarItem> RegisterToolbarItems() {

            List<ToolbarItem> list = base.RegisterToolbarItems();
            list.Add(new ToolbarItem("Export", "Export Movie", "Images/Export.png", new RelayCommand((object state) => { ExportButton_Click(this, new RoutedEventArgs()); })));
            list.Add(new ToolbarItem("Import", "Import Movie", "Images/Import.png", new RelayCommand((object state) => { ImportButton_Click(this, new RoutedEventArgs()); })));
            list.Add(new ToolbarItem("Revert", "Revert Chunk/Ebx", "Images/Revert.png", new RelayCommand((object state) => { RevertButton_Click(this, new RoutedEventArgs()); })));
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
                        if (MoviePluginConfig.GetConfig().AutoplayOnImport)
                        {
                            mediaPlayer.Play();
                        }
                    }
                }

                // Refresh the property grid UI
                if (propertyGrid != null)
                     propertyGrid.Object = asset.RootObject;
                
                InvokeOnAssetModified();

                logger.Log($"Succesfully imported {AssetEntry.Filename}.");

                // Update the UI
                RefreshChunkSizeInPropertyGrid(chunkSize);
            }
        }

        private void RevertButton_Click(object sender, EventArgs e)
        {
            if (!(sender is MovieTexture2Editor editor))
                return;

            // Capture the AssetEntry and its Name on the UI thread
            // This prevents threading errors when trying to access them from the background task
            AssetEntry entry = editor.AssetEntry;
            string assetName = entry.Name;

            dynamic chunkGuid = ((dynamic)editor.Asset.RootObject).ChunkGuid;
            if (!entry.IsAdded) { 
                // Get unmodified data to make sure ChunkGuid is default.
                EbxAsset unmodifiedData = App.AssetManager.GetEbx(assetName, true);
            }
            App.AssetManager.RevertAsset(App.AssetManager.GetChunkEntry(chunkGuid));

            // FrostyTaskWindow.Show runs the action on a background thread.
            // We MUST use the captured 'entry' variable here.
            FrostyTaskWindow.Show("Reverting Asset", "", (task) => 
            { 
                App.AssetManager.RevertAsset(entry, suppressOnModify: false); 
            });

            // Find the parent tab and close it via the MainWindow
            if (Application.Current.MainWindow is FrostyEditor.MainWindow mW)
            {
                // Use reflection to access the private tabControl, as it's not exposed publicly in MainWindow
                FrostyTabControl tabControl = (FrostyTabControl)GetInstanceField(mW.GetType(), mW, "tabControl");
                if (tabControl != null)
                {
                    foreach (FrostyTabItem currentTi in tabControl.Items)
                    {
                        if (currentTi.TabId == assetName)
                        {
                            mW.ShutdownEditorAndRemoveTab(editor, currentTi);
                            return;
                        }
                    }
                }
            }
        }

        private void RefreshChunkSizeInPropertyGrid(uint chunkSize) {
            //propertyGrid.Items
            ObservableCollection<FrostyPropertyGridItemData> items = (ObservableCollection<FrostyPropertyGridItemData>)GetInstanceField(propertyGrid.GetType(), propertyGrid, "items");
            foreach (var category in items)
            {
                foreach (var item in category.Children)
                {
                    if (item.Name == "ChunkSize")
                    {
                        item.Value = chunkSize; break;
                    }
                }
            }
        }

        // Source - https://stackoverflow.com/a/3303182
        // Posted by dcp, modified by community. See post 'Timeline' for change history
        // Retrieved 2026-02-16, License - CC BY-SA 2.5

        /// <summary>
        /// Uses reflection to get the field value from an object.
        /// </summary>
        ///
        /// <param name="type">The instance type.</param>
        /// <param name="instance">The instance object.</param>
        /// <param name="fieldName">The field's name which is to be fetched.</param>
        ///
        /// <returns>The field value from the object.</returns>
        internal static object GetInstanceField(Type type, object instance, string fieldName)
        {
            BindingFlags bindFlags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic
                | BindingFlags.Static;
            FieldInfo field = type.GetField(fieldName, bindFlags);
            return field.GetValue(instance);
        }

        private void ExtractLibVlcResources(string targetPath)
        {
            Assembly assembly = Assembly.GetExecutingAssembly();
            string resourcePrefix = "MovieTexture2AssetEditorPlugin.LibVlc.";

            foreach (string resourceName in assembly.GetManifestResourceNames())
            {
                if (resourceName.StartsWith(resourcePrefix))
                {
                    // Relative path from the LibVlc resource root
                    string relativePath = resourceName.Substring(resourcePrefix.Length);
                    
                    // Resource names replace slashes with dots. 
                    // We need to reconstruct the file path.
                    // LibVLC structure is:
                    // libvlc.dll
                    // libvlccore.dll
                    // plugins/[category]/[plugin].dll
                    
                    string fileName = "";
                    string subDir = "";

                    if (relativePath.StartsWith("plugins."))
                    {
                        // e.g., plugins.access.libaccess_plugin.dll
                        // We expect: plugins\access\libaccess_plugin.dll
                        string[] parts = relativePath.Split('.');
                        // last 2 parts are [filename] and [dll]
                        if (parts.Length >= 4)
                        {
                            fileName = parts[parts.Length - 2] + "." + parts[parts.Length - 1];
                            // Everything between "plugins" and the filename
                            List<string> dirParts = new List<string>();
                            for (int i = 0; i < parts.Length - 2; i++)
                            {
                                dirParts.Add(parts[i]);
                            }
                            subDir = Path.Combine(dirParts.ToArray());
                        }
                    }
                    else
                    {
                        // root files like libvlc.dll
                        fileName = relativePath;
                    }

                    if (!string.IsNullOrEmpty(fileName))
                    {
                        string fullDir = Path.Combine(targetPath, subDir);
                        if (!Directory.Exists(fullDir)) Directory.CreateDirectory(fullDir);
                        
                        WriteResourceToFile(assembly, resourceName, Path.Combine(fullDir, fileName));
                    }
                }
            }
        }

        private void WriteResourceToFile(Assembly assembly, string resourceName, string fileName)
        {
            using (Stream stream = assembly.GetManifestResourceStream(resourceName))
            {
                if (stream == null) return;
                using (FileStream fileStream = new FileStream(fileName, FileMode.Create))
                {
                    stream.CopyTo(fileStream);
                }
            }
        }
    }
}
