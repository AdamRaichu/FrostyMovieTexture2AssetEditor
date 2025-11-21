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
using static DuplicationPlugin.DuplicationTool;

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

    public class MovieTexture2Editor : FrostyAssetEditor
    {
        public MovieTexture2Editor(ILogger logger) : base(logger)
        {
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
                // Refresh the property grid UI
                FrostyPropertyGrid pg = (GetTemplateChild("PART_AssetPropertyGrid") as FrostyPropertyGrid);
                pg.Object = asset.RootObject;
                InvokeOnAssetModified();

                logger.Log($"Succesfully imported {AssetEntry.Filename}. (ChunkSize was updated automatically; refresh to see changes.)");
            }
        }
    }
}
